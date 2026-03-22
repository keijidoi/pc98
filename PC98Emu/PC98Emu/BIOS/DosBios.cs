using PC98Emu.CPU;
using PC98Emu.Bus;
using PC98Emu.Disk;

namespace PC98Emu.BIOS;

/// <summary>
/// INT 21h DOS function handler.
/// Implements essential DOS API functions needed for program loading and execution.
/// Registered as a BIOS handler at the kernel's INT 21h stub address.
/// </summary>
public class DosBios
{
    private readonly V30 _cpu;
    private readonly SystemBus _bus;

    // DOS internal state
    public bool SkipIret { get; set; } // Set by EXEC to prevent IRET after control transfer
    public bool TraceAfterExec { get; set; } // Enable CPU trace after EXEC
    private ushort _currentPSP;
    private ushort _dtaSeg;
    private ushort _dtaOff = 0x0080; // Default DTA at PSP:0080
    private byte _currentDrive; // 0=A, 1=B, 2=C...
    private byte _verifyFlag;
    private ushort _lastError; // Last DOS error code for AH=59

    // Simple file handle table (handles 0-39, extended for overlay managers)
    private const int MAX_HANDLES = 40;
    private readonly bool[] _handleOpen = new bool[MAX_HANDLES];

    // File position and size tracking per handle
    private readonly uint[] _filePosition = new uint[MAX_HANDLES];
    private readonly uint[] _fileSize = new uint[MAX_HANDLES];

    // Memory management - block allocator with free list
    private const ushort MEM_TOP_SEG = 0xA000; // PC-98 conventional memory top (640KB = 0xA0000)
    private readonly List<(ushort seg, ushort size)> _allocBlocks = new();
    private ushort _memBaseSeg = 0x3000; // Lowest allocation segment

    // DOS version to report (MS-DOS 6.20 matching the boot disk)
    private const byte DOS_MAJOR = 6;
    private const byte DOS_MINOR = 20;

    // Per-drive FAT16 readers (indexed by drive number: 0=A, 1=B, 2=C...)
    private readonly Fat16Reader?[] _fatReaders = new Fat16Reader?[26];

    // Fake SFT (System File Table) for overlay manager support
    // SFT is stored in memory at a fixed address so overlay managers can directly access it
    // Each SFT entry is 59 bytes (0x3B)
    private const int SFT_BASE = 0xE7000; // In BIOS ROM area (below E8000)
    private const int SFT_ENTRY_SIZE = 0x3B; // 59 bytes per entry
    private const int SFT_HEADER_SIZE = 6; // 4-byte next pointer + 2-byte count

    // Per-drive current directory (indexed by drive number, without leading/trailing backslash)
    private readonly string[] _currentDirs = new string[26];

    // Debug counter for AH=52 calls
    private int _ah52CallCount = 0;

    public DosBios(V30 cpu, SystemBus bus)
    {
        _cpu = cpu;
        _bus = bus;

        // Initialize fake SFT in memory
        InitializeSft();

        // Pre-open standard handles
        _handleOpen[0] = true; // STDIN
        _handleOpen[1] = true; // STDOUT
        _handleOpen[2] = true; // STDERR
        _handleOpen[3] = true; // STDAUX
        _handleOpen[4] = true; // STDPRN
    }

    public void SetFat16Reader(Fat16Reader reader)
    {
        // Legacy: set for drive A (0)
        _fatReaders[0] = reader;
    }

    public void SetCurrentDrive(byte drive)
    {
        _currentDrive = drive;
    }

    public void SetCurrentDirectory(byte drive, string dir)
    {
        if (drive < 26)
            _currentDirs[drive] = dir;
    }

    public void SetFat16ReaderForDrive(int drive, Fat16Reader reader)
    {
        if (drive >= 0 && drive < _fatReaders.Length)
            _fatReaders[drive] = reader;
    }

    /// <summary>
    /// Get the Fat16Reader for the current drive, or null if none.
    /// </summary>
    private Fat16Reader? GetCurrentFat16()
    {
        return _fatReaders[_currentDrive];
    }

    /// <summary>
    /// Reset all internal state for reboot.
    /// </summary>
    public void Reset()
    {
        _currentPSP = 0;
        _dtaSeg = 0;
        _dtaOff = 0x0080;
        _currentDrive = 0;
        _verifyFlag = 0;
        _lastError = 0;
        Array.Clear(_fatReaders);
        _memBaseSeg = 0x3000;
        _allocBlocks.Clear();
        SkipIret = false;
        TraceAfterExec = false;
        WaitingForInput = false;
        Array.Clear(_handleOpen);
        Array.Clear(_filePosition);
        Array.Clear(_fileSize);
        _handleOpen[0] = true; // STDIN
        _handleOpen[1] = true; // STDOUT
        _handleOpen[2] = true; // STDERR
        _handleOpen[3] = true; // STDAUX
        _handleOpen[4] = true; // STDPRN
    }

    public void SetPSP(ushort segment)
    {
        _currentPSP = segment;
        _dtaSeg = segment;
        _dtaOff = 0x0080; // Default DTA at PSP:0080

        // Set initial free memory above the PSP + some space for the program
        ushort memBase = (ushort)(segment + 0x1000); // Give 64KB for the program
        if (memBase > _memBaseSeg)
            _memBaseSeg = memBase;
        Console.Error.WriteLine($"[DOS] SetPSP segment={segment:X4}, memBase={_memBaseSeg:X4}");
    }

    /// <summary>
    /// Set current PSP segment (for direct game load, bypassing EXEC).
    /// Also resets DTA and rebuilds MCB chain.
    /// </summary>
    public void SetCurrentPSP(ushort segment)
    {
        _currentPSP = segment;
        _dtaSeg = segment;
        _dtaOff = 0x0080;
        Console.Error.WriteLine($"[DOS] SetCurrentPSP → {segment:X4}");
        RebuildMcbChain();
    }

    /// <summary>
    /// Handle INT 21h call. Called as a BIOS handler when CPU reaches the stub address.
    /// Must do its own IRET (pop IP, CS, FLAGS from stack).
    /// </summary>
    public void HandleInt21()
    {
        byte func = _cpu.AH;
        switch (func)
        {
            case 0x00: // Terminate Program
                Terminate();
                break;

            case 0x02: // Display Character
                // DL = character to display
                DisplayChar(_cpu.DL);
                break;

            case 0x06: // Direct Console I/O
                DirectConsoleIO();
                break;

            case 0x09: // Display String
                DisplayString();
                break;

            case 0x0A: // Buffered Input
                BufferedInput();
                break;

            case 0x0B: // Check Input Status
                _cpu.AL = 0x00; // No character available
                break;

            case 0x11: // Find First File (FCB)
                FcbFindFirst();
                break;

            case 0x12: // Find Next File (FCB)
                FcbFindNext();
                break;

            case 0x0E: // Select Disk
                SelectDisk();
                break;

            case 0x19: // Get Current Disk
                _cpu.AL = _currentDrive;
                break;

            case 0x1A: // Set DTA
                _dtaSeg = _cpu.DS;
                _dtaOff = _cpu.DX;
                break;

            case 0x25: // Set Interrupt Vector
                SetInterruptVector();
                break;

            case 0x29: // Parse Filename into FCB
                ParseFilename();
                break;

            case 0x2A: // Get Date
                GetDate();
                break;

            case 0x2C: // Get Time
                GetTime();
                break;

            case 0x2E: // Set Verify Flag
                _verifyFlag = _cpu.AL;
                break;

            case 0x2F: // Get DTA
                _cpu.ES = _dtaSeg;
                _cpu.BX = _dtaOff;
                break;

            case 0x30: // Get DOS Version
                _cpu.AL = DOS_MAJOR;
                _cpu.AH = DOS_MINOR;
                _cpu.BX = 0x0000; // OEM serial
                _cpu.CX = 0x0000;
                break;

            case 0x33: // Get/Set Break Flag
                if (_cpu.AL == 0x00) _cpu.DL = 0; // Get: break off
                // AL=01: Set (ignore)
                // AL=05: Get boot drive
                if (_cpu.AL == 0x05) _cpu.DL = 0x80; // HDD
                // AL=06: Get true DOS version
                if (_cpu.AL == 0x06)
                {
                    _cpu.BL = DOS_MAJOR;
                    _cpu.BH = DOS_MINOR;
                    _cpu.DL = 0; // Revision
                    _cpu.DH = 0x10; // DOS in HMA
                }
                break;

            case 0x35: // Get Interrupt Vector
                GetInterruptVector();
                break;

            case 0x36: // Get Disk Free Space
                GetDiskFreeSpace();
                break;

            case 0x39: // Create Directory (mkdir)
                // Stub: succeed (read-only filesystem)
                _cpu.Flags.CF = false;
                Console.Error.WriteLine($"[DOS] mkdir (stub, no-op)");
                break;

            case 0x3A: // Remove Directory (rmdir)
                _cpu.Flags.CF = false;
                break;

            case 0x3B: // Change Directory
                ChangeDirectory();
                break;

            case 0x3C: // Create File
                CreateFile();
                break;

            case 0x3D: // Open File
                OpenFile();
                break;

            case 0x3E: // Close File
                CloseFile();
                break;

            case 0x3F: // Read File
                ReadFile();
                break;

            case 0x40: // Write File
                WriteFile();
                break;

            case 0x41: // Delete File
                // Read-only filesystem: return access denied
                _cpu.AX = 0x05; // Access denied
                _cpu.Flags.CF = true;
                break;

            case 0x42: // Seek (LSEEK)
                Seek();
                break;

            case 0x43: // Get/Set File Attributes
                if (_cpu.AL == 0x00) // Get
                {
                    _cpu.CX = 0x0020; // Archive attribute
                    _cpu.Flags.CF = false;
                }
                else // Set
                {
                    _cpu.Flags.CF = false; // Stub: succeed
                }
                break;

            case 0x44: // IOCTL
                HandleIoctl();
                break;

            case 0x47: // Get Current Directory
                GetCurrentDirectory();
                break;

            case 0x48: // Allocate Memory
                AllocateMemory();
                break;

            case 0x49: // Free Memory
                FreeMemory();
                break;

            case 0x4A: // Resize Memory Block
                ResizeMemory();
                break;

            case 0x4E: // Find First File
                FindFirstFile();
                break;

            case 0x4F: // Find Next File
                FindNextFile();
                break;

            case 0x4B: // EXEC (Load and Execute)
                Exec();
                break;

            case 0x4C: // Exit
                Terminate();
                break;

            case 0x4D: // Get Return Code
                _cpu.AX = 0x0000; // Normal termination, return code 0
                break;

            case 0x50: // Set Current PSP
                Console.Error.WriteLine($"[DOS] SetPSP(INT21) BX={_cpu.BX:X4}");
                _currentPSP = _cpu.BX;
                break;

            case 0x51: // Get Current PSP
            case 0x62: // Get PSP Address (same as 51h)
                _cpu.BX = _currentPSP;
                break;

            case 0x52: // Get List of Lists (SYSVARS)
            {
                // Return pointer to a minimal SYSVARS structure in a safe area
                // SYSVARS is at ES:BX where BX must be >= 2 (game reads ES:[BX-2] for MCB pointer)
                // Use segment 0xE6F0, offset 0x26 (like real DOS), so SYSVARS at phys 0xE6F26
                const ushort SYSVARS_SEG = 0xE6F0;
                const ushort SYSVARS_OFF = 0x0026;
                const int SYSVARS_ADDR = (SYSVARS_SEG << 4) + SYSVARS_OFF; // 0xE6F26
                _cpu.ES = SYSVARS_SEG;
                _cpu.BX = SYSVARS_OFF;

                var sysMem = _bus.GetMemoryDirect();

                // Dump calling code on first few calls for debugging
                if (_ah52CallCount < 3)
                {
                    // Read return address from stack (IP is after INT 21h)
                    int spAddr = (_cpu.SS << 4) + _cpu.SP;
                    ushort retIP = (ushort)(sysMem[spAddr] | (sysMem[spAddr + 1] << 8));
                    ushort retCS = (ushort)(sysMem[spAddr + 2] | (sysMem[spAddr + 3] << 8));
                    int codeAddr = (retCS << 4) + retIP;
                    string hex = "";
                    for (int hh = 0; hh < 40 && codeAddr + hh < sysMem.Length; hh++)
                        hex += $"{sysMem[codeAddr + hh]:X2} ";
                    Console.Error.WriteLine($"[AH52-DBG] Call#{_ah52CallCount} ret={retCS:X4}:{retIP:X4} code: {hex}");
                    // Also dump the stack
                    string stackHex = "";
                    for (int ss = 0; ss < 16 && spAddr + ss < sysMem.Length; ss++)
                        stackHex += $"{sysMem[spAddr + ss]:X2} ";
                    Console.Error.WriteLine($"[AH52-DBG] SS:SP={_cpu.SS:X4}:{_cpu.SP:X4} stack: {stackHex}");
                    // Dump PSP JFT
                    int pspA = _currentPSP << 4;
                    string jftHex = "";
                    for (int jj = 0; jj < 20; jj++)
                        jftHex += $"{sysMem[pspA + 0x18 + jj]:X2} ";
                    Console.Error.WriteLine($"[AH52-DBG] PSP={_currentPSP:X4} JFT: {jftHex}");
                    Console.Error.WriteLine($"[AH52-DBG] PSP maxH={sysMem[pspA+0x32]|sysMem[pspA+0x33]<<8} JFTptr={sysMem[pspA+0x36]|sysMem[pspA+0x37]<<8:X4}:{sysMem[pspA+0x34]|sysMem[pspA+0x35]<<8:X4}");
                }
                _ah52CallCount++;

                // Initialize SYSVARS structure (only on first call or if needed)
                // Offset -2: first MCB segment
                sysMem[SYSVARS_ADDR - 2] = (byte)(_memBaseSeg & 0xFF);
                sysMem[SYSVARS_ADDR - 1] = (byte)(_memBaseSeg >> 8);
                // Offset +0: first DPB pointer (FFFF:FFFF = no DPBs)
                sysMem[SYSVARS_ADDR + 0] = 0xFF;
                sysMem[SYSVARS_ADDR + 1] = 0xFF;
                sysMem[SYSVARS_ADDR + 2] = 0xFF;
                sysMem[SYSVARS_ADDR + 3] = 0xFF;
                // Offset +4: SFT pointer
                ushort sftOff = (ushort)(SFT_BASE & 0x0F);
                ushort sftSeg = (ushort)(SFT_BASE >> 4);
                sysMem[SYSVARS_ADDR + 4] = (byte)(sftOff & 0xFF);
                sysMem[SYSVARS_ADDR + 5] = (byte)(sftOff >> 8);
                sysMem[SYSVARS_ADDR + 6] = (byte)(sftSeg & 0xFF);
                sysMem[SYSVARS_ADDR + 7] = (byte)(sftSeg >> 8);
                // Offset +8: CLOCK$ device pointer (FFFF:FFFF)
                sysMem[SYSVARS_ADDR + 8] = 0xFF;
                sysMem[SYSVARS_ADDR + 9] = 0xFF;
                sysMem[SYSVARS_ADDR + 10] = 0xFF;
                sysMem[SYSVARS_ADDR + 11] = 0xFF;
                // Offset +0C: CON device pointer (FFFF:FFFF)
                sysMem[SYSVARS_ADDR + 0x0C] = 0xFF;
                sysMem[SYSVARS_ADDR + 0x0D] = 0xFF;
                sysMem[SYSVARS_ADDR + 0x0E] = 0xFF;
                sysMem[SYSVARS_ADDR + 0x0F] = 0xFF;
                // Offset +10: max sector size
                sysMem[SYSVARS_ADDR + 0x10] = 0x00;
                sysMem[SYSVARS_ADDR + 0x11] = 0x02; // 512 bytes
                // Offset +12: disk buffer pointer (FFFF:FFFF)
                sysMem[SYSVARS_ADDR + 0x12] = 0xFF;
                sysMem[SYSVARS_ADDR + 0x13] = 0xFF;
                sysMem[SYSVARS_ADDR + 0x14] = 0xFF;
                sysMem[SYSVARS_ADDR + 0x15] = 0xFF;
                // Offset +16: CDS array pointer (FFFF:FFFF)
                sysMem[SYSVARS_ADDR + 0x16] = 0xFF;
                sysMem[SYSVARS_ADDR + 0x17] = 0xFF;
                sysMem[SYSVARS_ADDR + 0x18] = 0xFF;
                sysMem[SYSVARS_ADDR + 0x19] = 0xFF;
                // Offset +1A: FCB SFT pointer (same as SFT)
                sysMem[SYSVARS_ADDR + 0x1A] = (byte)(sftOff & 0xFF);
                sysMem[SYSVARS_ADDR + 0x1B] = (byte)(sftOff >> 8);
                sysMem[SYSVARS_ADDR + 0x1C] = (byte)(sftSeg & 0xFF);
                sysMem[SYSVARS_ADDR + 0x1D] = (byte)(sftSeg >> 8);
                // Offset +1E: FCB table entries count
                sysMem[SYSVARS_ADDR + 0x1E] = 0x04;
                sysMem[SYSVARS_ADDR + 0x1F] = 0x00;
                // Offset +20: number of block devices
                sysMem[SYSVARS_ADDR + 0x20] = 0x02; // A: and B:
                // Offset +21: LASTDRIVE value
                sysMem[SYSVARS_ADDR + 0x21] = 0x1A; // 26 (Z:)
                // Offset +22: NUL device header (18 bytes)
                // next pointer = FFFF:FFFF
                sysMem[SYSVARS_ADDR + 0x22] = 0xFF;
                sysMem[SYSVARS_ADDR + 0x23] = 0xFF;
                sysMem[SYSVARS_ADDR + 0x24] = 0xFF;
                sysMem[SYSVARS_ADDR + 0x25] = 0xFF;
                // attributes = 0x8004 (character device, NUL)
                sysMem[SYSVARS_ADDR + 0x26] = 0x04;
                sysMem[SYSVARS_ADDR + 0x27] = 0x80;
                // strategy entry / interrupt entry (dummy)
                sysMem[SYSVARS_ADDR + 0x28] = 0x00;
                sysMem[SYSVARS_ADDR + 0x29] = 0x00;
                sysMem[SYSVARS_ADDR + 0x2A] = 0x00;
                sysMem[SYSVARS_ADDR + 0x2B] = 0x00;
                // device name "NUL     "
                sysMem[SYSVARS_ADDR + 0x2C] = (byte)'N';
                sysMem[SYSVARS_ADDR + 0x2D] = (byte)'U';
                sysMem[SYSVARS_ADDR + 0x2E] = (byte)'L';
                sysMem[SYSVARS_ADDR + 0x2F] = (byte)' ';
                sysMem[SYSVARS_ADDR + 0x30] = (byte)' ';
                sysMem[SYSVARS_ADDR + 0x31] = (byte)' ';
                sysMem[SYSVARS_ADDR + 0x32] = (byte)' ';
                sysMem[SYSVARS_ADDR + 0x33] = (byte)' ';

                // Ensure SFT header's "next" pointer is 0xFFFF:FFFF (end of chain)
                sysMem[SFT_BASE] = 0xFF;
                sysMem[SFT_BASE + 1] = 0xFF;
                sysMem[SFT_BASE + 2] = 0xFF;
                sysMem[SFT_BASE + 3] = 0xFF;
                sysMem[SFT_BASE + 4] = (byte)(MAX_HANDLES & 0xFF);
                sysMem[SFT_BASE + 5] = (byte)(MAX_HANDLES >> 8);
                break;
            }

            case 0x56: // Rename File
                _cpu.Flags.CF = false; // Stub: succeed
                break;

            case 0x38: // Get/Set Country Info
                if (_cpu.AL == 0x00 || _cpu.AL == 0x01)
                {
                    // Get country info → DS:DX buffer (34 bytes for DOS 3.3+)
                    int ciAddr = (_cpu.DS << 4) + _cpu.DX;
                    var ciMem = _bus.GetMemoryDirect();
                    for (int i = 0; i < 34; i++) ciMem[(ciAddr + i) & 0xFFFFF] = 0;
                    // Offset 0-1: Date format (2 = YY/MM/DD Japan)
                    ciMem[ciAddr] = 0x02; ciMem[ciAddr + 1] = 0x00;
                    // Offset 2-6: Currency symbol (¥ NUL-padded)
                    ciMem[ciAddr + 2] = 0x5C; // backslash (yen in SJIS)
                    ciMem[ciAddr + 3] = 0x00;
                    // Offset 7-8: Thousands separator
                    ciMem[ciAddr + 7] = 0x2C; ciMem[ciAddr + 8] = 0x00;
                    // Offset 9-10: Decimal separator
                    ciMem[ciAddr + 9] = 0x2E; ciMem[ciAddr + 10] = 0x00;
                    // Offset 11-12: Date separator
                    ciMem[ciAddr + 11] = 0x2F; ciMem[ciAddr + 12] = 0x00; // '/'
                    // Offset 13-14: Time separator
                    ciMem[ciAddr + 13] = 0x3A; ciMem[ciAddr + 14] = 0x00; // ':'
                    // Offset 15: Currency format (0=symbol before, no space)
                    ciMem[ciAddr + 15] = 0x00;
                    // Offset 16: Digits after decimal in currency
                    ciMem[ciAddr + 16] = 0x00;
                    // Offset 17: Time format (0=12hr, 1=24hr)
                    ciMem[ciAddr + 17] = 0x01; // 24hr
                    // Offset 18-21: Case map call address (far pointer)
                    ciMem[ciAddr + 18] = 0x00; ciMem[ciAddr + 19] = 0x00;
                    ciMem[ciAddr + 20] = 0x00; ciMem[ciAddr + 21] = 0x00;
                    // Offset 22-23: Data list separator
                    ciMem[ciAddr + 22] = 0x2C; ciMem[ciAddr + 23] = 0x00; // ','
                    _cpu.BX = 0x0051; // Country code 81 = Japan
                    _cpu.Flags.CF = false;
                }
                else
                {
                    _cpu.Flags.CF = false;
                }
                break;

            case 0x5D: // Network/DOS internal
                // Subfunc in AL: 08=Get redirect, 09=Set redirect
                _cpu.Flags.CF = false;
                break;

            case 0x57: // Get/Set File Date and Time
                if (_cpu.AL == 0x00) // Get
                {
                    _cpu.CX = 0x0000; // Time
                    _cpu.DX = 0x0021; // Date: 1980-01-01
                    _cpu.Flags.CF = false;
                }
                else // Set
                {
                    _cpu.Flags.CF = false;
                }
                break;

            case 0x63: // Get Lead Byte Table (DBCS support)
                // Return pointer to DBCS lead byte table in DS:SI
                // For Shift-JIS: 81-9F and E0-FC are lead bytes
                {
                    // Write a small DBCS table in a safe location
                    var lbMem = _bus.GetMemoryDirect();
                    int tblAddr = 0x600 + 0x01F0; // Use spare BDA area
                    lbMem[tblAddr + 0] = 0x81; lbMem[tblAddr + 1] = 0x9F; // Range 1
                    lbMem[tblAddr + 2] = 0xE0; lbMem[tblAddr + 3] = 0xFC; // Range 2
                    lbMem[tblAddr + 4] = 0x00; lbMem[tblAddr + 5] = 0x00; // End marker
                    _cpu.DS = 0x0060;
                    _cpu.SI = 0x01F0;
                    _cpu.Flags.CF = false;
                }
                break;

            case 0x59: // Get Extended Error Info
                _cpu.AX = _lastError;
                if (_lastError == 0x12) // No more files
                {
                    _cpu.BH = 0x01; // Class: out of resource
                    _cpu.BL = 0x01; // Action: retry
                    _cpu.CH = 0x01; // Locus: unknown
                }
                else if (_lastError == 0x02) // File not found
                {
                    _cpu.BH = 0x08; // Class: not found
                    _cpu.BL = 0x01; // Action: retry
                    _cpu.CH = 0x02; // Locus: disk
                }
                else
                {
                    _cpu.BX = 0x0000;
                    _cpu.CX = 0x0000;
                }
                _cpu.Flags.CF = false;
                break;

            case 0x69: // Get/Set Disk Serial Number
                if (_cpu.AL == 0x00) // Get
                {
                    // BL = drive (0=default), DS:DX = buffer
                    int snAddr = (_cpu.DS << 4) + _cpu.DX;
                    var snMem = _bus.GetMemoryDirect();
                    // Info level (word) = 0
                    snMem[(snAddr) & 0xFFFFF] = 0x00;
                    snMem[(snAddr + 1) & 0xFFFFF] = 0x00;
                    // Serial number (4 bytes)
                    snMem[(snAddr + 2) & 0xFFFFF] = 0x00;
                    snMem[(snAddr + 3) & 0xFFFFF] = 0x00;
                    snMem[(snAddr + 4) & 0xFFFFF] = 0x00;
                    snMem[(snAddr + 5) & 0xFFFFF] = 0x00;
                    // Volume label (11 bytes)
                    byte[] label = System.Text.Encoding.ASCII.GetBytes("NO NAME    ");
                    for (int i = 0; i < 11; i++)
                        snMem[(snAddr + 6 + i) & 0xFFFFF] = label[i];
                    // File system type (8 bytes)
                    byte[] fsType = System.Text.Encoding.ASCII.GetBytes("FAT16   ");
                    for (int i = 0; i < 8; i++)
                        snMem[(snAddr + 17 + i) & 0xFFFFF] = fsType[i];
                    _cpu.Flags.CF = false;
                }
                else
                {
                    _cpu.Flags.CF = false; // Set: stub
                }
                break;

            case 0x65: // Get Extended Country Info
                // AL=04: Get filename uppercase table
                // Return minimal info
                _cpu.Flags.CF = true; // Not supported
                _cpu.AX = 0x01; // Invalid function
                break;

            case 0x67: // Set Handle Count
            {
                ushort newCount = _cpu.BX;
                var jftMem = _bus.GetMemoryDirect();
                int pspAddr = _currentPSP << 4;
                // Read current JFT pointer and size
                ushort oldMax = (ushort)(jftMem[pspAddr + 0x32] | (jftMem[pspAddr + 0x33] << 8));
                ushort jftOff = (ushort)(jftMem[pspAddr + 0x34] | (jftMem[pspAddr + 0x35] << 8));
                ushort jftSeg = (ushort)(jftMem[pspAddr + 0x36] | (jftMem[pspAddr + 0x37] << 8));
                int oldJftAddr = (jftSeg << 4) + jftOff;
                if (newCount <= oldMax)
                {
                    // Just update the count
                    jftMem[pspAddr + 0x32] = (byte)(newCount & 0xFF);
                    jftMem[pspAddr + 0x33] = (byte)(newCount >> 8);
                }
                else
                {
                    // Allocate new JFT in memory (use area after SYSVARS)
                    const int NEW_JFT_BASE = 0xE6E00;
                    // Copy existing entries
                    for (int j = 0; j < oldMax && j < newCount; j++)
                        jftMem[NEW_JFT_BASE + j] = jftMem[oldJftAddr + j];
                    // Fill new entries with 0xFF (unused)
                    for (int j = oldMax; j < newCount; j++)
                        jftMem[NEW_JFT_BASE + j] = 0xFF;
                    // Update PSP: new JFT pointer and count
                    ushort newJftSeg = (ushort)(NEW_JFT_BASE >> 4);
                    ushort newJftOff = (ushort)(NEW_JFT_BASE & 0x0F);
                    jftMem[pspAddr + 0x32] = (byte)(newCount & 0xFF);
                    jftMem[pspAddr + 0x33] = (byte)(newCount >> 8);
                    jftMem[pspAddr + 0x34] = (byte)(newJftOff & 0xFF);
                    jftMem[pspAddr + 0x35] = (byte)(newJftOff >> 8);
                    jftMem[pspAddr + 0x36] = (byte)(newJftSeg & 0xFF);
                    jftMem[pspAddr + 0x37] = (byte)(newJftSeg >> 8);
                    Console.Error.WriteLine($"[DOS] SetHandleCount {oldMax} → {newCount}, JFT at {newJftSeg:X4}:{newJftOff:X4}");
                }
                _cpu.Flags.CF = false;
                break;
            }

            default:
                // Unimplemented function - log and return with CF=0
                Console.Error.WriteLine($"[DOS] Unimplemented INT 21h AH={func:X2} AL={_cpu.AL:X2} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4}");
                _cpu.Flags.CF = false;
                break;
        }
    }

    /// <summary>
    /// Handle the full interrupt stub. Determines which interrupt was called
    /// and dispatches INT 21h. Other interrupts just IRET.
    /// </summary>
    public void HandleDosStub()
    {
        // Determine which interrupt was called by reading the byte before the stacked IP
        var mem = _bus.GetMemoryDirect();
        int sp = (_cpu.SS << 4) + _cpu.SP;
        ushort stkIP = (ushort)(mem[sp] | (mem[sp + 1] << 8));
        ushort stkCS = (ushort)(mem[sp + 2] | (mem[sp + 3] << 8));

        // The byte at CS:IP-1 is the interrupt number (from the CD xx instruction)
        int intByteAddr = ((stkCS << 4) + stkIP - 1) & 0xFFFFF;
        byte intNum = mem[intByteAddr];

        if (intNum == 0x21)
        {
            HandleInt21();
        }
        else if (intNum == 0x20)
        {
            // INT 20h: Terminate Program
            Terminate();
        }
        else if (intNum == 0x27)
        {
            // INT 27h: Terminate and Stay Resident
            // DX = last byte + 1 to keep resident
            _cpu.Flags.CF = false;
        }
        else
        {
            // Other DOS interrupts (INT 22h-26h, 28h-FFh)
            // Most are not commonly used; just return
        }

        // IRET: pop IP, CS, FLAGS
        DoIret();
    }

    private void DoIret()
    {
        bool cf = _cpu.Flags.CF;
        _cpu.IP = _cpu.Pop();
        _cpu.CS = _cpu.Pop();
        ushort flags = _cpu.Pop();
        _cpu.Flags.Value = flags;
        _cpu.Flags.CF = cf; // Preserve CF set by handler
    }

    // Text cursor position for DOS console output
    private int _cursorRow = 5; // Start below the banner area
    private int _cursorCol;
    private const int TEXT_COLS = 80;
    private const int TEXT_ROWS = 25;
    private const int TEXT_VRAM = 0xA0000;
    private const int ATTR_VRAM = 0xA2000;

    // Shift-JIS lead byte buffer for multi-byte character assembly
    private byte _sjisLeadByte;
    private bool _hasSjisLead;
    private bool _bootMenuCleaned;

    // ANSI escape sequence state
    private enum EscState { None, Esc, Csi }
    private EscState _escState;
    private string _escParams = "";

    private void HandleEscSequence(char cmd, string parms, byte[] mem)
    {
        switch (cmd)
        {
            case 'J': // Erase in Display
            {
                int mode = parms.Length > 0 ? int.Parse(parms) : 0;
                if (mode == 2)
                {
                    // Clear entire screen
                    for (int a = TEXT_VRAM; a < TEXT_VRAM + 0x2000; a += 2)
                    {
                        mem[a] = 0x20; mem[a + 1] = 0x00;
                    }
                    for (int a = ATTR_VRAM; a < ATTR_VRAM + 0x2000; a += 2)
                    {
                        mem[a] = 0x00; mem[a + 1] = 0x00;
                    }
                    _cursorRow = 0;
                    _cursorCol = 0;
                }
                break;
            }
            case 'H': // Cursor Position
            case 'f': // Cursor Position (same)
            {
                string[] parts = parms.Split(';');
                int row = parts.Length > 0 && parts[0].Length > 0 ? int.Parse(parts[0]) - 1 : 0;
                int col = parts.Length > 1 && parts[1].Length > 0 ? int.Parse(parts[1]) - 1 : 0;
                _cursorRow = Math.Clamp(row, 0, TEXT_ROWS - 1);
                _cursorCol = Math.Clamp(col, 0, TEXT_COLS - 1);
                break;
            }
            case 'K': // Erase in Line
            {
                int mode = parms.Length > 0 ? int.Parse(parms) : 0;
                if (mode == 0)
                {
                    // Clear from cursor to end of line
                    for (int c = _cursorCol; c < TEXT_COLS; c++)
                    {
                        int pos = (_cursorRow * TEXT_COLS + c) * 2;
                        mem[TEXT_VRAM + pos] = 0x20; mem[TEXT_VRAM + pos + 1] = 0x00;
                        mem[ATTR_VRAM + pos] = 0x00; mem[ATTR_VRAM + pos + 1] = 0x00;
                    }
                }
                break;
            }
            case 'A': // Cursor Up
            {
                int n = parms.Length > 0 ? int.Parse(parms) : 1;
                _cursorRow = Math.Max(0, _cursorRow - n);
                break;
            }
            case 'B': // Cursor Down
            {
                int n = parms.Length > 0 ? int.Parse(parms) : 1;
                _cursorRow = Math.Min(TEXT_ROWS - 1, _cursorRow + n);
                break;
            }
            case 'C': // Cursor Forward
            {
                int n = parms.Length > 0 ? int.Parse(parms) : 1;
                _cursorCol = Math.Min(TEXT_COLS - 1, _cursorCol + n);
                break;
            }
            case 'D': // Cursor Back
            {
                int n = parms.Length > 0 ? int.Parse(parms) : 1;
                _cursorCol = Math.Max(0, _cursorCol - n);
                break;
            }
        }
    }

    private static bool IsSjisLeadByte(byte b)
        => (b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC);

    private static (byte j1, byte j2) SjisToJis(byte s1, byte s2)
    {
        if (s1 >= 0xE0) s1 = (byte)(s1 - 0x40);
        if (s2 >= 0x80) s2 = (byte)(s2 - 1);

        byte j1, j2;
        if (s2 >= 0x9E)
        {
            j1 = (byte)((s1 - 0x70) * 2);
            j2 = (byte)(s2 - 0x7D);
        }
        else
        {
            j1 = (byte)((s1 - 0x70) * 2 - 1);
            j2 = (byte)(s2 - 0x1F);
        }
        return (j1, j2);
    }

    private void DisplayChar(byte ch)
    {
        var mem = _bus.GetMemoryDirect();

        // ANSI escape sequence handling (ESC [ ... letter)
        if (_escState == EscState.Esc)
        {
            if (ch == (byte)'[')
            {
                _escState = EscState.Csi;
                _escParams = "";
                return;
            }
            // ESC* (reset), ESC) etc. - single-char escape sequences, just consume
            _escState = EscState.None;
            return; // Swallow the character after ESC
        }
        else if (_escState == EscState.Csi)
        {
            // Accept digits, semicolons, and intermediate chars (>, =, ?, !)
            if (ch >= (byte)'0' && ch <= (byte)'9' || ch == (byte)';'
                || ch == (byte)'>' || ch == (byte)'=' || ch == (byte)'?' || ch == (byte)'!')
            {
                _escParams += (char)ch;
                return;
            }
            // Final character - execute the sequence
            _escState = EscState.None;
            HandleEscSequence((char)ch, _escParams, mem);
            return;
        }

        if (ch == 0x1B) // ESC
        {
            _escState = EscState.Esc;
            return;
        }

        // If we have a buffered Shift-JIS lead byte, combine with trail byte
        if (_hasSjisLead)
        {
            _hasSjisLead = false;
            byte lead = _sjisLeadByte;
            var (j1, j2) = SjisToJis(lead, ch);
            ushort jisCode = (ushort)(j2 | (j1 << 8));
            Console.Error.WriteLine($"[SJIS] {lead:X2}{ch:X2} → JIS {j1:X2}{j2:X2} (0x{jisCode:X4}) at ({_cursorRow},{_cursorCol})");

            // Write kanji to text VRAM (occupies 2 cells)
            if (_cursorCol >= TEXT_COLS - 1)
            {
                // Not enough room on this line, wrap to next
                _cursorCol = 0;
                _cursorRow++;
                if (_cursorRow >= TEXT_ROWS) { ScrollUp(mem); _cursorRow = TEXT_ROWS - 1; }
            }

            int pos = (_cursorRow * TEXT_COLS + _cursorCol) * 2;
            mem[TEXT_VRAM + pos] = j2;       // JIS column (low byte)
            mem[TEXT_VRAM + pos + 1] = j1;   // JIS row (high byte)
            mem[ATTR_VRAM + pos] = 0xE1;
            mem[ATTR_VRAM + pos + 1] = 0x00;

            // Second cell: mark as continuation (empty with attribute)
            int pos2 = pos + 2;
            mem[TEXT_VRAM + pos2] = 0x00;
            mem[TEXT_VRAM + pos2 + 1] = 0x00;
            mem[ATTR_VRAM + pos2] = 0xE1;
            mem[ATTR_VRAM + pos2 + 1] = 0x00;

            _cursorCol += 2;
            if (_cursorCol >= TEXT_COLS)
            {
                _cursorCol = 0;
                _cursorRow++;
                if (_cursorRow >= TEXT_ROWS) { ScrollUp(mem); _cursorRow = TEXT_ROWS - 1; }
            }
            return;
        }

        // Check if this is a Shift-JIS lead byte
        if (IsSjisLeadByte(ch))
        {
            _sjisLeadByte = ch;
            _hasSjisLead = true;
            return;
        }

        if (ch == 0x0D) // CR
        {
            _cursorCol = 0;
            return;
        }
        if (ch == 0x0A) // LF
        {
            _cursorRow++;
            if (_cursorRow >= TEXT_ROWS)
            {
                ScrollUp(mem);
                _cursorRow = TEXT_ROWS - 1;
            }
            return;
        }
        if (ch == 0x08) // BS
        {
            if (_cursorCol > 0) _cursorCol--;
            return;
        }
        if (ch == 0x07) // BEL
            return;

        // Write ANK character to text VRAM
        int pos3 = (_cursorRow * TEXT_COLS + _cursorCol) * 2;
        mem[TEXT_VRAM + pos3] = ch;
        mem[TEXT_VRAM + pos3 + 1] = 0x00; // High byte 0 = ANK
        mem[ATTR_VRAM + pos3] = 0xE1;
        mem[ATTR_VRAM + pos3 + 1] = 0x00;

        _cursorCol++;
        if (_cursorCol >= TEXT_COLS)
        {
            _cursorCol = 0;
            _cursorRow++;
            if (_cursorRow >= TEXT_ROWS)
            {
                ScrollUp(mem);
                _cursorRow = TEXT_ROWS - 1;
            }
        }
    }

    private void ScrollUp(byte[] mem)
    {
        // Scroll text VRAM up by one line
        int lineBytes = TEXT_COLS * 2;
        for (int row = 0; row < TEXT_ROWS - 1; row++)
        {
            int dst = row * lineBytes;
            int src = (row + 1) * lineBytes;
            Array.Copy(mem, TEXT_VRAM + src, mem, TEXT_VRAM + dst, lineBytes);
            Array.Copy(mem, ATTR_VRAM + src, mem, ATTR_VRAM + dst, lineBytes);
        }
        // Clear last line
        int lastLine = (TEXT_ROWS - 1) * lineBytes;
        for (int i = 0; i < lineBytes; i++)
        {
            mem[TEXT_VRAM + lastLine + i] = 0x00;
            mem[ATTR_VRAM + lastLine + i] = 0x00;
        }
    }

    /// <summary>
    /// AH=0A: Buffered keyboard input.
    /// Injects a small spin loop at a trampoline address that calls INT 18h AH=00
    /// (keyboard wait) until Enter is pressed, then returns to the original caller.
    /// </summary>
    public bool WaitingForInput { get; set; }

    private void BufferedInput()
    {
        int bufAddr = (_cpu.DS << 4) + _cpu.DX;
        var mem = _bus.GetMemoryDirect();
        int maxLen = mem[bufAddr & 0xFFFFF];
        if (maxLen == 0) maxLen = 1;

        // Store buffer info for the keyboard input handler
        _inputBufPhys = bufAddr;
        _inputMaxLen = maxLen;

        // Return empty line for now — but signal waiting state
        mem[(bufAddr + 1) & 0xFFFFF] = 0x00; // 0 bytes read
        mem[(bufAddr + 2) & 0xFFFFF] = 0x0D; // CR

        WaitingForInput = true;
        SkipIret = true; // Don't IRET — we'll resume when input is done

        // Save the return address from the INT 21h stack frame
        var stackMem = _bus.GetMemoryDirect();
        int sp = (_cpu.SS << 4) + _cpu.SP;
        _savedRetIP = (ushort)(stackMem[sp] | (stackMem[sp + 1] << 8));
        _savedRetCS = (ushort)(stackMem[sp + 2] | (stackMem[sp + 3] << 8));
        _savedRetFlags = (ushort)(stackMem[sp + 4] | (stackMem[sp + 5] << 8));
        _cpu.SP += 6; // Pop the INT frame

        // Inject HLT loop at a safe ROM address — CPU will halt until input is ready
        // We use the emulator's HLT handling to process keyboard events
        _cpu.Halted = true;

        Console.Error.WriteLine($"[DOS] BufferedInput: waiting at cursor ({_cursorRow},{_cursorCol}), maxLen={maxLen}, ret={_savedRetCS:X4}:{_savedRetIP:X4}");
    }

    private int _inputBufPhys;
    private int _inputMaxLen;
    private ushort _savedRetIP, _savedRetCS, _savedRetFlags;

    // Previous command buffer for function key editing (F1-F5, F9)
    private byte[] _prevCommand = Array.Empty<byte>();
    private int _prevCmdCursor; // Position in previous command for F1/F4 copy/skip
    private bool _insertMode; // F8 toggle
    private bool _waitingForF2Char; // F2: waiting for target character
    private bool _waitingForF5Char; // F5: waiting for target character

    /// <summary>
    /// Called by emulator when a key is pressed during WaitingForInput state.
    /// funcKey: 1-10 for F1-F10, 0 for normal key.
    /// Returns true if input is complete (Enter pressed).
    /// </summary>
    public bool HandleKeyInput(byte ascii, byte scancode, byte funcKey = 0)
    {
        if (!WaitingForInput) return false;
        var mem = _bus.GetMemoryDirect();

        int curLen = mem[(_inputBufPhys + 1) & 0xFFFFF];

        // F2/F5: waiting for a target character
        if (_waitingForF2Char && ascii >= 0x20)
        {
            _waitingForF2Char = false;
            // Copy from previous command up to (but not including) the target character
            for (int i = _prevCmdCursor; i < _prevCommand.Length && curLen < _inputMaxLen - 1; i++)
            {
                if (_prevCommand[i] == ascii) { _prevCmdCursor = i; break; }
                mem[(_inputBufPhys + 2 + curLen) & 0xFFFFF] = _prevCommand[i];
                curLen++;
                DisplayChar(_prevCommand[i]);
                _prevCmdCursor = i + 1;
            }
            mem[(_inputBufPhys + 1) & 0xFFFFF] = (byte)curLen;
            return false;
        }
        if (_waitingForF5Char && ascii >= 0x20)
        {
            _waitingForF5Char = false;
            // Skip in previous command up to (but not including) the target character
            for (int i = _prevCmdCursor; i < _prevCommand.Length; i++)
            {
                if (_prevCommand[i] == ascii) { _prevCmdCursor = i; return false; }
                _prevCmdCursor = i + 1;
            }
            return false;
        }

        // Handle function keys
        if (funcKey != 0)
        {
            switch (funcKey)
            {
                case 1: // F1 = C1: Copy one character from previous command
                    if (_prevCmdCursor < _prevCommand.Length && curLen < _inputMaxLen - 1)
                    {
                        byte ch = _prevCommand[_prevCmdCursor++];
                        mem[(_inputBufPhys + 2 + curLen) & 0xFFFFF] = ch;
                        curLen++;
                        mem[(_inputBufPhys + 1) & 0xFFFFF] = (byte)curLen;
                        DisplayChar(ch);
                    }
                    break;

                case 2: // F2 = CU: Copy up to specified character
                    _waitingForF2Char = true;
                    break;

                case 3: // F3 = CA: Copy all remaining from previous command
                    while (_prevCmdCursor < _prevCommand.Length && curLen < _inputMaxLen - 1)
                    {
                        byte ch = _prevCommand[_prevCmdCursor++];
                        mem[(_inputBufPhys + 2 + curLen) & 0xFFFFF] = ch;
                        curLen++;
                        DisplayChar(ch);
                    }
                    mem[(_inputBufPhys + 1) & 0xFFFFF] = (byte)curLen;
                    break;

                case 4: // F4 = S1: Skip one character in previous command
                    if (_prevCmdCursor < _prevCommand.Length)
                        _prevCmdCursor++;
                    break;

                case 5: // F5 = SU: Skip up to specified character
                    _waitingForF5Char = true;
                    break;

                case 6: // F6 = VOID: Insert Ctrl+Z (EOF)
                case 10: // F10 = ^Z: Insert Ctrl+Z (EOF)
                    if (curLen < _inputMaxLen - 1)
                    {
                        mem[(_inputBufPhys + 2 + curLen) & 0xFFFFF] = 0x1A;
                        curLen++;
                        mem[(_inputBufPhys + 1) & 0xFFFFF] = (byte)curLen;
                        DisplayChar((byte)'^');
                        DisplayChar((byte)'Z');
                    }
                    break;

                case 7: // F7 = NWL: Newline (submit current input)
                    ascii = 0x0D;
                    break; // Fall through to Enter handling below

                case 8: // F8 = INS: Toggle insert mode
                    _insertMode = !_insertMode;
                    break;

                case 9: // F9 = REP: Replace - not yet implemented (would need char input)
                    break;
            }
            if (funcKey != 7) return false; // F7 falls through to Enter
        }

        if (ascii == 0x0D) // Enter
        {
            // Save current input as previous command
            _prevCommand = new byte[curLen];
            for (int i = 0; i < curLen; i++)
                _prevCommand[i] = mem[(_inputBufPhys + 2 + i) & 0xFFFFF];
            _prevCmdCursor = 0;

            mem[(_inputBufPhys + 2 + curLen) & 0xFFFFF] = 0x0D;
            DisplayChar(0x0D);
            DisplayChar(0x0A);
            WaitingForInput = false;
            _cpu.IP = _savedRetIP;
            _cpu.CS = _savedRetCS;
            _cpu.Flags.Value = _savedRetFlags;
            _cpu.Halted = false;
            Console.Error.WriteLine($"[DOS] Input complete: {curLen} chars, resume at {_savedRetCS:X4}:{_savedRetIP:X4}");
            return true;
        }

        if (ascii == 0x08) // Backspace
        {
            if (curLen > 0)
            {
                curLen--;
                mem[(_inputBufPhys + 1) & 0xFFFFF] = (byte)curLen;
                DisplayChar(0x08);
                DisplayChar((byte)' ');
                DisplayChar(0x08);
                // Also back up the previous command cursor if applicable
                if (_prevCmdCursor > 0) _prevCmdCursor--;
            }
            return false;
        }

        if (ascii >= 0x20 && curLen < _inputMaxLen - 1)
        {
            mem[(_inputBufPhys + 2 + curLen) & 0xFFFFF] = ascii;
            curLen++;
            mem[(_inputBufPhys + 1) & 0xFFFFF] = (byte)curLen;
            DisplayChar(ascii);
            // Advance previous command cursor in parallel
            if (_prevCmdCursor < _prevCommand.Length) _prevCmdCursor++;
        }

        return false;
    }

    private void DirectConsoleIO()
    {
        if (_cpu.DL == 0xFF)
        {
            // Input: no character available
            _cpu.AL = 0x00;
            _cpu.Flags.ZF = true;
        }
        else
        {
            // Output
            DisplayChar(_cpu.DL);
        }
    }

    private void DisplayString()
    {
        // DS:DX points to '$'-terminated string
        int addr = (_cpu.DS << 4) + _cpu.DX;
        var mem = _bus.GetMemoryDirect();

        // Debug: hex dump first 64 bytes
        var sb = new System.Text.StringBuilder();
        sb.Append($"[DOS] AH=09 @{_cpu.DS:X4}:{_cpu.DX:X4}: ");
        for (int i = 0; i < 64; i++)
        {
            byte b = mem[(addr + i) & 0xFFFFF];
            if (b == (byte)'$') { sb.Append("24($)"); break; }
            sb.Append($"{b:X2} ");
        }
        Console.Error.WriteLine(sb.ToString());

        for (int i = 0; i < 4096; i++)
        {
            byte ch = mem[(addr + i) & 0xFFFFF];
            if (ch == (byte)'$') break;
            DisplayChar(ch);
        }
    }

    private void SelectDisk()
    {
        _currentDrive = _cpu.DL;
        _cpu.AL = 26; // Number of logical drives
    }

    private void SetInterruptVector()
    {
        byte vector = _cpu.AL;
        int ivtAddr = vector * 4;
        _bus.WriteMemoryWord(ivtAddr, _cpu.DX);
        _bus.WriteMemoryWord(ivtAddr + 2, _cpu.DS);
    }

    /// <summary>
    /// AH=29: Parse Filename into FCB.
    /// DS:SI → string to parse, ES:DI → FCB to fill.
    /// AL = parse control bits (bit0=skip leading separators, bit1=set drive only if specified,
    ///       bit2=set filename only if specified, bit3=set extension only if specified).
    /// Returns: AL=00 (no wildcards), 01 (wildcards), FF (invalid drive). DS:SI advanced past parsed name.
    /// </summary>
    private void ParseFilename()
    {
        var mem = _bus.GetMemoryDirect();
        int si = (_cpu.DS << 4) + _cpu.SI;
        int di = (_cpu.ES << 4) + _cpu.DI;
        byte ctrl = _cpu.AL;

        // Skip leading separators/spaces if bit 0 set
        if ((ctrl & 0x01) != 0)
        {
            while (true)
            {
                byte c = mem[si & 0xFFFFF];
                if (c == ' ' || c == '\t' || c == ';' || c == ',' || c == '=')
                    si++;
                else
                    break;
            }
        }

        // Initialize FCB: drive=0, filename=spaces, extension=spaces
        bool setDrive = (ctrl & 0x02) == 0;
        bool setName = (ctrl & 0x04) == 0;
        bool setExt = (ctrl & 0x08) == 0;

        if (setDrive) mem[(di + 0) & 0xFFFFF] = 0x00; // Default drive
        if (setName) for (int i = 1; i <= 8; i++) mem[(di + i) & 0xFFFFF] = 0x20; // Spaces
        if (setExt) for (int i = 9; i <= 11; i++) mem[(di + i) & 0xFFFFF] = 0x20; // Spaces

        bool hasWildcard = false;

        // Check for drive letter
        byte first = mem[si & 0xFFFFF];
        byte second = mem[(si + 1) & 0xFFFFF];
        if (second == (byte)':' && ((first >= (byte)'A' && first <= (byte)'Z') || (first >= (byte)'a' && first <= (byte)'z')))
        {
            byte drive = (byte)((first & 0xDF) - 'A' + 1); // 1=A, 2=B, etc.
            if (setDrive) mem[(di + 0) & 0xFFFFF] = drive;
            si += 2;
        }

        // Parse filename (up to 8 chars)
        int namePos = 0;
        while (namePos < 8)
        {
            byte c = mem[si & 0xFFFFF];
            if (c == '.' || c == 0x0D || c == 0x00 || c == ' ' || c == '/' || c == '\\' ||
                c == '\t' || c == ';' || c == ',' || c == '=' || c == '+' || c == '<' ||
                c == '>' || c == '|' || c == '[' || c == ']')
                break;
            if (c == '*')
            {
                hasWildcard = true;
                for (int i = namePos; i < 8; i++)
                    mem[(di + 1 + i) & 0xFFFFF] = (byte)'?';
                namePos = 8;
                si++;
                break;
            }
            if (c == '?') hasWildcard = true;
            if (setName)
                mem[(di + 1 + namePos) & 0xFFFFF] = (byte)(c >= 'a' && c <= 'z' ? c - 32 : c);
            namePos++;
            si++;
        }

        // Parse extension if dot found
        if (mem[si & 0xFFFFF] == (byte)'.')
        {
            si++; // skip dot
            int extPos = 0;
            while (extPos < 3)
            {
                byte c = mem[si & 0xFFFFF];
                if (c == 0x0D || c == 0x00 || c == ' ' || c == '.' || c == '/' || c == '\\' ||
                    c == '\t' || c == ';' || c == ',' || c == '=' || c == '+' || c == '<' ||
                    c == '>' || c == '|' || c == '[' || c == ']')
                    break;
                if (c == '*')
                {
                    hasWildcard = true;
                    for (int i = extPos; i < 3; i++)
                        mem[(di + 9 + i) & 0xFFFFF] = (byte)'?';
                    extPos = 3;
                    si++;
                    break;
                }
                if (c == '?') hasWildcard = true;
                if (setExt)
                    mem[(di + 9 + extPos) & 0xFFFFF] = (byte)(c >= 'a' && c <= 'z' ? c - 32 : c);
                extPos++;
                si++;
            }
        }

        // Update SI to point past parsed text
        _cpu.SI = (ushort)(si - (_cpu.DS << 4));
        _cpu.AL = hasWildcard ? (byte)0x01 : (byte)0x00;
    }

    private void GetInterruptVector()
    {
        byte vector = _cpu.AL;
        int ivtAddr = vector * 4;
        _cpu.BX = _bus.ReadMemoryWord(ivtAddr);
        _cpu.ES = _bus.ReadMemoryWord(ivtAddr + 2);
        if (vector == 0x41 || vector == 0x42 || (vector >= 0x60 && vector <= 0x70))
            Console.Error.WriteLine($"[INT21-35] GetVector INT {vector:X2} → {_cpu.ES:X4}:{_cpu.BX:X4} from {_cpu.CS:X4}:{_cpu.IP:X4}");
    }

    private void GetDate()
    {
        var now = DateTime.Now;
        _cpu.CX = (ushort)now.Year;
        _cpu.DH = (byte)now.Month;
        _cpu.DL = (byte)now.Day;
        _cpu.AL = (byte)now.DayOfWeek;
    }

    private void GetTime()
    {
        var now = DateTime.Now;
        _cpu.CH = (byte)now.Hour;
        _cpu.CL = (byte)now.Minute;
        _cpu.DH = (byte)now.Second;
        _cpu.DL = (byte)(now.Millisecond / 10);
    }

    private void CreateFile()
    {
        // Find free handle
        for (int i = 5; i < MAX_HANDLES; i++)
        {
            if (!_handleOpen[i])
            {
                _handleOpen[i] = true;
                _cpu.AX = (ushort)i;
                _cpu.Flags.CF = false;
                return;
            }
        }
        _cpu.AX = 0x04; // Too many open files
        _cpu.Flags.CF = true;
    }

    private void OpenFile()
    {
        // Read filename from DS:DX
        int nameAddr = (_cpu.DS << 4) + _cpu.DX;
        var mem = _bus.GetMemoryDirect();
        string filename = "";
        for (int i = 0; i < 128; i++)
        {
            byte ch = mem[(nameAddr + i) & 0xFFFFF];
            if (ch == 0) break;
            filename += (char)ch;
        }

        // Find free handle
        for (int i = 5; i < MAX_HANDLES; i++)
        {
            if (!_handleOpen[i])
            {
                _handleOpen[i] = true;
                _filePosition[i] = 0;
                _fileSize[i] = 0;
                _fileData[i] = null;

                // Try to load file from FAT16
                // Determine the drive from the filename, or use current drive
                byte fileDrive = _currentDrive;
                string lookupName = filename;
                if (filename.Length >= 2 && filename[1] == ':')
                {
                    fileDrive = (byte)(char.ToUpper(filename[0]) - 'A');
                    lookupName = filename.Substring(2);
                }

                // If the filename has no directory component, prepend current directory
                if (!lookupName.Contains('\\') && !lookupName.Contains('/'))
                {
                    string curDir = _currentDirs[fileDrive] ?? "";
                    if (curDir.Length > 0)
                        lookupName = curDir + "\\" + lookupName;
                }

                var fat16 = _fatReaders[fileDrive];
                if (fat16 != null && fat16.IsInitialized)
                {
                    var (cluster, size) = fat16.FindFile(lookupName);
                    if (cluster == 0)
                        Console.Error.WriteLine($"[DOS] OpenFile '{filename}' resolved to '{lookupName}' on drive {fileDrive}");
                    if (cluster != 0)
                    {
                        _fileSize[i] = size;
                        _fileData[i] = fat16.ReadFile(cluster, size);
                        Console.Error.WriteLine($"[DOS] OpenFile '{filename}' → handle {i}, size={size}, loaded={_fileData[i] != null}");
                    }
                    else
                    {
                        // File not found
                        _handleOpen[i] = false;
                        _cpu.AX = 0x02; // File not found
                        _cpu.Flags.CF = true;
                        Console.Error.WriteLine($"[DOS] OpenFile '{filename}' → not found");
                        return;
                    }
                }

                _cpu.AX = (ushort)i;
                _cpu.Flags.CF = false;
                UpdateSftEntry(i);
                UpdateJft(i, (byte)i); // Map handle to SFT index
                return;
            }
        }
        _cpu.AX = 0x04; // Too many open files
        _cpu.Flags.CF = true;
        Console.Error.WriteLine($"[DOS] OpenFile '{filename}' FAILED (too many files)");
    }

    private void CloseFile()
    {
        ushort handle = _cpu.BX;
        if (handle < MAX_HANDLES)
        {
            _handleOpen[handle] = false;
            _fileData[handle] = null;
            _filePosition[handle] = 0;
            _fileSize[handle] = 0;
            _cpu.Flags.CF = false;
            UpdateSftEntry(handle);
            UpdateJft(handle, 0xFF); // Mark JFT entry as unused
        }
        else
        {
            _cpu.AX = 0x06; // Invalid handle
            _cpu.Flags.CF = true;
        }
    }

    private void ReadFile()
    {
        // CX = bytes to read, BX = handle, DS:DX = buffer
        ushort handle = _cpu.BX;
        ushort count = _cpu.CX;
        int bufAddr = (_cpu.DS << 4) + _cpu.DX;
        var mem = _bus.GetMemoryDirect();

        if (handle >= MAX_HANDLES || !_handleOpen[handle])
        {
            _cpu.AX = 0x06; // Invalid handle
            _cpu.Flags.CF = true;
            return;
        }

        // STDIN (handle 0): read from keyboard
        if (handle == 0)
        {
            _cpu.AX = 0;
            _cpu.Flags.CF = false;
            return;
        }

        // Sync position from SFT (overlay manager may have changed it directly)
        SyncFromSft(handle);

        // Read from cached file data
        byte[]? data = _fileData[handle];
        if (data == null)
        {
            _cpu.AX = 0; // EOF
            _cpu.Flags.CF = false;
            return;
        }

        uint pos = _filePosition[handle];
        int bytesRead = 0;
        for (int i = 0; i < count && pos + i < data.Length; i++)
        {
            mem[(bufAddr + i) & 0xFFFFF] = data[pos + i];
            bytesRead++;
        }
        _filePosition[handle] = pos + (uint)bytesRead;
        _cpu.AX = (ushort)bytesRead;
        _cpu.Flags.CF = false;
        UpdateSftEntry(handle);
    }

    private void WriteFile()
    {
        // CX = bytes to write, BX = handle, DS:DX = buffer
        ushort handle = _cpu.BX;
        ushort count = _cpu.CX;

        if (handle == 1 || handle == 2)
        {
            // STDOUT/STDERR: output to text VRAM
            int addr = (_cpu.DS << 4) + _cpu.DX;
            var mem = _bus.GetMemoryDirect();

            // Debug: hex dump first 64 bytes of output
            var sb = new System.Text.StringBuilder();
            sb.Append($"[DOS] Write h={handle} {count}B @{_cpu.DS:X4}:{_cpu.DX:X4}: ");
            for (int i = 0; i < Math.Min((int)count, 64); i++)
                sb.Append($"{mem[(addr + i) & 0xFFFFF]:X2} ");
            Console.Error.WriteLine(sb.ToString());

            for (int i = 0; i < count; i++)
            {
                byte ch = mem[(addr + i) & 0xFFFFF];
                DisplayChar(ch);
            }

            // Verify VRAM after write: dump first 40 chars of current row
            if (count > 4)
            {
                var vramSb = new System.Text.StringBuilder();
                int verifyRow = _cursorRow > 0 ? _cursorRow - 1 : _cursorRow; // prev row (after CR/LF)
                vramSb.Append($"[VRAM] Row {verifyRow}: ");
                for (int c = 0; c < 40; c++)
                {
                    int vpos = (verifyRow * TEXT_COLS + c) * 2;
                    ushort code = (ushort)(mem[TEXT_VRAM + vpos] | (mem[TEXT_VRAM + vpos + 1] << 8));
                    vramSb.Append($"{code:X4} ");
                }
                Console.Error.WriteLine(vramSb.ToString());
            }
        }

        _cpu.AX = count; // All bytes written
        _cpu.Flags.CF = false;
    }

    private void Seek()
    {
        // AL=method (0=start, 1=current, 2=end), BX=handle, CX:DX=offset
        byte method = _cpu.AL;
        ushort handle = _cpu.BX;
        int offset = (_cpu.CX << 16) | _cpu.DX; // Signed 32-bit offset

        uint curPos = (handle < MAX_HANDLES) ? _filePosition[handle] : 0;
        uint size = (handle < MAX_HANDLES) ? _fileSize[handle] : 0;

        // For file handles with unknown size, use MSDOS.SYS default (40960 bytes)
        if (size == 0 && handle >= 5)
            size = 0xA000;

        uint newPos;
        switch (method)
        {
            case 0: // From start
                newPos = (uint)offset;
                break;
            case 1: // From current
                newPos = (uint)(curPos + offset);
                break;
            case 2: // From end
                newPos = (uint)(size + offset);
                break;
            default:
                newPos = curPos;
                break;
        }

        if (handle < MAX_HANDLES)
        {
            _filePosition[handle] = newPos;
            UpdateSftEntry(handle);
        }

        Console.Error.WriteLine($"[DOS] LSEEK handle={handle} method={method} offset={offset} size={size} → pos={newPos}");

        // Return new position in DX:AX
        _cpu.DX = (ushort)(newPos >> 16);
        _cpu.AX = (ushort)(newPos & 0xFFFF);
        _cpu.Flags.CF = false;
    }

    private void HandleIoctl()
    {
        byte subFunc = _cpu.AL;
        switch (subFunc)
        {
            case 0x00: // Get Device Information
                // Return: DX = device info word
                // Bit 7 = 1 for character device
                if (_cpu.BX <= 4)
                    _cpu.DX = 0x80D3; // Character device, STDIN/STDOUT
                else
                    _cpu.DX = 0x0002; // Block device, drive C:
                _cpu.Flags.CF = false;
                break;

            case 0x01: // Set Device Information
                _cpu.Flags.CF = false;
                break;

            case 0x08: // Check if block device is removable
                _cpu.AX = 0x0001; // Fixed disk
                _cpu.Flags.CF = false;
                break;

            default:
                _cpu.Flags.CF = false;
                break;
        }
    }

    private void ChangeDirectory()
    {
        // DS:DX = ASCIZ path
        int nameAddr = (_cpu.DS << 4) + _cpu.DX;
        var mem = _bus.GetMemoryDirect();
        string path = "";
        for (int i = 0; i < 128; i++)
        {
            byte ch = mem[(nameAddr + i) & 0xFFFFF];
            if (ch == 0) break;
            path += (char)ch;
        }

        // Determine which drive
        byte drive = _currentDrive;
        if (path.Length >= 2 && path[1] == ':')
        {
            drive = (byte)(char.ToUpper(path[0]) - 'A');
            path = path.Substring(2);
        }

        // Normalize
        path = path.Replace('/', '\\').Trim('\\');

        // Handle special cases
        if (path == "." || path == "")
        {
            // Stay in current dir (or go to root if empty after stripping)
            if (path == "") _currentDirs[drive] = "";
        }
        else if (path == "..")
        {
            // Go up one level
            string cur = _currentDirs[drive] ?? "";
            int lastSep = cur.LastIndexOf('\\');
            _currentDirs[drive] = lastSep >= 0 ? cur.Substring(0, lastSep) : "";
        }
        else if (path.StartsWith("\\"))
        {
            // Absolute path
            _currentDirs[drive] = path.TrimStart('\\');
        }
        else
        {
            // Could be absolute or relative — if starts from root, treat as absolute
            _currentDirs[drive] = path;
        }

        Console.Error.WriteLine($"[DOS] ChDir drive={drive} path='{path}' → currentDir='{_currentDirs[drive]}'");
        _cpu.Flags.CF = false;
    }

    private void GetCurrentDirectory()
    {
        // DL = drive (0=default, 1=A, 2=B, ...)
        // DS:SI = buffer for path (64 bytes, no drive letter, no leading backslash)
        byte drive = _cpu.DL == 0 ? _currentDrive : (byte)(_cpu.DL - 1);
        int addr = (_cpu.DS << 4) + _cpu.SI;
        var mem = _bus.GetMemoryDirect();
        string dir = _currentDirs[drive] ?? "";
        for (int i = 0; i < dir.Length && i < 63; i++)
            mem[(addr + i) & 0xFFFFF] = (byte)dir[i];
        mem[(addr + dir.Length) & 0xFFFFF] = 0x00;
        _cpu.Flags.CF = false;
    }

    /// <summary>
    /// Reset memory allocations and set up a single block for a directly loaded program.
    /// Used when the emulator bypasses DOS EXEC to load a game.
    /// </summary>
    public void ResetAllocationsForDirectLoad(ushort pspSeg, ushort totalParagraphs)
    {
        _allocBlocks.Clear();
        _memBaseSeg = (ushort)(pspSeg - 1); // MCB starts one paragraph before PSP
        _allocBlocks.Add((pspSeg, totalParagraphs));
        Console.Error.WriteLine($"[DOS] ResetAlloc: cleared all blocks, set {pspSeg:X4} size={totalParagraphs:X4} (end={pspSeg + totalParagraphs:X4})");
        RebuildMcbChain();
    }

    /// <summary>
    /// Rebuild the MCB (Memory Control Block) chain in memory from our allocation list.
    /// MCB format: Byte 0 = 'M' or 'Z', Bytes 1-2 = owner PSP, Bytes 3-4 = size in paragraphs
    /// Each MCB is 1 paragraph (16 bytes) before the allocated block.
    /// </summary>
    private void RebuildMcbChain()
    {
        var mem = _bus.GetMemoryDirect();

        // Sort allocations by segment
        var sorted = _allocBlocks.OrderBy(b => b.seg).ToList();
        if (sorted.Count == 0) return;

        // Build MCB chain
        // First MCB is at _memBaseSeg (one paragraph before first allocated block)
        for (int i = 0; i < sorted.Count; i++)
        {
            ushort blockSeg = sorted[i].seg;
            ushort blockSize = sorted[i].size;
            ushort mcbSeg = (ushort)(blockSeg - 1); // MCB is one paragraph before the block
            int mcbAddr = mcbSeg << 4;

            bool isLast = (i == sorted.Count - 1);

            if (isLast)
            {
                // Last MCB: type='M' for the allocation, then a final 'Z' MCB for remaining free space
                mem[mcbAddr] = (byte)'M';
                mem[mcbAddr + 1] = (byte)(_currentPSP & 0xFF);
                mem[mcbAddr + 2] = (byte)(_currentPSP >> 8);
                mem[mcbAddr + 3] = (byte)(blockSize & 0xFF);
                mem[mcbAddr + 4] = (byte)(blockSize >> 8);
                // Clear rest of MCB
                for (int j = 5; j < 16; j++) mem[mcbAddr + j] = 0;

                // Add final free MCB at end of this block
                ushort freeMcbSeg = (ushort)(blockSeg + blockSize);
                if (freeMcbSeg < MEM_TOP_SEG)
                {
                    int freeAddr = freeMcbSeg << 4;
                    ushort freeSize = (ushort)(MEM_TOP_SEG - freeMcbSeg - 1);
                    mem[freeAddr] = (byte)'Z'; // Last MCB
                    mem[freeAddr + 1] = 0x00; // Owner = 0 (free)
                    mem[freeAddr + 2] = 0x00;
                    mem[freeAddr + 3] = (byte)(freeSize & 0xFF);
                    mem[freeAddr + 4] = (byte)(freeSize >> 8);
                    for (int j = 5; j < 16; j++) mem[freeAddr + j] = 0;
                }
            }
            else
            {
                // Middle MCB
                ushort nextSeg = sorted[i + 1].seg;
                mem[mcbAddr] = (byte)'M';
                mem[mcbAddr + 1] = (byte)(_currentPSP & 0xFF);
                mem[mcbAddr + 2] = (byte)(_currentPSP >> 8);
                mem[mcbAddr + 3] = (byte)(blockSize & 0xFF);
                mem[mcbAddr + 4] = (byte)(blockSize >> 8);
                for (int j = 5; j < 16; j++) mem[mcbAddr + j] = 0;

                // If there's a gap between this block and the next, add a free MCB
                ushort gapSeg = (ushort)(blockSeg + blockSize);
                if (gapSeg + 1 < nextSeg - 1) // Need room for gap MCB + at least 0 data paragraphs
                {
                    int gapAddr = gapSeg << 4;
                    ushort gapSize = (ushort)(nextSeg - gapSeg - 2); // gap + 1 MCB para + gapSize = next MCB
                    mem[gapAddr] = (byte)'M';
                    mem[gapAddr + 1] = 0x00; // Free
                    mem[gapAddr + 2] = 0x00;
                    mem[gapAddr + 3] = (byte)(gapSize & 0xFF);
                    mem[gapAddr + 4] = (byte)(gapSize >> 8);
                    for (int j = 5; j < 16; j++) mem[gapAddr + j] = 0;
                }
            }
        }

        Console.Error.WriteLine($"[DOS] MCB chain rebuilt: {sorted.Count} blocks, base={_memBaseSeg:X4}");
    }

    private ushort GetNextFreeSeg()
    {
        // Find the end of the highest allocated block, or memBaseSeg if none
        ushort highest = _memBaseSeg;
        foreach (var (seg, size) in _allocBlocks)
        {
            ushort end = (ushort)(seg + size);
            if (end > highest) highest = end;
        }
        return highest;
    }

    private void AllocateMemory()
    {
        // BX = paragraphs requested
        ushort paragraphs = _cpu.BX;
        ushort nextFree = GetNextFreeSeg();
        ushort available = (ushort)(nextFree < MEM_TOP_SEG ? MEM_TOP_SEG - nextFree : 0);

        Console.Error.WriteLine($"[DOS] AllocMem BX={paragraphs:X4} ({paragraphs * 16} bytes) nextFree={nextFree:X4} avail={available:X4}");

        if (paragraphs <= available)
        {
            ushort segment = (ushort)(nextFree + 1); // +1 for MCB paragraph
            available = (ushort)(nextFree + 1 < MEM_TOP_SEG ? MEM_TOP_SEG - nextFree - 1 : 0);
            if (paragraphs <= available)
            {
                _allocBlocks.Add((segment, paragraphs));
                _cpu.AX = segment;
                _cpu.Flags.CF = false;
                Console.Error.WriteLine($"[DOS] AllocMem → segment {segment:X4}");
                RebuildMcbChain();
            }
            else
            {
                _cpu.AX = 0x08;
                _cpu.BX = available;
                _cpu.Flags.CF = true;
                Console.Error.WriteLine($"[DOS] AllocMem FAILED (MCB), max avail={available:X4} paragraphs");
            }
        }
        else
        {
            _cpu.AX = 0x08; // Insufficient memory
            _cpu.BX = available;
            _cpu.Flags.CF = true;
            Console.Error.WriteLine($"[DOS] AllocMem FAILED, max avail={available:X4} paragraphs");
        }
    }

    private void FreeMemory()
    {
        ushort segment = _cpu.ES;
        int idx = _allocBlocks.FindIndex(b => b.seg == segment);
        if (idx >= 0)
        {
            Console.Error.WriteLine($"[DOS] FreeMem ES={segment:X4} size={_allocBlocks[idx].size:X4}");
            _allocBlocks.RemoveAt(idx);
            RebuildMcbChain();
        }
        else
        {
            Console.Error.WriteLine($"[DOS] FreeMem ES={segment:X4} (not tracked)");
        }
        _cpu.Flags.CF = false;
    }

    private void GetDiskFreeSpace()
    {
        // DL = drive (0=default, 1=A, etc.)
        // Returns: AX=sectors/cluster, BX=free clusters, CX=bytes/sector, DX=total clusters
        // Or AX=FFFF on error
        _cpu.AX = 4;      // 4 sectors per cluster
        _cpu.BX = 1000;   // 1000 free clusters
        _cpu.CX = 512;    // 512 bytes per sector
        _cpu.DX = 2048;   // 2048 total clusters (~4MB)
        Console.Error.WriteLine($"[DOS] GetDiskFreeSpace drive={_cpu.DL}");
    }

    private void ResizeMemory()
    {
        ushort segment = _cpu.ES;
        ushort newSize = _cpu.BX;

        int idx = _allocBlocks.FindIndex(b => b.seg == segment);
        if (idx < 0)
        {
            // Not tracked — add it as a new block with size 0
            _allocBlocks.Add((segment, (ushort)0));
            idx = _allocBlocks.Count - 1;
        }

        // Calculate maximum available size for this block:
        // From segment to either the next allocated block or MEM_TOP_SEG
        ushort currentSize = _allocBlocks[idx].size;
        int blockEnd = segment + currentSize; // Current end of this block

        // Find the lowest block that starts above this segment
        int maxEnd = MEM_TOP_SEG;
        foreach (var (bSeg, bSize) in _allocBlocks)
        {
            if (bSeg > segment && bSeg < maxEnd)
                maxEnd = bSeg;
        }
        ushort maxAvail = (ushort)(maxEnd - segment);

        if (newSize <= maxAvail)
        {
            _allocBlocks[idx] = (segment, newSize);
            _cpu.Flags.CF = false;
            Console.Error.WriteLine($"[DOS] ResizeMem ES={segment:X4} BX={newSize:X4} → OK (max={maxAvail:X4})");
            RebuildMcbChain();
        }
        else
        {
            _cpu.AX = 0x08; // Insufficient memory
            _cpu.BX = maxAvail;
            _cpu.Flags.CF = true;
            Console.Error.WriteLine($"[DOS] ResizeMem ES={segment:X4} BX={newSize:X4} → FAIL (max={maxAvail:X4})");
        }
    }

    // Handle-based search state (AH=4E/4F)
    private List<byte[]>? _handleDirEntries;
    private int _handleDirIndex;
    private byte[] _handleSearchPattern83 = new byte[11];
    private ushort _handleSearchAttr;

    private void FindFirstFile()
    {
        // DS:DX = ASCIIZ filespec, CX = attributes
        int nameAddr = (_cpu.DS << 4) + _cpu.DX;
        var mem = _bus.GetMemoryDirect();
        string filespec = "";
        for (int i = 0; i < 128; i++)
        {
            byte ch = mem[(nameAddr + i) & 0xFFFFF];
            if (ch == 0) break;
            filespec += (char)ch;
        }
        _handleSearchAttr = _cpu.CX;
        Console.Error.WriteLine($"[DOS] FindFirst '{filespec}' attr={_cpu.CX:X4}");

        // Convert filespec to 8.3 pattern
        string fname = filespec;
        int lastSlash = Math.Max(fname.LastIndexOf('\\'), fname.LastIndexOf('/'));
        if (lastSlash >= 0) fname = fname.Substring(lastSlash + 1);

        // Build 8.3 pattern (wildcards: * expands to ?)
        for (int i = 0; i < 11; i++) _handleSearchPattern83[i] = (byte)' ';
        int dotPos = fname.IndexOf('.');
        string namePart = dotPos >= 0 ? fname.Substring(0, dotPos) : fname;
        string extPart = dotPos >= 0 ? fname.Substring(dotPos + 1) : "";

        // Expand * to ?s
        if (namePart == "*") namePart = "????????";
        if (extPart == "*") extPart = "???";

        for (int i = 0; i < 8 && i < namePart.Length; i++)
            _handleSearchPattern83[i] = (byte)char.ToUpper(namePart[i]);
        for (int i = 0; i < 3 && i < extPart.Length; i++)
            _handleSearchPattern83[8 + i] = (byte)char.ToUpper(extPart[i]);

        // Handle bare "*" without dot: match everything
        if (filespec.Trim() == "*" || filespec.EndsWith("\\*") || filespec.EndsWith("/*"))
        {
            for (int i = 0; i < 11; i++) _handleSearchPattern83[i] = (byte)'?';
        }

        // Determine the drive and directory to search
        byte searchDrive = _currentDrive;
        string searchPath = filespec;
        if (searchPath.Length >= 2 && searchPath[1] == ':')
        {
            searchDrive = (byte)(char.ToUpper(searchPath[0]) - 'A');
            searchPath = searchPath.Substring(2);
        }

        // Extract directory portion from the filespec
        int dirEnd = Math.Max(searchPath.LastIndexOf('\\'), searchPath.LastIndexOf('/'));
        string searchDir = dirEnd >= 0 ? searchPath.Substring(0, dirEnd) : "";

        // If no directory in filespec, use current directory for the drive
        if (searchDir.Length == 0)
            searchDir = _currentDirs[searchDrive] ?? "";

        // Load directory entries
        var fat16 = _fatReaders[searchDrive];
        if (fat16 != null && fat16.IsInitialized)
        {
            if (searchDir.Length > 0)
            {
                int dirCluster = fat16.ResolveDirPath(searchDir);
                if (dirCluster > 0)
                    _handleDirEntries = fat16.GetSubDirEntries((ushort)dirCluster);
                else if (dirCluster == 0)
                    _handleDirEntries = fat16.GetRootDirEntries();
                else
                    _handleDirEntries = new List<byte[]>(); // dir not found
            }
            else
            {
                _handleDirEntries = fat16.GetRootDirEntries();
            }
        }
        else
            _handleDirEntries = new List<byte[]>();

        _handleDirIndex = 0;

        // Save search state in DTA (bytes 0-20) for FindNext
        int dtaAddr = (_dtaSeg << 4) + _dtaOff;
        for (int i = 0; i < 11; i++)
            mem[(dtaAddr + i) & 0xFFFFF] = _handleSearchPattern83[i];

        FindNextFile();
    }

    private void FindNextFile()
    {
        var mem = _bus.GetMemoryDirect();
        while (_handleDirEntries != null && _handleDirIndex < _handleDirEntries.Count)
        {
            byte[] entry = _handleDirEntries[_handleDirIndex++];
            byte attr = entry[0x0B];

            // Skip volume labels unless specifically requested
            if ((attr & 0x08) != 0 && (_handleSearchAttr & 0x08) == 0) continue;
            // Skip directories unless requested
            if ((attr & 0x10) != 0 && (_handleSearchAttr & 0x10) == 0) continue;

            // Match pattern
            bool match = true;
            for (int i = 0; i < 11; i++)
            {
                if (_handleSearchPattern83[i] == (byte)'?') continue;
                byte e = entry[i];
                if (e >= (byte)'a' && e <= (byte)'z') e -= 0x20;
                if (_handleSearchPattern83[i] != e) { match = false; break; }
            }
            if (!match) continue;

            // Fill DTA with result
            int dtaAddr = (_dtaSeg << 4) + _dtaOff;
            // Byte 0x15: attribute
            mem[(dtaAddr + 0x15) & 0xFFFFF] = attr;
            // Bytes 0x16-0x17: time
            mem[(dtaAddr + 0x16) & 0xFFFFF] = entry[0x16];
            mem[(dtaAddr + 0x17) & 0xFFFFF] = entry[0x17];
            // Bytes 0x18-0x19: date
            mem[(dtaAddr + 0x18) & 0xFFFFF] = entry[0x18];
            mem[(dtaAddr + 0x19) & 0xFFFFF] = entry[0x19];
            // Bytes 0x1A-0x1D: file size
            mem[(dtaAddr + 0x1A) & 0xFFFFF] = entry[0x1C];
            mem[(dtaAddr + 0x1B) & 0xFFFFF] = entry[0x1D];
            mem[(dtaAddr + 0x1C) & 0xFFFFF] = entry[0x1E];
            mem[(dtaAddr + 0x1D) & 0xFFFFF] = entry[0x1F];
            // Bytes 0x1E+: ASCIIZ filename (8.3 with dot)
            int namePos = 0;
            for (int i = 0; i < 8; i++)
            {
                if (entry[i] != (byte)' ')
                    mem[(dtaAddr + 0x1E + namePos++) & 0xFFFFF] = entry[i];
            }
            if (entry[8] != (byte)' ')
            {
                mem[(dtaAddr + 0x1E + namePos++) & 0xFFFFF] = (byte)'.';
                for (int i = 8; i < 11; i++)
                {
                    if (entry[i] != (byte)' ')
                        mem[(dtaAddr + 0x1E + namePos++) & 0xFFFFF] = entry[i];
                }
            }
            mem[(dtaAddr + 0x1E + namePos) & 0xFFFFF] = 0; // NUL terminator

            uint size = (uint)(entry[0x1C] | (entry[0x1D] << 8) | (entry[0x1E] << 16) | (entry[0x1F] << 24));
            string dispName = "";
            for (int i = 0; i < namePos; i++) dispName += (char)mem[(dtaAddr + 0x1E + i) & 0xFFFFF];
            Console.Error.WriteLine($"[DOS] FindFile → '{dispName}' attr={attr:X2} size={size}");

            _cpu.Flags.CF = false;
            return;
        }

        _cpu.AX = 0x12; // No more files
        _lastError = 0x12;
        _cpu.Flags.CF = true;
    }

    // FCB directory search state
    private List<byte[]>? _fcbDirEntries;
    private int _fcbDirIndex;
    private byte[] _fcbSearchPattern = new byte[11]; // 8.3 pattern with '?' wildcards

    private byte _fcbSearchAttr; // attribute mask for FCB search

    private void FcbFindFirst()
    {
        // DS:DX = FCB (may be normal or extended)
        var mem = _bus.GetMemoryDirect();
        int fcbAddr = (_cpu.DS << 4) + _cpu.DX;

        // Check for extended FCB (first byte = 0xFF)
        int nameOffset;
        if (mem[fcbAddr & 0xFFFFF] == 0xFF)
        {
            // Extended FCB: +0=0xFF, +1..5=reserved, +6=attr, +7=drive, +8..18=filename
            _fcbSearchAttr = mem[(fcbAddr + 6) & 0xFFFFF];
            nameOffset = fcbAddr + 8;
        }
        else
        {
            // Normal FCB: +0=drive, +1..11=filename
            _fcbSearchAttr = 0;
            nameOffset = fcbAddr + 1;
        }

        // Read search pattern (11 bytes: 8 name + 3 ext)
        for (int i = 0; i < 11; i++)
            _fcbSearchPattern[i] = mem[(nameOffset + i) & 0xFFFFF];

        string pat = "";
        for (int i = 0; i < 11; i++) pat += (char)_fcbSearchPattern[i];
        // Dump raw FCB bytes for debugging
        string hexDump = "";
        for (int i = 0; i < 20; i++)
            hexDump += $"{mem[((_cpu.DS << 4) + _cpu.DX + i) & 0xFFFFF]:X2} ";
        Console.Error.WriteLine($"[DOS] FCB FindFirst pattern='{pat}' attr={_fcbSearchAttr:X2} rawFCB=[{hexDump.Trim()}]");

        // Load directory entries
        var fat16 = GetCurrentFat16();
        if (fat16 != null && fat16.IsInitialized)
            _fcbDirEntries = fat16.GetRootDirEntries();
        else
            _fcbDirEntries = new List<byte[]>();

        _fcbDirIndex = 0;
        FcbFindNextMatch();
    }

    private void FcbFindNext()
    {
        if (_fcbDirEntries == null)
        {
            _cpu.AL = 0xFF; // No more files
            return;
        }
        FcbFindNextMatch();
    }

    private void FcbFindNextMatch()
    {
        var mem = _bus.GetMemoryDirect();
        while (_fcbDirEntries != null && _fcbDirIndex < _fcbDirEntries.Count)
        {
            byte[] entry = _fcbDirEntries[_fcbDirIndex++];
            byte attr = entry[0x0B];

            // Match 8.3 pattern (? matches any char)
            bool match = true;
            for (int i = 0; i < 11; i++)
            {
                if (_fcbSearchPattern[i] == (byte)'?') continue;
                if (_fcbSearchPattern[i] != entry[i]) { match = false; break; }
            }
            if (!match) continue;

            // Write result to DTA as an FCB directory entry (33 bytes)
            int dtaAddr = (_dtaSeg << 4) + _dtaOff;
            // Byte 0: drive number (1=A)
            mem[(dtaAddr) & 0xFFFFF] = 0x01;
            // Bytes 1-11: filename (8.3)
            for (int i = 0; i < 11; i++)
                mem[(dtaAddr + 1 + i) & 0xFFFFF] = entry[i];
            // Byte 12: attribute
            mem[(dtaAddr + 12) & 0xFFFFF] = attr;
            // Bytes 13-21: reserved (zero)
            for (int i = 13; i < 22; i++)
                mem[(dtaAddr + i) & 0xFFFFF] = 0;
            // Bytes 22-23: time
            mem[(dtaAddr + 22) & 0xFFFFF] = entry[0x16];
            mem[(dtaAddr + 23) & 0xFFFFF] = entry[0x17];
            // Bytes 24-25: date
            mem[(dtaAddr + 24) & 0xFFFFF] = entry[0x18];
            mem[(dtaAddr + 25) & 0xFFFFF] = entry[0x19];
            // Bytes 26-27: start cluster
            mem[(dtaAddr + 26) & 0xFFFFF] = entry[0x1A];
            mem[(dtaAddr + 27) & 0xFFFFF] = entry[0x1B];
            // Bytes 28-31: file size
            mem[(dtaAddr + 28) & 0xFFFFF] = entry[0x1C];
            mem[(dtaAddr + 29) & 0xFFFFF] = entry[0x1D];
            mem[(dtaAddr + 30) & 0xFFFFF] = entry[0x1E];
            mem[(dtaAddr + 31) & 0xFFFFF] = entry[0x1F];

            string name = "";
            for (int i = 0; i < 11; i++) name += (char)entry[i];
            uint size = (uint)(entry[0x1C] | (entry[0x1D] << 8) | (entry[0x1E] << 16) | (entry[0x1F] << 24));
            Console.Error.WriteLine($"[DOS] FCB FindMatch → '{name.Trim()}' attr={attr:X2} size={size}");

            _cpu.AL = 0x00; // Found
            _lastError = 0; // Clear error
            return;
        }

        _cpu.AL = 0xFF; // No more files
        _lastError = 0x12; // Error 18: No more files
        Console.Error.WriteLine("[DOS] FCB FindMatch → no more files");
    }

    // File data cache for open file handles (loaded via FAT16)
    private readonly byte[]?[] _fileData = new byte[MAX_HANDLES][];

    /// <summary>
    /// Initialize the fake SFT (System File Table) in memory.
    /// The SFT is a linked list of tables. We create one table with MAX_HANDLES entries.
    /// </summary>
    private void InitializeSft()
    {
        var mem = _bus.GetMemoryDirect();
        int addr = SFT_BASE;

        // SFT header: next pointer (FFFF:FFFF = end of chain), entry count
        mem[addr] = 0xFF; mem[addr + 1] = 0xFF; // next offset = FFFF
        mem[addr + 2] = 0xFF; mem[addr + 3] = 0xFF; // next segment = FFFF
        mem[addr + 4] = (byte)(MAX_HANDLES & 0xFF);
        mem[addr + 5] = (byte)(MAX_HANDLES >> 8);

        // Initialize all entries as closed
        for (int i = 0; i < MAX_HANDLES; i++)
        {
            int entryAddr = SFT_BASE + SFT_HEADER_SIZE + i * SFT_ENTRY_SIZE;
            Array.Clear(mem, entryAddr, SFT_ENTRY_SIZE);
        }
    }

    /// <summary>
    /// Update the SFT entry for a given handle to match our internal state.
    /// Called after Open, Close, Seek, Read operations.
    /// </summary>
    private void UpdateSftEntry(int handle)
    {
        if (handle < 0 || handle >= MAX_HANDLES) return;
        var mem = _bus.GetMemoryDirect();
        int addr = SFT_BASE + SFT_HEADER_SIZE + handle * SFT_ENTRY_SIZE;

        if (_handleOpen[handle])
        {
            // Handle count (number of references)
            mem[addr + 0] = 0x01; mem[addr + 1] = 0x00;
            // Open mode
            mem[addr + 2] = 0x02; // Read/Write
            // Attribute
            mem[addr + 3] = 0x20; // Archive
            // Device info word (bit 6 = not EOF, bit 7 = 0 for file)
            mem[addr + 4] = 0x40; mem[addr + 5] = 0x00;
            // File size (32-bit LE)
            uint size = _fileSize[handle];
            mem[addr + 0x11] = (byte)(size & 0xFF);
            mem[addr + 0x12] = (byte)((size >> 8) & 0xFF);
            mem[addr + 0x13] = (byte)((size >> 16) & 0xFF);
            mem[addr + 0x14] = (byte)((size >> 24) & 0xFF);
            // File position (32-bit LE)
            uint pos = _filePosition[handle];
            mem[addr + 0x15] = (byte)(pos & 0xFF);
            mem[addr + 0x16] = (byte)((pos >> 8) & 0xFF);
            mem[addr + 0x17] = (byte)((pos >> 16) & 0xFF);
            mem[addr + 0x18] = (byte)((pos >> 24) & 0xFF);
        }
        else
        {
            // Closed - zero out
            mem[addr + 0] = 0x00; mem[addr + 1] = 0x00;
        }
    }

    /// <summary>
    /// Update the JFT (Job File Table) in the PSP for a given handle.
    /// Maps handle number to SFT index (or 0xFF for closed).
    /// </summary>
    private void UpdateJft(int handle, byte sftIndex)
    {
        if (_currentPSP == 0) return;
        var mem = _bus.GetMemoryDirect();
        int pspAddr = _currentPSP << 4;
        // Read JFT pointer from PSP
        ushort jftOff = (ushort)(mem[pspAddr + 0x34] | (mem[pspAddr + 0x35] << 8));
        ushort jftSeg = (ushort)(mem[pspAddr + 0x36] | (mem[pspAddr + 0x37] << 8));
        ushort maxHandles = (ushort)(mem[pspAddr + 0x32] | (mem[pspAddr + 0x33] << 8));
        if (handle < maxHandles)
        {
            int jftAddr = (jftSeg << 4) + jftOff + handle;
            mem[jftAddr] = sftIndex;
        }
    }

    /// <summary>
    /// Sync file position from SFT entry back to our internal state.
    /// Called before Read/Seek to pick up position changes made by overlay manager.
    /// </summary>
    private void SyncFromSft(int handle)
    {
        if (handle < 0 || handle >= MAX_HANDLES) return;
        var mem = _bus.GetMemoryDirect();
        int addr = SFT_BASE + SFT_HEADER_SIZE + handle * SFT_ENTRY_SIZE;

        // Read file position back from SFT
        uint pos = (uint)(mem[addr + 0x15] | (mem[addr + 0x16] << 8) |
                         (mem[addr + 0x17] << 16) | (mem[addr + 0x18] << 24));
        if (pos != _filePosition[handle] && _handleOpen[handle])
        {
            Console.Error.WriteLine($"[SFT] Handle {handle} pos synced: {_filePosition[handle]:X8} → {pos:X8}");
            _filePosition[handle] = pos;
        }
    }

    /// <summary>
    /// Get SFT entry address for INT 2F AX=122E.
    /// Returns the segment:offset of the SFT entry for the given handle index.
    /// </summary>
    public (ushort seg, ushort off) GetSftEntryAddress(int handleIndex)
    {
        int addr = SFT_BASE + SFT_HEADER_SIZE + handleIndex * SFT_ENTRY_SIZE;
        ushort seg = (ushort)(addr >> 4);
        ushort off = (ushort)(addr & 0x0F);
        return (seg, off);
    }

    /// <summary>
    /// Get SFT header address (for INT 21h AH=52 - Get List of Lists).
    /// </summary>
    public (ushort seg, ushort off) GetSftHeaderAddress()
    {
        ushort seg = (ushort)(SFT_BASE >> 4);
        ushort off = (ushort)(SFT_BASE & 0x0F);
        return (seg, off);
    }

    private void Exec()
    {
        // INT 21h AH=4Bh: EXEC
        byte subfunc = _cpu.AL;
        Console.Error.WriteLine($"[DOS] EXEC AH=4B AL={subfunc:X2} DS:DX={_cpu.DS:X4}:{_cpu.DX:X4} ES:BX={_cpu.ES:X4}:{_cpu.BX:X4}");

        // Read filename
        int nameAddr = (_cpu.DS << 4) + _cpu.DX;
        var mem = _bus.GetMemoryDirect();
        string filename = "";
        for (int i = 0; i < 128; i++)
        {
            byte ch = mem[(nameAddr + i) & 0xFFFFF];
            if (ch == 0) break;
            filename += (char)ch;
        }
        Console.Error.WriteLine($"[DOS] EXEC filename: {filename}");

        var _fat16 = GetCurrentFat16();
        if (_fat16 == null || !_fat16.IsInitialized)
        {
            Console.Error.WriteLine("[DOS] EXEC failed: no filesystem");
            _cpu.AX = 0x02;
            _cpu.Flags.CF = true;
            return;
        }

        // Resolve drive and current directory (same logic as OpenFile)
        byte fileDrive = _currentDrive;
        string lookupName = filename;
        if (filename.Length >= 2 && filename[1] == ':')
        {
            fileDrive = (byte)(char.ToUpper(filename[0]) - 'A');
            lookupName = filename.Substring(2);
        }
        if (!lookupName.Contains('\\') && !lookupName.Contains('/'))
        {
            string curDir = _currentDirs[fileDrive] ?? "";
            if (curDir.Length > 0)
                lookupName = curDir + "\\" + lookupName;
        }
        var fileFat = _fatReaders[fileDrive] ?? _fat16;
        Console.Error.WriteLine($"[DOS] EXEC resolved: '{lookupName}' on drive {fileDrive}");

        // Find file on disk
        var (cluster, fileSize) = fileFat.FindFile(lookupName);
        if (cluster == 0)
        {
            Console.Error.WriteLine($"[DOS] EXEC '{filename}' → '{lookupName}' not found");
            _cpu.AX = 0x02;
            _cpu.Flags.CF = true;
            return;
        }

        // Read file data
        byte[]? fileData = fileFat.ReadFile(cluster, fileSize);
        if (fileData == null)
        {
            Console.Error.WriteLine($"[DOS] EXEC '{filename}' read error");
            _cpu.AX = 0x02;
            _cpu.Flags.CF = true;
            return;
        }

        Console.Error.WriteLine($"[DOS] EXEC loaded {fileData.Length} bytes from disk");

        if (subfunc == 0x03)
        {
            // AL=03: Load Overlay - load EXE code at ES:BX-specified segment, no PSP
            LoadOverlay(fileData);
            return;
        }

        // Check for EXE or COM
        bool isExe = fileData.Length >= 2 && fileData[0] == 0x4D && fileData[1] == 0x5A;

        // Allocate memory for the program
        ushort loadSeg = (ushort)(GetNextFreeSeg() + 1); // +1 for MCB

        if (isExe)
            LoadExe(fileData, loadSeg, subfunc);
        else
            LoadCom(fileData, loadSeg, subfunc);
    }

    /// <summary>
    /// Load Overlay (AH=4B AL=03): Load EXE code at specified segment without creating PSP.
    /// Parameter block at ES:BX: [+0]=load segment, [+2]=relocation factor
    /// </summary>
    private void LoadOverlay(byte[] data)
    {
        var mem = _bus.GetMemoryDirect();
        int paramAddr = (_cpu.ES << 4) + _cpu.BX;
        ushort loadSeg = (ushort)(mem[paramAddr] | (mem[paramAddr + 1] << 8));
        ushort relocFactor = (ushort)(mem[paramAddr + 2] | (mem[paramAddr + 3] << 8));

        Console.Error.WriteLine($"[DOS] EXEC Overlay: loadSeg={loadSeg:X4} relocFactor={relocFactor:X4} size={data.Length}");

        bool isExe = data.Length >= 2 && data[0] == 0x4D && data[1] == 0x5A;
        if (isExe)
        {
            // Parse EXE header
            int headerSize = (data[8] | (data[9] << 8)) * 16;
            int relocCount = data[6] | (data[7] << 8);
            int relocOff = data[0x18] | (data[0x19] << 8);
            int codeLen = data.Length - headerSize;

            // Copy code to load segment
            int loadAddr = loadSeg << 4;
            if (loadAddr + codeLen <= mem.Length)
                Array.Copy(data, headerSize, mem, loadAddr, codeLen);

            // Apply relocations using relocFactor
            for (int r = 0; r < relocCount; r++)
            {
                int rOff = relocOff + r * 4;
                if (rOff + 3 >= data.Length) break;
                int rOffset = data[rOff] | (data[rOff + 1] << 8);
                int rSeg = data[rOff + 2] | (data[rOff + 3] << 8);
                int fixAddr = loadAddr + rSeg * 16 + rOffset;
                if (fixAddr + 1 < mem.Length)
                {
                    ushort val = (ushort)(mem[fixAddr] | (mem[fixAddr + 1] << 8));
                    val += relocFactor;
                    mem[fixAddr] = (byte)(val & 0xFF);
                    mem[fixAddr + 1] = (byte)(val >> 8);
                }
            }

            Console.Error.WriteLine($"[DOS] EXEC Overlay loaded: {codeLen} bytes at {loadSeg:X4}, {relocCount} relocs (factor={relocFactor:X4})");
        }
        else
        {
            // COM-style overlay: just copy data to load segment
            int loadAddr = loadSeg << 4;
            if (loadAddr + data.Length <= mem.Length)
                Array.Copy(data, 0, mem, loadAddr, data.Length);
            Console.Error.WriteLine($"[DOS] EXEC Overlay (COM) loaded: {data.Length} bytes at {loadSeg:X4}");
        }

        _cpu.Flags.CF = false;
    }

    private void LoadCom(byte[] data, ushort loadSeg, byte subfunc)
    {
        var mem = _bus.GetMemoryDirect();
        ushort pspSeg = loadSeg;
        ushort codeSeg = pspSeg; // COM: code starts at PSP:0100

        // Build PSP at pspSeg:0000
        BuildPSP(pspSeg, mem);

        // Load COM file at PSP:0100
        int loadAddr = (pspSeg << 4) + 0x100;
        int copyLen = Math.Min(data.Length, 0xFE00); // Max ~64KB - 256
        Array.Copy(data, 0, mem, loadAddr, copyLen);

        // Track allocation for the loaded program
        ushort progParas = (ushort)(((copyLen + 0x100 + 15) >> 4) + 0x10);
        _allocBlocks.Add((pspSeg, progParas));

        Console.Error.WriteLine($"[DOS] EXEC COM loaded at {pspSeg:X4}:0100 ({copyLen} bytes), size={progParas:X4} paras");

        if (subfunc == 0x00) // Load and Execute
        {
            _currentPSP = pspSeg;
            _cpu.CS = codeSeg;
            _cpu.IP = 0x0100;
            _cpu.DS = pspSeg;
            _cpu.ES = pspSeg;
            _cpu.SS = pspSeg;
            _cpu.SP = 0xFFFE;
            // Push 0x0000 as return address (INT 20h at PSP:0000)
            _cpu.SP -= 2;
            int sp = (_cpu.SS << 4) + _cpu.SP;
            mem[sp] = 0x00;
            mem[sp + 1] = 0x00;
            _cpu.Flags.CF = false;
            SkipIret = true; // Don't IRET — control transfers directly to child
            TraceAfterExec = true;
        }
        else
        {
            _cpu.Flags.CF = false;
        }
    }

    private void LoadExe(byte[] data, ushort loadSeg, byte subfunc)
    {
        var mem = _bus.GetMemoryDirect();

        // Parse EXE header
        int headerSize = (data[0x08] | (data[0x09] << 8)) * 16; // Header paragraphs * 16
        int relocCount = data[0x06] | (data[0x07] << 8);
        int relocOff = data[0x18] | (data[0x19] << 8);
        ushort initSS = (ushort)(data[0x0E] | (data[0x0F] << 8));
        ushort initSP = (ushort)(data[0x10] | (data[0x11] << 8));
        ushort initIP = (ushort)(data[0x14] | (data[0x15] << 8));
        ushort initCS = (ushort)(data[0x16] | (data[0x17] << 8));
        int minAlloc = data[0x0A] | (data[0x0B] << 8);
        int maxAlloc = data[0x0C] | (data[0x0D] << 8);

        ushort pspSeg = loadSeg;
        ushort startSeg = (ushort)(pspSeg + 0x10); // Code starts 256 bytes after PSP

        // Build PSP
        BuildPSP(pspSeg, mem);

        // Load EXE image (skip header) at startSeg:0000
        int imageSize = data.Length - headerSize;
        int loadAddr = startSeg << 4;
        if (loadAddr + imageSize <= mem.Length)
            Array.Copy(data, headerSize, mem, loadAddr, imageSize);

        Console.Error.WriteLine($"[DOS] EXEC EXE headerSize={headerSize} imageSize={imageSize} loadAt={startSeg:X4}:0000");
        Console.Error.WriteLine($"[DOS] EXEC EXE CS:IP={initCS:X4}:{initIP:X4} SS:SP={initSS:X4}:{initSP:X4} relocs={relocCount}");

        // Apply relocations
        for (int r = 0; r < relocCount; r++)
        {
            int rOff = relocOff + r * 4;
            if (rOff + 3 >= data.Length) break;
            int rOffset = data[rOff] | (data[rOff + 1] << 8);
            int rSegment = data[rOff + 2] | (data[rOff + 3] << 8);
            int fixupAddr = ((rSegment + startSeg) << 4) + rOffset;
            if (fixupAddr + 1 < mem.Length)
            {
                ushort val = (ushort)(mem[fixupAddr] | (mem[fixupAddr + 1] << 8));
                val += startSeg;
                mem[fixupAddr] = (byte)(val & 0xFF);
                mem[fixupAddr + 1] = (byte)(val >> 8);
            }
        }

        // Track allocation for the loaded program
        int totalParagraphs = ((imageSize + 15) >> 4) + 0x10 + minAlloc;
        _allocBlocks.Add((pspSeg, (ushort)totalParagraphs));

        Console.Error.WriteLine($"[DOS] EXEC EXE loaded, size={totalParagraphs:X4} paras");

        if (subfunc == 0x00) // Load and Execute
        {
            _currentPSP = pspSeg;
            _cpu.CS = (ushort)(initCS + startSeg);
            _cpu.IP = initIP;
            _cpu.DS = pspSeg;
            _cpu.ES = pspSeg;
            _cpu.SS = (ushort)(initSS + startSeg);
            _cpu.SP = initSP;
            _cpu.Flags.CF = false;
            SkipIret = true; // Don't IRET — control transfers directly to child
        }
        else
        {
            _cpu.Flags.CF = false;
        }
    }

    private void BuildPSP(ushort pspSeg, byte[] mem)
    {
        int pspAddr = pspSeg << 4;
        // Clear PSP
        for (int i = 0; i < 256; i++)
            mem[pspAddr + i] = 0;

        // INT 20h at offset 0
        mem[pspAddr + 0x00] = 0xCD;
        mem[pspAddr + 0x01] = 0x20;

        // Memory size (top of memory segment)
        mem[pspAddr + 0x02] = (byte)(MEM_TOP_SEG & 0xFF);
        mem[pspAddr + 0x03] = (byte)(MEM_TOP_SEG >> 8);

        // File handle table at offset 0x18 (20 handles, 0xFF = closed)
        mem[pspAddr + 0x32] = 20; // Handle table size
        mem[pspAddr + 0x34] = 0x18; // Handle table offset (low)
        mem[pspAddr + 0x36] = (byte)(pspSeg & 0xFF); // Handle table segment (low)
        mem[pspAddr + 0x37] = (byte)(pspSeg >> 8);
        for (int i = 0; i < 20; i++)
            mem[pspAddr + 0x18 + i] = (i < 5) ? (byte)i : (byte)0xFF;

        // Environment segment (allocated before PSP)
        ushort envSeg = (ushort)(pspSeg - 0x20); // 512 bytes for env
        mem[pspAddr + 0x2C] = (byte)(envSeg & 0xFF);
        mem[pspAddr + 0x2D] = (byte)(envSeg >> 8);
        // Write environment block
        int envAddr = envSeg << 4;
        int envOff = 0;
        // COMSPEC=A:\COMMAND.COM
        string comspec = "COMSPEC=A:\\COMMAND.COM";
        foreach (char c in comspec) mem[envAddr + envOff++] = (byte)c;
        mem[envAddr + envOff++] = 0; // NUL terminator
        // PATH=A:\
        string path = "PATH=A:\\";
        foreach (char c in path) mem[envAddr + envOff++] = (byte)c;
        mem[envAddr + envOff++] = 0;
        // End of environment
        mem[envAddr + envOff++] = 0;
        // Additional string count (DOS 3.0+)
        mem[envAddr + envOff++] = 0x01;
        mem[envAddr + envOff++] = 0x00;
        // Program path
        string progPath = "A:\\COMMAND.COM";
        foreach (char c in progPath) mem[envAddr + envOff++] = (byte)c;
        mem[envAddr + envOff++] = 0;

        // Default DTA at PSP:0080
        mem[pspAddr + 0x80] = 0; // Empty command tail
        mem[pspAddr + 0x81] = 0x0D;

        // Default FCBs
        mem[pspAddr + 0x5C] = 0; // FCB1
        mem[pspAddr + 0x6C] = 0; // FCB2
    }

    private void Terminate()
    {
        // Program termination - halt the CPU
        Console.Error.WriteLine($"[DOS] Program terminated (AH={_cpu.AH:X2} AL={_cpu.AL:X2})");
        _cpu.Halted = true;
    }
}
