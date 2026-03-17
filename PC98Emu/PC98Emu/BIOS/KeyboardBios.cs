using PC98Emu.CPU;
using PC98Emu.Bus;
using PC98Emu.Devices;

namespace PC98Emu.BIOS;

public class KeyboardBios
{
    private readonly V30 _cpu;
    private readonly SystemBus _bus;
    private readonly Keyboard _keyboard;

    public KeyboardBios(V30 cpu, SystemBus bus, Keyboard keyboard)
    {
        _cpu = cpu;
        _bus = bus;
        _keyboard = keyboard;
    }

    public void Handle()
    {
        byte func = _cpu.AH;
        switch (func)
        {
            case 0x00:
                WaitForKey();
                break;
            case 0x01:
                CheckKey();
                break;
            case 0x02:
                GetShiftStatus();
                break;
            case 0x04:
                ClearBuffer();
                break;
            default:
                _cpu.AH = 0;
                break;
        }
    }

    private void WaitForKey()
    {
        // Read scancode from keyboard buffer via port 0x41
        // Port 0x43 indicates if data is available (0x01 = yes)
        byte status = _keyboard.ReadByte(0x43);
        if ((status & 0x01) != 0)
        {
            byte scancode = _keyboard.ReadByte(0x41);
            _cpu.AH = scancode;
            _cpu.AL = scancode; // ASCII approximation
        }
        else
        {
            _cpu.AX = 0;
        }
    }

    private void CheckKey()
    {
        byte status = _keyboard.ReadByte(0x43);
        if ((status & 0x01) != 0)
        {
            _cpu.Flags.ZF = false;
        }
        else
        {
            _cpu.Flags.ZF = true;
        }
    }

    private void GetShiftStatus()
    {
        _cpu.AL = 0;
    }

    private void ClearBuffer()
    {
        _keyboard.Reset();
    }
}
