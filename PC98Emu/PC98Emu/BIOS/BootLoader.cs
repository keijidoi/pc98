using PC98Emu.CPU;
using PC98Emu.Bus;
using PC98Emu.Disk;

namespace PC98Emu.BIOS;

public class BootLoader
{
    private readonly V30 _cpu;
    private readonly SystemBus _bus;
    private readonly DiskManager _diskManager;

    // BIOS return stub: IPL uses RETF to return here after boot
    // Must not conflict with IRQ stubs (0xE8060-0xE81D0)
    private const int BOOT_RETURN_STUB = 0xE8200;

    public BootLoader(V30 cpu, SystemBus bus, DiskManager diskManager)
    {
        _cpu = cpu;
        _bus = bus;
        _diskManager = diskManager;
    }

    /// <summary>
    /// Boot from specified drive.
    /// Reads sector C=0, H=0, S=1 and loads 512 bytes to 0x1FE00.
    /// Sets CS=0x1FE0, IP=0x0000, DL=boot drive.
    /// PC-98 does NOT check boot signature.
    /// </summary>
    public bool Boot(int drive)
    {
        var disk = drive < 4 ? _diskManager.GetFloppy(drive) : _diskManager.GetHDD(drive - 0x80);
        if (disk == null)
            return false;

        byte[] sectorBuf = new byte[disk.SectorSize];
        if (!disk.ReadSector(0, 0, 1, sectorBuf))
            return false;

        // Load to physical address 0x1FE00
        // PC-98 loads the full first sector (512 or 1024 bytes depending on format)
        const int loadAddr = 0x1FE00;
        int copyLen = sectorBuf.Length;
        for (int i = 0; i < copyLen; i++)
        {
            _bus.WriteMemoryByte(loadAddr + i, sectorBuf[i]);
        }

        // Write IRET at boot return stub and register a handler that HLTs
        _bus.SetBiosRomArea(false);
        _bus.WriteBiosDirectly(BOOT_RETURN_STUB, 0xCF); // IRET opcode (placeholder)
        _bus.SetBiosRomArea(true);
        _cpu.RegisterBiosHandler(BOOT_RETURN_STUB, () =>
        {
            // IPL returned to BIOS - just halt and wait for interrupts
            _cpu.Halted = true;
        });

        // Push return address for RETF (simulating BIOS CALL FAR to IPL)
        ushort returnSeg = (ushort)(BOOT_RETURN_STUB >> 4);
        ushort returnOff = (ushort)(BOOT_RETURN_STUB & 0x0F);
        _cpu.Push(returnSeg); // CS for RETF
        _cpu.Push(returnOff); // IP for RETF

        // Set CPU state for boot
        _cpu.CS = 0x1FE0;
        _cpu.IP = 0x0000;
        _cpu.DL = (byte)drive;

        // Set boot device DA/UA in BDA
        // PC-98 stores boot DA/UA at 0x0584
        byte daUa;
        if (drive >= 0x80)
            daUa = (byte)(0x80 + (drive - 0x80)); // SASI HDD
        else
            daUa = (byte)(0x20 + drive); // 2HD floppy (default)
        _bus.WriteMemoryByte(0x0584, daUa);
        // Also store at 0x0584+1 for some IPLs
        _bus.WriteMemoryByte(0x0585, daUa);

        return true;
    }
}
