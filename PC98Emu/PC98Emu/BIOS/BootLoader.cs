using PC98Emu.CPU;
using PC98Emu.Bus;
using PC98Emu.Disk;

namespace PC98Emu.BIOS;

public class BootLoader
{
    private readonly V30 _cpu;
    private readonly SystemBus _bus;
    private readonly DiskManager _diskManager;

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
        const int loadAddr = 0x1FE00;
        int copyLen = Math.Min(sectorBuf.Length, 512);
        for (int i = 0; i < copyLen; i++)
        {
            _bus.WriteMemoryByte(loadAddr + i, sectorBuf[i]);
        }

        // Set CPU state for boot
        _cpu.CS = 0x1FE0;
        _cpu.IP = 0x0000;
        _cpu.DL = (byte)drive;

        return true;
    }
}
