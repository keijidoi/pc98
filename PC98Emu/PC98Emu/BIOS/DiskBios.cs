using PC98Emu.CPU;
using PC98Emu.Bus;
using PC98Emu.Disk;

namespace PC98Emu.BIOS;

public class DiskBios
{
    private readonly V30 _cpu;
    private readonly SystemBus _bus;
    private readonly DiskManager _diskManager;

    public DiskBios(V30 cpu, SystemBus bus, DiskManager diskManager)
    {
        _cpu = cpu;
        _bus = bus;
        _diskManager = diskManager;
    }

    public void Handle()
    {
        byte func = _cpu.AH;
        switch (func)
        {
            case 0x03:
                InitializeDrive();
                break;
            case 0x04:
                GetDriveStatus();
                break;
            case 0x06:
                ReadSectors();
                break;
            case 0x07:
                WriteSectors();
                break;
            default:
                _cpu.AH = 0;
                _cpu.Flags.CF = false;
                break;
        }
    }

    private void InitializeDrive()
    {
        _cpu.AH = 0;
        _cpu.Flags.CF = false;
    }

    private void GetDriveStatus()
    {
        byte drive = _cpu.AL;
        var disk = GetDisk(drive);
        if (disk != null)
        {
            _cpu.AH = 0;
            _cpu.Flags.CF = false;
        }
        else
        {
            _cpu.AH = 0x80; // not ready
            _cpu.Flags.CF = true;
        }
    }

    private void ReadSectors()
    {
        byte drive = _cpu.AL;
        ushort sectorCount = _cpu.BX;
        ushort cylinder = _cpu.CX;
        ushort head = _cpu.DX;
        ushort sector = _cpu.BP;
        int bufferAddr = V30.GetPhysicalAddress(_cpu.ES, _cpu.DI);

        var disk = GetDisk(drive);
        if (disk == null)
        {
            _cpu.AH = 0x80;
            _cpu.Flags.CF = true;
            return;
        }

        byte[] sectorBuf = new byte[disk.SectorSize];
        for (int i = 0; i < sectorCount; i++)
        {
            if (!disk.ReadSector(cylinder, head, sector + i, sectorBuf))
            {
                _cpu.AH = 0x60; // seek error
                _cpu.Flags.CF = true;
                return;
            }

            int destAddr = bufferAddr + i * disk.SectorSize;
            for (int j = 0; j < sectorBuf.Length; j++)
            {
                _bus.WriteMemoryByte(destAddr + j, sectorBuf[j]);
            }
        }

        _cpu.AH = 0;
        _cpu.Flags.CF = false;
    }

    private void WriteSectors()
    {
        byte drive = _cpu.AL;
        ushort sectorCount = _cpu.BX;
        ushort cylinder = _cpu.CX;
        ushort head = _cpu.DX;
        ushort sector = _cpu.BP;
        int bufferAddr = V30.GetPhysicalAddress(_cpu.ES, _cpu.DI);

        var disk = GetDisk(drive);
        if (disk == null)
        {
            _cpu.AH = 0x80;
            _cpu.Flags.CF = true;
            return;
        }

        byte[] sectorBuf = new byte[disk.SectorSize];
        for (int i = 0; i < sectorCount; i++)
        {
            int srcAddr = bufferAddr + i * disk.SectorSize;
            for (int j = 0; j < sectorBuf.Length; j++)
            {
                sectorBuf[j] = _bus.ReadMemoryByte(srcAddr + j);
            }

            if (!disk.WriteSector(cylinder, head, sector + i, sectorBuf))
            {
                _cpu.AH = 0x60;
                _cpu.Flags.CF = true;
                return;
            }
        }

        _cpu.AH = 0;
        _cpu.Flags.CF = false;
    }

    private IDiskImage? GetDisk(byte drive)
    {
        // PC-98: drives 0-3 are floppy, 0x80+ are HDD
        if (drive < 4)
            return _diskManager.GetFloppy(drive);
        if (drive >= 0x80)
            return _diskManager.GetHDD(drive - 0x80);
        return null;
    }
}
