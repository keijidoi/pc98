using PC98Emu.CPU;
using PC98Emu.Bus;
using PC98Emu.Devices;
using PC98Emu.Disk;

namespace PC98Emu.BIOS;

public class CompatibleBios
{
    private readonly V30 _cpu;
    private readonly SystemBus _bus;

    private DiskBios? _diskBios;
    private SerialBios? _serialBios;
    private TimerBios? _timerBios;
    private KeyboardBios? _keyboardBios;
    private CrtBios? _crtBios;
    private GraphicsBios? _graphicsBios;

    // BIOS handler addresses in ROM area
    private const int INT18_ADDR = 0xE8000; // Disk
    private const int INT19_ADDR = 0xE8010; // Serial
    private const int INT1A_ADDR = 0xE8020; // Timer
    private const int INT1B_ADDR = 0xE8030; // Keyboard
    private const int INT1C_ADDR = 0xE8040; // CRT
    private const int INT1D_ADDR = 0xE8050; // Graphics

    // BDA addresses
    private const int BDA_MEMORY_SIZE = 0x0458;
    private const int BDA_BOOT_DEVICE = 0x045B;
    private const int BDA_DISPLAY_MODE = 0x0480;
    private const int BDA_GDC_CLOCK = 0x0486;

    public CompatibleBios(V30 cpu, SystemBus bus)
    {
        _cpu = cpu;
        _bus = bus;
    }

    public void SetDiskManager(DiskManager diskManager)
    {
        _diskBios = new DiskBios(_cpu, _bus, diskManager);
    }

    public void SetKeyboard(Keyboard keyboard)
    {
        _keyboardBios = new KeyboardBios(_cpu, _bus, keyboard);
    }

    public void Initialize()
    {
        // Create sub-BIOS handlers that don't need external dependencies
        _serialBios ??= new SerialBios(_cpu, _bus);
        _timerBios ??= new TimerBios(_cpu, _bus);
        _crtBios ??= new CrtBios(_cpu, _bus);
        _graphicsBios ??= new GraphicsBios(_cpu, _bus);

        // Unprotect BIOS ROM for writing
        _bus.SetBiosRomArea(false);

        // Write IRET opcode (0xCF) at each handler address as a safety fallback
        WriteBiosEntry(INT18_ADDR);
        WriteBiosEntry(INT19_ADDR);
        WriteBiosEntry(INT1A_ADDR);
        WriteBiosEntry(INT1B_ADDR);
        WriteBiosEntry(INT1C_ADDR);
        WriteBiosEntry(INT1D_ADDR);

        // Setup IVT entries
        WriteIVTEntry(0x18, INT18_ADDR);
        WriteIVTEntry(0x19, INT19_ADDR);
        WriteIVTEntry(0x1A, INT1A_ADDR);
        WriteIVTEntry(0x1B, INT1B_ADDR);
        WriteIVTEntry(0x1C, INT1C_ADDR);
        WriteIVTEntry(0x1D, INT1D_ADDR);

        // Register BIOS handlers with CPU
        _cpu.RegisterBiosHandler(INT18_ADDR, HandleInt18);
        _cpu.RegisterBiosHandler(INT19_ADDR, HandleInt19);
        _cpu.RegisterBiosHandler(INT1A_ADDR, HandleInt1A);
        _cpu.RegisterBiosHandler(INT1B_ADDR, HandleInt1B);
        _cpu.RegisterBiosHandler(INT1C_ADDR, HandleInt1C);
        _cpu.RegisterBiosHandler(INT1D_ADDR, HandleInt1D);

        // Initialize BDA
        _bus.WriteMemoryWord(BDA_MEMORY_SIZE, 640);
        _bus.WriteMemoryByte(BDA_BOOT_DEVICE, 0);
        _bus.WriteMemoryByte(BDA_DISPLAY_MODE, 0);
        _bus.WriteMemoryByte(BDA_GDC_CLOCK, 0);

        // Protect BIOS ROM
        _bus.SetBiosRomArea(true);
    }

    private void WriteBiosEntry(int addr)
    {
        _bus.WriteBiosDirectly(addr, 0xCF); // IRET opcode
    }

    private void WriteIVTEntry(int intNum, int handlerPhysAddr)
    {
        // Convert physical address to segment:offset
        ushort segment = (ushort)(handlerPhysAddr >> 4);
        ushort offset = (ushort)(handlerPhysAddr & 0x0F);

        int ivtAddr = intNum * 4;
        _bus.WriteMemoryWord(ivtAddr, offset);
        _bus.WriteMemoryWord(ivtAddr + 2, segment);
    }

    private void DoIret()
    {
        _cpu.IP = _cpu.Pop();
        _cpu.CS = _cpu.Pop();
        ushort flags = _cpu.Pop();
        _cpu.Flags.Value = flags;
    }

    private void HandleInt18()
    {
        _diskBios?.Handle();
        DoIret();
    }

    private void HandleInt19()
    {
        _serialBios?.Handle();
        DoIret();
    }

    private void HandleInt1A()
    {
        _timerBios?.Handle();
        DoIret();
    }

    private void HandleInt1B()
    {
        _keyboardBios?.Handle();
        DoIret();
    }

    private void HandleInt1C()
    {
        _crtBios?.Handle();
        DoIret();
    }

    private void HandleInt1D()
    {
        _graphicsBios?.Handle();
        DoIret();
    }
}
