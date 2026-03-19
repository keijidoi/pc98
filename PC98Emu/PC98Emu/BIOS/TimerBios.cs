using PC98Emu.CPU;
using PC98Emu.Bus;

namespace PC98Emu.BIOS;

public class TimerBios
{
    private readonly V30 _cpu;
    private readonly SystemBus _bus;

    public TimerBios(V30 cpu, SystemBus bus)
    {
        _cpu = cpu;
        _bus = bus;
    }

    public void Handle()
    {
        byte func = _cpu.AH;
        switch (func)
        {
            case 0x00:
                ReadTickCount();
                break;
            case 0x02:
                ReadTime();
                break;
            case 0x04:
                ReadDate();
                break;
            case 0x01:
                // Set tick count - accept and ignore
                _cpu.AH = 0;
                break;
            case 0x03:
                // Set RTC time - accept and ignore
                _cpu.AH = 0;
                break;
            case 0x05:
                // Set RTC date - accept and ignore
                _cpu.AH = 0;
                break;
            case 0x10:
                // Function 10h: used by MS-DOS kernel during init - return success
                _cpu.AH = 0;
                break;
            default:
                Console.Error.WriteLine($"[TIMER] Unhandled INT 1Ah AH={func:X2} AL={_cpu.AL:X2} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} CS={_cpu.CS:X4} IP={_cpu.IP:X4}");
                _cpu.AH = 0;
                break;
        }
    }

    private void ReadTickCount()
    {
        var now = DateTime.Now;
        var midnight = now.Date;
        double secondsSinceMidnight = (now - midnight).TotalSeconds;
        uint ticks = (uint)(secondsSinceMidnight * 18.2);
        _cpu.CX = (ushort)(ticks >> 16);
        _cpu.DX = (ushort)(ticks & 0xFFFF);
        _cpu.AH = 0;
    }

    private void ReadTime()
    {
        var now = DateTime.Now;
        _cpu.CH = ToBCD(now.Hour);
        _cpu.CL = ToBCD(now.Minute);
        _cpu.DH = ToBCD(now.Second);
        _cpu.DL = 0;
        _cpu.AH = 0;
    }

    private void ReadDate()
    {
        var now = DateTime.Now;
        _cpu.CX = (ushort)((ToBCD(now.Year / 100) << 8) | ToBCD(now.Year % 100));
        _cpu.DH = ToBCD(now.Month);
        _cpu.DL = ToBCD(now.Day);
        _cpu.AH = 0;
    }

    private static byte ToBCD(int value) => (byte)(((value / 10) << 4) | (value % 10));
}
