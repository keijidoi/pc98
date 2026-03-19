using PC98Emu.CPU;
using PC98Emu.Bus;
using PC98Emu.Disk;

namespace PC98Emu.BIOS;

public class DiskBios
{
    private readonly V30 _cpu;
    private readonly SystemBus _bus;
    private readonly DiskManager _diskManager;

    /// <summary>
    /// Partition offset in physical sectors (= BPB hidden sectors).
    /// Added to LBA in DA/UA fallback mode after DOS kernel initialization,
    /// converting partition-relative sector numbers to absolute disk LBAs.
    /// </summary>
    private int _partitionOffset;

    public DiskBios(V30 cpu, SystemBus bus, DiskManager diskManager)
    {
        _cpu = cpu;
        _bus = bus;
        _diskManager = diskManager;
    }

    public void SetPartitionOffset(int hiddenSectors)
    {
        _partitionOffset = hiddenSectors;
        Console.Error.WriteLine($"[DISK] Partition offset set to {hiddenSectors} physical sectors");
    }

    public void Handle()
    {
        byte func = _cpu.AH;
        Console.Error.Write($"[DISK] AH={func:X2} AL={_cpu.AL:X2} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} ES:BP={_cpu.ES:X4}:{_cpu.BP:X4}");
        switch (func)
        {
            case 0x03:
            case 0x8E:
                InitializeDrive();
                break;
            case 0x04:
                // Sense - returns drive parameters (BL=sector len code, CX=cylinders, DH=heads, DL=sectors)
                SenseDrive();
                break;
            case 0x84:
                // AH=84: SenseDrive (same as AH=04) - return geometry
                // CX=cylinders, DH=heads, DL=sectors per track
                SenseDrive();
                break;
            case 0x14:
                // Read result status / Equipment check - returns device type in BL
                EquipmentCheck();
                break;
            case 0x05:
            case 0x07:
                WriteSectors();
                break;
            case 0x06:
                ReadSectors();
                break;
            case 0xD6:
                // SASI extended read - used by IO.SYS buildBPB to read the PBR.
                // Parameters: BH=sector count, BX/CX=sector size info, DX=count.
                // Standard CHS/LBA interpretation doesn't apply; reads from partition start.
                ReadSectorsD6();
                break;
            case 0x0E:
                // Seek - just return success
                _cpu.AH = 0;
                _cpu.Flags.CF = false;
                break;
            case 0x00: // Initialize/Recalibrate
            case 0x01: // Seek
            case 0x09: // SCSI extended / Format track
            {
                // These functions require a valid drive - return error if not found
                byte initDaUa = _cpu.AL;
                var (initDisk, _, _) = GetDiskByDaUa(initDaUa);
                if (initDisk != null)
                {
                    _cpu.AH = 0;
                    _cpu.Flags.CF = false;
                }
                else
                {
                    Console.Error.Write($" [NO DRIVE {initDaUa:X2}]");
                    _cpu.AH = 0x60; // not ready
                    _cpu.Flags.CF = true;
                }
                break;
            }
            default:
                // Unknown function - return success
                Console.Error.Write($" [UNKNOWN AH={func:X2}]");
                _cpu.AH = 0;
                _cpu.Flags.CF = false;
                break;
        }
        Console.Error.WriteLine($" → AH={_cpu.AH:X2} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} CF={(_cpu.Flags.CF ? 1 : 0)}");
    }

    private void InitializeDrive()
    {
        _cpu.AH = 0;
        _cpu.Flags.CF = false;
    }

    /// <summary>
    /// AH=04h/84h: Sense - returns drive geometry parameters.
    /// Output: AH=0, CF=0, BL=sector length code, CX=cylinders, DH=heads, DL=sectors per track
    /// Sector length codes: 0=128, 1=256, 2=512, 3=1024
    /// </summary>
    private void SenseDrive()
    {
        byte daUa = _cpu.AL;
        var (disk, isHdd, _) = GetDiskByDaUa(daUa);
        if (disk != null)
        {
            _cpu.AH = 0;
            _cpu.Flags.CF = false;

            if (isHdd)
            {
                // SASI/SCSI HDD: return sector size in BYTES in BX
                // MS-DOS boot code does CMP BX, 0100h to check for 256-byte sectors
                // PBR does DIV BX to convert byte counts to sector counts
                _cpu.BX = (ushort)disk.SectorSize;
            }
            else
            {
                // Floppy: return sector length code in BL, clear BH
                _cpu.BH = 0;
                _cpu.BL = disk.SectorSize switch
                {
                    128 => 0,
                    256 => 1,
                    512 => 2,
                    1024 => 3,
                    _ => 1 // default to 256
                };
            }

            // Return geometry
            _cpu.CL = (byte)(disk.Cylinders & 0xFF);
            _cpu.CH = (byte)((disk.Cylinders >> 8) & 0xFF);
            _cpu.DH = (byte)disk.Heads;
            _cpu.DL = (byte)disk.SectorsPerTrack;
        }
        else
        {
            _cpu.AH = 0x60; // not ready
            _cpu.Flags.CF = true;
        }
    }

    /// <summary>
    /// AH=14h: Equipment check / Read result status - returns device type in BL.
    /// For SASI HDD, returns BL=0x84.
    /// </summary>
    private void EquipmentCheck()
    {
        byte daUa = _cpu.AL;
        var (disk, _, _) = GetDiskByDaUa(daUa);
        if (disk != null)
        {
            _cpu.AH = 0;
            _cpu.Flags.CF = false;

            int device = daUa & 0xF0;
            _cpu.BL = device switch
            {
                0x80 => 0x84, // SASI HDD
                0x90 => 0x94, // SCSI HDD
                0xA0 => 0x84, // IDE/generic HDD probe → report as SASI
                0x20 => 0x20, // 2HD floppy
                0x30 => 0x30, // 2HD 1.44MB
                0x10 => 0x10, // 2DD floppy
                0x00 => 0x00, // 2D floppy
                _ => (byte)device
            };
        }
        else
        {
            _cpu.AH = 0x60;
            _cpu.Flags.CF = true;
        }
    }

    private void GetDriveStatus()
    {
        byte daUa = _cpu.AL;
        var (disk, _, _) = GetDiskByDaUa(daUa);
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

    /// <summary>
    /// PC-98 BIOS INT 1Bh function 06h: Read sectors
    /// AL = DA/UA (device address / unit address)
    /// BH = number of sectors to read
    /// BL = sector length code (0=128, 1=256, 2=512, 3=1024)
    /// CH:CL = cylinder (CH=high, CL=low)
    /// DH = head
    /// DL = sector number
    /// ES:BP = buffer address
    /// </summary>
    private void ReadSectors()
    {
        byte daUa = _cpu.AL;
        int sectorCount = _cpu.BH;
        int sectorLenCode = _cpu.BL;
        int cylinder = (_cpu.CH << 8) | _cpu.CL;
        int head = _cpu.DH;
        int sector = _cpu.DL;
        int bufferAddr = V30.GetPhysicalAddress(_cpu.ES, _cpu.BP);

        if (sectorCount == 0) sectorCount = 1;

        int requestedSectorSize = 128 << sectorLenCode;

        var (disk, isHdd, useLba) = GetDiskByDaUa(daUa);
        if (disk == null)
        {
            _cpu.AH = 0x80;
            _cpu.Flags.CF = true;
            return;
        }

        int physSectorSize = disk.SectorSize;

        // Calculate physical sectors to read per logical sector
        int physPerLogical = 1;
        int copySize = physSectorSize;
        if (requestedSectorSize > physSectorSize)
        {
            physPerLogical = requestedSectorSize / physSectorSize;
            copySize = requestedSectorSize;
        }

        byte[] sectorBuf = new byte[physSectorSize];
        int totalPhysSectors = sectorCount * physPerLogical;

        if (useLba && disk.SectorsPerTrack > 0 && disk.Heads > 0)
        {
            // DA/UA fallback LBA mode: CX = base sector number, DL = sector offset.
            // Before DOS kernel init, PBR uses absolute physical LBAs.
            // After DOS kernel init, code uses partition-relative LBAs;
            // _partitionOffset (= BPB hidden sectors) converts to absolute.
            int lba = cylinder + sector + _partitionOffset;

            int secPerCyl = disk.Heads * disk.SectorsPerTrack;
            Console.Error.Write($" LBA={lba} count={totalPhysSectors}");

            for (int i = 0; i < totalPhysSectors; i++)
            {
                int curLba = lba + i;
                int curCylinder = curLba / secPerCyl;
                int rem = curLba % secPerCyl;
                int curHead = rem / disk.SectorsPerTrack;
                int curSector = (rem % disk.SectorsPerTrack) + 1; // 1-based for HDI

                if (!disk.ReadSector(curCylinder, curHead, curSector, sectorBuf))
                {
                    _cpu.AH = 0x60;
                    _cpu.Flags.CF = true;
                    return;
                }

                int destAddr = bufferAddr + i * physSectorSize;
                for (int j = 0; j < physSectorSize; j++)
                    _bus.WriteMemoryByte(destAddr + j, sectorBuf[j]);
            }
        }
        else
        {
            // CHS mode: standard PC-98 addressing
            for (int i = 0; i < totalPhysSectors; i++)
            {
                int physSectorIdx = sector * physPerLogical + i;
                int curHead = head;
                int curCylinder = cylinder;

                if (disk.SectorsPerTrack > 0)
                {
                    int sectorForDisk = physSectorIdx;
                    if (isHdd)
                    {
                        // SASI/SCSI uses 0-based sector numbers in BIOS calls
                        sectorForDisk = physSectorIdx + 1;
                    }
                    else
                    {
                        // Floppy uses 1-based
                        if (sectorForDisk == 0) sectorForDisk = 1;
                    }

                    // Handle sector/head/cylinder overflow
                    if (sectorForDisk > disk.SectorsPerTrack)
                    {
                        int overflow = sectorForDisk - 1;
                        curHead += overflow / disk.SectorsPerTrack;
                        sectorForDisk = (overflow % disk.SectorsPerTrack) + 1;
                    }
                    if (disk.Heads > 0 && curHead >= disk.Heads)
                    {
                        curCylinder += curHead / disk.Heads;
                        curHead = curHead % disk.Heads;
                    }

                    if (!disk.ReadSector(curCylinder, curHead, sectorForDisk, sectorBuf))
                    {
                        _cpu.AH = 0x60;
                        _cpu.Flags.CF = true;
                        return;
                    }
                }
                else
                {
                    if (!disk.ReadSector(curCylinder, curHead, physSectorIdx + 1, sectorBuf))
                    {
                        _cpu.AH = 0x60;
                        _cpu.Flags.CF = true;
                        return;
                    }
                }

                int destAddr = bufferAddr + i * physSectorSize;
                for (int j = 0; j < physSectorSize; j++)
                    _bus.WriteMemoryByte(destAddr + j, sectorBuf[j]);
            }
        }

        _cpu.AH = 0;
        _cpu.Flags.CF = false;
    }

    private void WriteSectors()
    {
        byte daUa = _cpu.AL;
        int sectorCount = _cpu.BH;
        int cylinder = (_cpu.CH << 8) | _cpu.CL;
        int head = _cpu.DH;
        int sector = _cpu.DL;
        int bufferAddr = V30.GetPhysicalAddress(_cpu.ES, _cpu.BP);

        if (sectorCount == 0) sectorCount = 1;

        var (disk, isHdd, _) = GetDiskByDaUa(daUa);
        if (disk == null)
        {
            _cpu.AH = 0x80;
            _cpu.Flags.CF = true;
            return;
        }

        int actualSectorSize = disk.SectorSize;
        byte[] sectorBuf = new byte[actualSectorSize];

        if (isHdd && disk.SectorsPerTrack > 0 && disk.Heads > 0)
        {
            // SASI HDD: LBA = CX + DL + partition offset
            int lba = cylinder + sector + _partitionOffset;
            int secPerCyl = disk.Heads * disk.SectorsPerTrack;

            for (int i = 0; i < sectorCount; i++)
            {
                int srcAddr = bufferAddr + i * actualSectorSize;
                for (int j = 0; j < actualSectorSize; j++)
                    sectorBuf[j] = _bus.ReadMemoryByte(srcAddr + j);

                int curLba = lba + i;
                int curCyl = curLba / secPerCyl;
                int rem = curLba % secPerCyl;
                int curHead = rem / disk.SectorsPerTrack;
                int curSector = (rem % disk.SectorsPerTrack) + 1;

                if (!disk.WriteSector(curCyl, curHead, curSector, sectorBuf))
                {
                    _cpu.AH = 0x60;
                    _cpu.Flags.CF = true;
                    return;
                }
            }
        }
        else
        {
            for (int i = 0; i < sectorCount; i++)
            {
                int srcAddr = bufferAddr + i * actualSectorSize;
                for (int j = 0; j < actualSectorSize; j++)
                    sectorBuf[j] = _bus.ReadMemoryByte(srcAddr + j);

                if (!disk.WriteSector(cylinder, head, sector + i, sectorBuf))
                {
                    _cpu.AH = 0x60;
                    _cpu.Flags.CF = true;
                    return;
                }
            }
        }

        _cpu.AH = 0;
        _cpu.Flags.CF = false;
    }

    /// <summary>
    /// AH=D6h: SASI extended read from partition start.
    /// Used by IO.SYS buildBPB handler to read the PBR/BPB.
    /// Reads BH physical sectors from the partition start (sector _partitionOffset).
    /// </summary>
    private void ReadSectorsD6()
    {
        byte daUa = _cpu.AL;
        int sectorCount = _cpu.BH;
        if (sectorCount == 0) sectorCount = 2; // default: 2 sectors = 512 bytes
        int bufferAddr = V30.GetPhysicalAddress(_cpu.ES, _cpu.BP);

        var (disk, _, _) = GetDiskByDaUa(daUa);
        if (disk == null)
        {
            _cpu.AH = 0x80;
            _cpu.Flags.CF = true;
            return;
        }

        int physSectorSize = disk.SectorSize;
        byte[] sectorBuf = new byte[physSectorSize];
        int secPerCyl = disk.Heads * disk.SectorsPerTrack;

        // Read from partition start (PBR location)
        int startLba = _partitionOffset;
        Console.Error.Write($" D6-READ LBA={startLba} count={sectorCount}");

        for (int i = 0; i < sectorCount; i++)
        {
            int curLba = startLba + i;
            int curCylinder = curLba / secPerCyl;
            int rem = curLba % secPerCyl;
            int curHead = rem / disk.SectorsPerTrack;
            int curSector = (rem % disk.SectorsPerTrack) + 1; // 1-based

            if (!disk.ReadSector(curCylinder, curHead, curSector, sectorBuf))
            {
                _cpu.AH = 0x60;
                _cpu.Flags.CF = true;
                return;
            }

            int destAddr = bufferAddr + i * physSectorSize;
            for (int j = 0; j < physSectorSize; j++)
                _bus.WriteMemoryByte(destAddr + j, sectorBuf[j]);
        }

        _cpu.AH = 0;
        _cpu.Flags.CF = false;
    }

    /// <summary>
    /// Map DA/UA to disk image.
    /// DA/UA encoding:
    ///   0x00-0x03: 2D floppy (320KB)
    ///   0x10-0x13: 2DD floppy (640KB)
    ///   0x20-0x23: 2HD floppy (1.25MB)
    ///   0x30-0x33: 2HD floppy (1.44MB)
    ///   0x80-0x83: SASI HDD
    ///   0x90-0x93: SCSI HDD
    /// </summary>
    /// <summary>
    /// Returns (disk, isHdd, useLba).
    /// useLba is true when the DA/UA fallback fires (PBR does AND 7Fh on boot DA/UA).
    /// In LBA mode, CX = physical sector number, not cylinder.
    /// </summary>
    private (IDiskImage? disk, bool isHdd, bool useLba) GetDiskByDaUa(byte daUa)
    {
        int unit = daUa & 0x03;
        int device = daUa & 0xF0;
        bool isHdd = device >= 0x80;

        IDiskImage? disk = device switch
        {
            0x00 or 0x10 or 0x20 or 0x30 => _diskManager.GetFloppy(unit),
            0x80 or 0x90 or 0xA0 => _diskManager.GetHDD(unit),
            _ => null
        };

        // Fallback: NEC MS-DOS partition boot record does "AND AL, 7Fh" on the boot DA/UA,
        // which strips the HDD device bit (0x80 → 0x00). When a floppy DA/UA is requested
        // but no floppy exists, and it matches the boot HDD's DA/UA masked by 0x7F,
        // redirect to the boot HDD. The PBR uses LBA addressing (CX = physical sector number).
        bool useLba = false;
        if (disk == null && !isHdd)
        {
            byte bootDaUa = _bus.ReadMemoryByte(0x0584);
            if ((bootDaUa & 0x80) != 0 && (bootDaUa & 0x7F) == daUa)
            {
                int bootUnit = bootDaUa & 0x03;
                disk = _diskManager.GetHDD(bootUnit);
                if (disk != null)
                {
                    isHdd = true;
                    useLba = true;
                    Console.Error.Write($"[DISK] DA/UA fallback: {daUa:X2} → HDD unit {bootUnit} LBA mode");
                }
            }
        }

        return (disk, isHdd, useLba);
    }
}
