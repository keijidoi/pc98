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

    // Simple file handle table (handles 0-19)
    private const int MAX_HANDLES = 20;
    private readonly bool[] _handleOpen = new bool[MAX_HANDLES];

    // File position and size tracking per handle
    private readonly uint[] _filePosition = new uint[MAX_HANDLES];
    private readonly uint[] _fileSize = new uint[MAX_HANDLES];

    // Memory management - block allocator with free list
    private const ushort MEM_TOP_SEG = 0x9FC0; // Just below video RAM (A000:0000)
    private readonly List<(ushort seg, ushort size)> _allocBlocks = new();
    private ushort _memBaseSeg = 0x3000; // Lowest allocation segment

    // DOS version to report (MS-DOS 6.20 matching the boot disk)
    private const byte DOS_MAJOR = 6;
    private const byte DOS_MINOR = 20;

    private Fat16Reader? _fat16;

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

    public void SetFat16Reader(Fat16Reader reader)
    {
        _fat16 = reader;
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

            case 0x4E: // Find First File
                FindFirstFile();
                break;

            case 0x4F: // Find Next File
                // No more files
                _cpu.AX = 0x12; // No more files
                _cpu.Flags.CF = true;
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

            case 0x38: // Get/Set Country Info
                if (_cpu.AL == 0x00 || _cpu.AL == 0x01)
                {
                    // Get country info → DS:DX buffer
                    int ciAddr = (_cpu.DS << 4) + _cpu.DX;
                    var ciMem = _bus.GetMemoryDirect();
                    // Clear 34 bytes
                    for (int i = 0; i < 34; i++) ciMem[(ciAddr + i) & 0xFFFFF] = 0;
                    // Date format: 2 = YY/MM/DD (Japan)
                    ciMem[ciAddr] = 0x02; ciMem[ciAddr + 1] = 0x00;
                    // Currency symbol
                    ciMem[ciAddr + 2] = 0x5C; // yen sign
                    ciMem[ciAddr + 7] = 0x2C; // Thousands separator ','
                    ciMem[ciAddr + 9] = 0x2E; // Decimal separator '.'
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

            case 0x65: // Get Extended Country Info
                // AL=04: Get filename uppercase table
                // Return minimal info
                _cpu.Flags.CF = true; // Not supported
                _cpu.AX = 0x01; // Invalid function
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

    /// <summary>
    /// Called by emulator when a key is pressed during WaitingForInput state.
    /// Returns true if input is complete (Enter pressed).
    /// </summary>
    public bool HandleKeyInput(byte ascii, byte scancode)
    {
        if (!WaitingForInput) return false;
        var mem = _bus.GetMemoryDirect();

        int curLen = mem[(_inputBufPhys + 1) & 0xFFFFF];

        if (ascii == 0x0D) // Enter
        {
            mem[(_inputBufPhys + 2 + curLen) & 0xFFFFF] = 0x0D;
            DisplayChar(0x0D);
            DisplayChar(0x0A);
            WaitingForInput = false;
            // Restore CPU to the caller of INT 21h
            _cpu.IP = _savedRetIP;
            _cpu.CS = _savedRetCS;
            _cpu.Flags.Value = _savedRetFlags;
            _cpu.Halted = false;
            // Dump input buffer content
            var bufSb = new System.Text.StringBuilder();
            bufSb.Append($"[DOS] Input complete: {curLen} chars: ");
            for (int bi = 0; bi < curLen + 2; bi++)
                bufSb.Append($"{mem[(_inputBufPhys + bi) & 0xFFFFF]:X2} ");
            bufSb.Append(" → '");
            for (int bi = 0; bi < curLen; bi++)
                bufSb.Append((char)mem[(_inputBufPhys + 2 + bi) & 0xFFFFF]);
            bufSb.Append($"' resume at {_savedRetCS:X4}:{_savedRetIP:X4}");
            Console.Error.WriteLine(bufSb.ToString());
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
            }
            return false;
        }

        if (ascii >= 0x20 && curLen < _inputMaxLen - 1)
        {
            mem[(_inputBufPhys + 2 + curLen) & 0xFFFFF] = ascii;
            curLen++;
            mem[(_inputBufPhys + 1) & 0xFFFFF] = (byte)curLen;
            DisplayChar(ascii);
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
            ushort segment = nextFree;
            _allocBlocks.Add((segment, paragraphs));
            _cpu.AX = segment;
            _cpu.Flags.CF = false;
            Console.Error.WriteLine($"[DOS] AllocMem → segment {segment:X4}");
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
        }
        else
        {
            Console.Error.WriteLine($"[DOS] FreeMem ES={segment:X4} (not tracked)");
        }
        _cpu.Flags.CF = false;
    }

    private void ResizeMemory()
    {
        ushort segment = _cpu.ES;
        ushort newSize = _cpu.BX;
        Console.Error.WriteLine($"[DOS] ResizeMem ES={segment:X4} BX={newSize:X4}");

        int idx = _allocBlocks.FindIndex(b => b.seg == segment);
        if (idx >= 0)
        {
            _allocBlocks[idx] = (segment, newSize);
        }
        else
        {
            // Not tracked — add it as a new block
            _allocBlocks.Add((segment, newSize));
        }
        _cpu.Flags.CF = false;
    }

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
        Console.Error.WriteLine($"[DOS] FindFirst '{filespec}' attr={_cpu.CX:X4}");

        // Check if the file exists on disk
        if (_fat16 != null && _fat16.IsInitialized)
        {
            var (cluster, size) = _fat16.FindFile(filespec);
            if (cluster != 0)
            {
                // Found — fill DTA with file info
                int dtaAddr = (_dtaSeg << 4) + _dtaOff;
                // Clear DTA (43 bytes)
                for (int i = 0; i < 43; i++) mem[(dtaAddr + i) & 0xFFFFF] = 0;
                // Attribute at offset 0x15
                mem[(dtaAddr + 0x15) & 0xFFFFF] = 0x20; // Archive
                // File size at offset 0x1A (4 bytes)
                mem[(dtaAddr + 0x1A) & 0xFFFFF] = (byte)(size & 0xFF);
                mem[(dtaAddr + 0x1B) & 0xFFFFF] = (byte)((size >> 8) & 0xFF);
                mem[(dtaAddr + 0x1C) & 0xFFFFF] = (byte)((size >> 16) & 0xFF);
                mem[(dtaAddr + 0x1D) & 0xFFFFF] = (byte)((size >> 24) & 0xFF);
                // Filename at offset 0x1E (13 bytes ASCIIZ)
                string name = System.IO.Path.GetFileName(filespec).ToUpperInvariant();
                for (int i = 0; i < name.Length && i < 12; i++)
                    mem[(dtaAddr + 0x1E + i) & 0xFFFFF] = (byte)name[i];
                mem[(dtaAddr + 0x1E + Math.Min(name.Length, 12)) & 0xFFFFF] = 0;

                _cpu.Flags.CF = false;
                Console.Error.WriteLine($"[DOS] FindFirst → found, size={size}");
                return;
            }
        }

        // Not found
        _cpu.AX = 0x02; // File not found
        _cpu.Flags.CF = true;
        Console.Error.WriteLine($"[DOS] FindFirst → not found");
    }

    // File data cache for open file handles (loaded via FAT16)
    private readonly byte[]?[] _fileData = new byte[MAX_HANDLES][];

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

        if (_fat16 == null || !_fat16.IsInitialized)
        {
            Console.Error.WriteLine("[DOS] EXEC failed: no filesystem");
            _cpu.AX = 0x02;
            _cpu.Flags.CF = true;
            return;
        }

        // Find file on disk
        var (cluster, fileSize) = _fat16.FindFile(filename);
        if (cluster == 0)
        {
            Console.Error.WriteLine($"[DOS] EXEC '{filename}' not found");
            _fat16.ListRootDir();
            _cpu.AX = 0x02;
            _cpu.Flags.CF = true;
            return;
        }

        // Read file data
        byte[]? fileData = _fat16.ReadFile(cluster, fileSize);
        if (fileData == null)
        {
            Console.Error.WriteLine($"[DOS] EXEC '{filename}' read error");
            _cpu.AX = 0x02;
            _cpu.Flags.CF = true;
            return;
        }

        Console.Error.WriteLine($"[DOS] EXEC loaded {fileData.Length} bytes from disk");

        // Check for EXE or COM
        bool isExe = fileData.Length >= 2 && fileData[0] == 0x4D && fileData[1] == 0x5A;

        // Allocate memory for the program
        ushort loadSeg = GetNextFreeSeg();

        if (isExe)
            LoadExe(fileData, loadSeg, subfunc);
        else
            LoadCom(fileData, loadSeg, subfunc);
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
