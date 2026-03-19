using PC98Emu.CPU;
using PC98Emu.Bus;

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
    private ushort _currentPSP;
    private ushort _dtaSeg;
    private ushort _dtaOff = 0x0080; // Default DTA at PSP:0080
    private byte _currentDrive; // 0=A, 1=B, 2=C...
    private byte _verifyFlag;

    // Simple file handle table (handles 0-19)
    private const int MAX_HANDLES = 20;
    private readonly bool[] _handleOpen = new bool[MAX_HANDLES];

    // File position and size tracking per handle
    private readonly uint[] _filePosition = new uint[MAX_HANDLES];
    private readonly uint[] _fileSize = new uint[MAX_HANDLES];

    // Memory management - simple bump allocator
    // Free memory starts above all loaded code and grows upward
    private ushort _nextFreeSeg = 0x3000; // Default: above typical kernel code
    private const ushort MEM_TOP_SEG = 0x9FC0; // Just below video RAM (A000:0000)

    // DOS version to report (MS-DOS 3.30 for PC-98 compatibility)
    private const byte DOS_MAJOR = 3;
    private const byte DOS_MINOR = 30;

    public DosBios(V30 cpu, SystemBus bus)
    {
        _cpu = cpu;
        _bus = bus;

        // Pre-open standard handles
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
        // The kernel code extends well above the PSP segment
        ushort memBase = (ushort)(segment + 0x1000); // Give 64KB for the program
        if (memBase > _nextFreeSeg)
            _nextFreeSeg = memBase;
        Console.Error.WriteLine($"[DOS] SetPSP segment={segment:X4}, nextFreeSeg={_nextFreeSeg:X4}");
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

            case 0x2A: // Get Date
                GetDate();
                break;

            case 0x2C: // Get Time
                GetTime();
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
                break;

            case 0x35: // Get Interrupt Vector
                GetInterruptVector();
                break;

            case 0x3B: // Change Directory
                // Stub: just succeed
                _cpu.Flags.CF = false;
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

            case 0x42: // Seek (LSEEK)
                Seek();
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
                // Return pointer to a minimal structure
                // Games check this for drive table pointer
                _cpu.ES = 0x0060;
                _cpu.BX = 0x0026; // Fake SYSVARS pointer
                break;

            case 0x56: // Rename File
                _cpu.Flags.CF = false; // Stub: succeed
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

    private void DisplayChar(byte ch)
    {
        // Write character to text VRAM (simplified)
        Console.Error.Write((char)ch);
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
        for (int i = 0; i < 4096; i++)
        {
            byte ch = mem[(addr + i) & 0xFFFFF];
            if (ch == (byte)'$') break;
            Console.Error.Write((char)ch);
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

    private void GetInterruptVector()
    {
        byte vector = _cpu.AL;
        int ivtAddr = vector * 4;
        _cpu.BX = _bus.ReadMemoryWord(ivtAddr);
        _cpu.ES = _bus.ReadMemoryWord(ivtAddr + 2);
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
                _fileSize[i] = 0; // Unknown size (LSEEK will use default)
                _cpu.AX = (ushort)i;
                _cpu.Flags.CF = false;
                Console.Error.WriteLine($"[DOS] OpenFile '{filename}' → handle {i}");
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
            _cpu.Flags.CF = false;
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
        // Stub: return 0 bytes read (EOF)
        _cpu.AX = 0x0000;
        _cpu.Flags.CF = false;
    }

    private void WriteFile()
    {
        // CX = bytes to write, BX = handle, DS:DX = buffer
        ushort handle = _cpu.BX;
        ushort count = _cpu.CX;

        if (handle == 1 || handle == 2)
        {
            // STDOUT/STDERR: output to console
            int addr = (_cpu.DS << 4) + _cpu.DX;
            var mem = _bus.GetMemoryDirect();
            for (int i = 0; i < count; i++)
            {
                byte ch = mem[(addr + i) & 0xFFFFF];
                Console.Error.Write((char)ch);
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
            _filePosition[handle] = newPos;

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

    private void GetCurrentDirectory()
    {
        // DS:SI = buffer for path (64 bytes, no drive letter, no leading backslash)
        int addr = (_cpu.DS << 4) + _cpu.SI;
        var mem = _bus.GetMemoryDirect();
        // Return root directory (empty string)
        mem[addr & 0xFFFFF] = 0x00;
        _cpu.Flags.CF = false;
    }

    private void AllocateMemory()
    {
        // BX = paragraphs requested
        ushort paragraphs = _cpu.BX;
        ushort available = (ushort)(MEM_TOP_SEG - _nextFreeSeg);

        Console.Error.WriteLine($"[DOS] AllocMem BX={paragraphs:X4} ({paragraphs * 16} bytes) nextFree={_nextFreeSeg:X4} avail={available:X4}");

        if (paragraphs <= available)
        {
            ushort segment = _nextFreeSeg;
            _nextFreeSeg += paragraphs;
            _cpu.AX = segment;
            _cpu.Flags.CF = false;
            Console.Error.WriteLine($"[DOS] AllocMem → segment {segment:X4}, nextFree now {_nextFreeSeg:X4}");
        }
        else
        {
            // Not enough contiguous memory
            _cpu.AX = 0x08; // Insufficient memory
            _cpu.BX = available; // Maximum available paragraphs
            _cpu.Flags.CF = true;
            Console.Error.WriteLine($"[DOS] AllocMem FAILED, max avail={available:X4} paragraphs");
        }
    }

    private void FreeMemory()
    {
        // ES = segment to free
        Console.Error.WriteLine($"[DOS] FreeMem ES={_cpu.ES:X4}");
        _cpu.Flags.CF = false; // Always succeed
    }

    private void ResizeMemory()
    {
        // ES = segment of block, BX = new size in paragraphs
        ushort segment = _cpu.ES;
        ushort newSize = _cpu.BX;
        ushort newEnd = (ushort)(segment + newSize);

        Console.Error.WriteLine($"[DOS] ResizeMem ES={segment:X4} BX={newSize:X4} newEnd={newEnd:X4} nextFree={_nextFreeSeg:X4}");

        // Update free memory pointer: free memory starts after this block
        if (newEnd > _nextFreeSeg || segment < _nextFreeSeg)
        {
            _nextFreeSeg = newEnd;
            Console.Error.WriteLine($"[DOS] ResizeMem → nextFree updated to {_nextFreeSeg:X4}");
        }

        _cpu.Flags.CF = false;
    }

    private void Exec()
    {
        // INT 21h AH=4Bh: EXEC
        // AL = subfunction (0=load+exec, 1=load only, 3=overlay)
        // DS:DX = filename, ES:BX = parameter block
        Console.Error.WriteLine($"[DOS] EXEC AH=4B AL={_cpu.AL:X2} DS:DX={_cpu.DS:X4}:{_cpu.DX:X4} ES:BX={_cpu.ES:X4}:{_cpu.BX:X4}");

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

        // Stub: fail with "file not found"
        _cpu.AX = 0x02;
        _cpu.Flags.CF = true;
    }

    private void Terminate()
    {
        // Program termination - halt the CPU
        Console.Error.WriteLine($"[DOS] Program terminated (AH={_cpu.AH:X2} AL={_cpu.AL:X2})");
        _cpu.Halted = true;
    }
}
