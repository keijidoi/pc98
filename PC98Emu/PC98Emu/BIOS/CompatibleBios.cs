using PC98Emu.CPU;
using PC98Emu.Bus;
using PC98Emu.Devices;
using PC98Emu.Disk;
using PC98Emu.Graphics;

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
    private DosBios? _dosBios;

    // BIOS handler addresses in ROM area
    // Correct PC-98 interrupt assignments:
    // INT 18h = CRT BIOS (text display)
    // INT 19h = Keyboard BIOS
    // INT 1Ah = Timer BIOS (calendar/clock)
    // INT 1Bh = Disk BIOS
    // INT 1Ch = Serial (RS-232C)
    // INT 1Dh = Printer / Graphics
    private const int INT18_ADDR = 0xE8000; // CRT
    private const int INT19_ADDR = 0xE8010; // Keyboard
    private const int INT1A_ADDR = 0xE8020; // Timer
    private const int INT1B_ADDR = 0xE8030; // Disk
    private const int INT1C_ADDR = 0xE8040; // Serial
    private const int INT1D_ADDR = 0xE8050; // Graphics/Printer

    // Hardware interrupt handler stubs in ROM
    // Master PIC: IRQ0-7 → INT 08h-0Fh
    // Slave PIC:  IRQ0-7 → INT 10h-17h
    private const int IRQ_STUB_BASE = 0xE8060; // 24 stubs × 16 bytes each (exceptions + IRQs)

    // DOS INT 21h handler address in ROM area
    // 0xE8200 is taken by BootLoader.BOOT_RETURN_STUB, so use 0xE8210
    private const int INT21_ADDR = 0xE8210; // DOS functions

    // BDA addresses
    private const int BDA_MEMORY_SIZE = 0x0458;   // Main memory size in KB (word)
    private const int BDA_BOOT_DEVICE = 0x045B;    // Boot device
    private const int BDA_CPU_TYPE = 0x0480;       // CPU type: 0x03=386+
    private const int BDA_GDC_CLOCK = 0x0486;      // GDC clock mode
    private const int BDA_BIOS_FLAG0 = 0x0500;     // BIOS flags byte 0
    private const int BDA_BIOS_FLAG1 = 0x0501;     // BIOS flags byte 1
    private const int BDA_KB_BUF_HEAD = 0x0524;    // Keyboard buffer head pointer
    private const int BDA_KB_BUF_TAIL = 0x0526;    // Keyboard buffer tail pointer
    private const int BDA_KB_BUF_START = 0x0502;   // Keyboard buffer start
    private const int BDA_CRT_STATUS = 0x053C;     // CRT status: bit7=high-res
    private const int BDA_CRT_MODE = 0x053D;       // CRT mode flags
    private const int BDA_BOOT_DAUA = 0x0584;      // Boot disk DA/UA

    // PC-9821 identification area in BIOS ROM
    private const int PC9821_ID_BASE = 0xF8E80;
    // HDD equipment flags
    private const int SASI_HDD_EQUIP = 0xF8E90;

    // Text VRAM
    private const int TEXT_VRAM_BASE = 0xA0000;
    private const int ATTR_VRAM_BASE = 0xA2000;

    public CompatibleBios(V30 cpu, SystemBus bus)
    {
        _cpu = cpu;
        _bus = bus;
    }

    private DiskManager? _diskManager;
    private GDC? _textGdc;

    public void SetTextGdc(GDC textGdc)
    {
        _textGdc = textGdc;
    }

    public DiskBios? DiskBios => _diskBios;

    public void SetDiskManager(DiskManager diskManager)
    {
        _diskManager = diskManager;
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
        _crtBios ??= new CrtBios(_cpu, _bus, _textGdc);
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

        // Setup CPU exception stubs (INT 00h-07h)
        // These handle division by zero, NMI, breakpoint, etc.
        for (int i = 0; i < 8; i++)
        {
            int stubAddr = IRQ_STUB_BASE + i * 16;
            WriteBiosEntry(stubAddr);
            WriteIVTEntry(i, stubAddr);
            _cpu.RegisterBiosHandler(stubAddr, MakeExceptionHandler(i));
        }

        // Setup hardware interrupt stubs (IRQ0-15 → INT 08h-17h)
        // Each stub is just an IRET at a unique ROM address
        for (int i = 0; i < 16; i++)
        {
            int stubAddr = IRQ_STUB_BASE + (i + 8) * 16;
            WriteBiosEntry(stubAddr);
            WriteIVTEntry(0x08 + i, stubAddr);
            _cpu.RegisterBiosHandler(stubAddr, MakeIrqHandler(0x08 + i));
        }

        // Setup BIOS IVT entries
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

        // Register BIOS chain handlers for DOS kernel chaining pattern.
        // MS-DOS hooks BIOS interrupts and chains to the original handler
        // at a specific offset (+0x19) within the BIOS routine.
        // The DOS chain pattern: STI, PUSH DS, PUSH DX, JMP FAR [old_handler + 0x19]
        // The chain handler must: process the function, POP DX, POP DS, IRET.
        RegisterBiosChainHandler(INT18_ADDR, 0x19, () => _crtBios!.Handle());
        RegisterBiosChainHandler(INT1A_ADDR, 0x19, () => _timerBios!.Handle());
        RegisterBiosChainHandler(INT1B_ADDR, 0x19, () => _diskBios!.Handle());

        // Initialize BDA (BIOS Data Area)
        _bus.WriteMemoryWord(BDA_MEMORY_SIZE, 640);  // 640KB conventional memory
        _bus.WriteMemoryByte(BDA_BOOT_DEVICE, 0);
        _bus.WriteMemoryByte(BDA_CPU_TYPE, 0x03);    // 386+ CPU
        _bus.WriteMemoryByte(BDA_GDC_CLOCK, 0x01);   // 2.5MHz GDC clock (standard)

        // BIOS flags: bit0=not original PC-9801, bit1=high density FDD available
        _bus.WriteMemoryByte(BDA_BIOS_FLAG0, 0x03);
        // BDA_BIOS_FLAG1: bit3 is checked by MS-DOS kernel to decide text VRAM segment
        // If bit3=1, kernel uses 0xE000 (GVRAM) instead of 0xA000 (text VRAM) — must be 0
        _bus.WriteMemoryByte(BDA_BIOS_FLAG1, 0x00);

        // CRT status: bit7=high resolution mode
        _bus.WriteMemoryByte(BDA_CRT_STATUS, 0x80);
        _bus.WriteMemoryByte(BDA_CRT_MODE, 0x08);    // 80-column mode

        // Keyboard buffer pointers (empty buffer)
        _bus.WriteMemoryWord(BDA_KB_BUF_HEAD, BDA_KB_BUF_START);
        _bus.WriteMemoryWord(BDA_KB_BUF_TAIL, BDA_KB_BUF_START);

        // HDD equipment flags in BDA
        // 0x055D: HDD presence bitmap.
        // Low nibble (bits 0-3) = SASI HDD units 0-3 present.
        // High nibble (bits 4-7) = IDE/SCSI HDD units 0-3 present.
        // Only set the SASI low nibble - setting the high nibble causes IO.SYS
        // to detect additional IDE/SCSI physical drives, inflating unit count.
        if (_diskManager != null)
        {
            byte hddBits = 0;
            for (int i = 0; i < 4; i++)
            {
                if (_diskManager.GetHDD(i) != null)
                    hddBits |= (byte)(1 << i); // SASI bit only
            }
            _bus.WriteMemoryByte(0x055D, hddBits);

            // 0x0402: disk equipment/device type flags
            // Bits 0-4 checked by MS-DOS boot code (TEST [0402], 1Fh)
            // Bit 0 = SASI HDD present
            if (hddBits != 0)
                _bus.WriteMemoryByte(0x0402, 0x01);

            // 0x04BC: HDD BIOS work flag (non-zero = HDDs present).
            // IO.SYS detection function at 4F59 skips entirely when this is 0.
            _bus.WriteMemoryByte(0x04BC, hddBits);

            // 0x0460: SASI/SCSI HDD work area (4 bytes per slot, 8 slots).
            // The detection function's second loop at 4FCC checks BDA[0460+slot*4].
            // If non-zero, it probes the slot with INT 1B AH=14h DA=0xA0|unit.
            // NOTE: Do NOT set BDA[0482] — that marks slots as "pre-configured by
            // BIOS POST", causing the second loop to SKIP them entirely.
            for (int i = 0; i < 4; i++)
            {
                if (_diskManager.GetHDD(i) != null)
                    _bus.WriteMemoryByte(0x0460 + i * 4, 0x80); // SASI type marker
            }
        }

        // Start text GDC - on a real PC-98, the BIOS POST initializes and starts the GDC.
        // The kernel expects the GDC to be running when it checks display status.
        if (_textGdc != null)
        {
            _textGdc.Start();
            // Set pitch to 80 characters (40 words per line for 80-column mode)
            _textGdc.WriteCommand(0x47); // PITCH command
            _textGdc.WriteParameter(80); // 80 chars per line
        }

        // Initialize text VRAM to blank (not visible)
        // Games/OS will set attributes when writing text
        // Attribute format: bit7=visible, bit6=vline, bit5=uline, bit4=reverse,
        //                   bit3=blink, bits0-2=color(GRB: 7=white)
        for (int i = 0; i < 0x2000; i++)
        {
            _bus.WriteMemoryByte(ATTR_VRAM_BASE + i, 0x00); // not visible
            _bus.WriteMemoryByte(TEXT_VRAM_BASE + i, 0x00);  // null chars
        }

        // Write PC-9821 identification bytes at 0xF8E80
        // MS-DOS boot code checks these to determine machine type
        _bus.WriteBiosDirectly(PC9821_ID_BASE + 0, 0x98);  // PC-98 signature byte 1
        _bus.WriteBiosDirectly(PC9821_ID_BASE + 1, 0x21);  // PC-9821 signature byte 2
        _bus.WriteBiosDirectly(PC9821_ID_BASE + 2, 0x1F);  // Feature flags (386+, EGC, GRCG, etc.)
        _bus.WriteBiosDirectly(PC9821_ID_BASE + 3, 0x20);  // Feature flags 2
        _bus.WriteBiosDirectly(PC9821_ID_BASE + 4, 0x00);  // Reserved
        _bus.WriteBiosDirectly(PC9821_ID_BASE + 5, 0x40);  // Feature flags 3: bit6=HDD boot capable

        // SASI HDD equipment flags at 0xF8E90
        // Bit N = HDD unit N present (up to 4 units)
        if (_diskManager != null)
        {
            byte hddEquip = 0;
            for (int i = 0; i < 4; i++)
            {
                if (_diskManager.GetHDD(i) != null)
                    hddEquip |= (byte)(1 << i);
            }
            _bus.WriteBiosDirectly(SASI_HDD_EQUIP, hddEquip);
        }

        // NEC copyright string at 0xE8DD8 (some code checks for this)
        byte[] necStr = System.Text.Encoding.ASCII.GetBytes("NEC");
        for (int i = 0; i < necStr.Length; i++)
            _bus.WriteBiosDirectly(0xE8DD8 + i, necStr[i]);

        // BIOS date string at 0xFFFE5 (some code checks for this)
        byte[] dateStr = System.Text.Encoding.ASCII.GetBytes("01/01/99");
        for (int i = 0; i < dateStr.Length; i++)
            _bus.WriteBiosDirectly(0xFFFE5 + i, dateStr[i]);

        // Reset vector at 0xFFFF0: JMP FAR to BIOS entry
        _bus.WriteBiosDirectly(0xFFFF0, 0xEA); // JMP FAR opcode
        _bus.WriteBiosDirectly(0xFFFF1, 0x00);
        _bus.WriteBiosDirectly(0xFFFF2, 0x00);
        _bus.WriteBiosDirectly(0xFFFF3, 0x00);
        _bus.WriteBiosDirectly(0xFFFF4, 0xE8); // segment 0xE800

        // DOS INT 21h handler - registered in ROM but IVT not set yet.
        // The kernel's IO.SYS sets IVT[21h] to its own stub during boot.
        // We activate our handler later (ActivateInt21) after kernel init,
        // so we don't interfere with the kernel's internal startup.
        _dosBios = new DosBios(_cpu, _bus);
        WriteBiosEntry(INT21_ADDR);
        _cpu.RegisterBiosHandler(INT21_ADDR, HandleInt21);

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
        // Preserve CF set by BIOS handler (modify stacked flags before returning)
        bool cf = _cpu.Flags.CF;
        _cpu.IP = _cpu.Pop();
        _cpu.CS = _cpu.Pop();
        ushort flags = _cpu.Pop();
        _cpu.Flags.Value = flags;
        // Restore CF from BIOS handler result
        _cpu.Flags.CF = cf;
    }

    private Action MakeExceptionHandler(int intNum)
    {
        return () =>
        {
            // Just IRET for CPU exceptions (div0, NMI, etc.)
            DoIret();
        };
    }

    private Action MakeIrqHandler(int intNum)
    {
        return () =>
        {
            // IRQ0 (INT 08h): timer tick - update BDA timer counter and flags
            if (intNum == 0x08)
            {
                // Increment 32-bit timer counter at BDA[046C-046F]
                uint count = (uint)(_bus.ReadMemoryWord(0x046C) | (_bus.ReadMemoryWord(0x046E) << 16));
                count++;
                _bus.WriteMemoryWord(0x046C, (ushort)count);
                _bus.WriteMemoryWord(0x046E, (ushort)(count >> 16));
                // Set BDA[055F] bit 0 (timer tick flag, used by calendar read code)
                byte flags = _bus.ReadMemoryByte(0x055F);
                _bus.WriteMemoryByte(0x055F, (byte)(flags | 0x01));
            }

            // Send EOI to PIC
            if (intNum >= 0x10)
            {
                _bus.WriteIoByte(0x08, 0x20); // slave EOI
                _bus.WriteIoByte(0x00, 0x20); // master EOI (cascade)
            }
            else
            {
                _bus.WriteIoByte(0x00, 0x20); // master EOI
            }
            DoIret();
        };
    }

    /// <summary>
    /// Register a BIOS chain handler at base + offset for DOS kernel chaining.
    /// DOS kernel chains: STI, PUSH DS, PUSH DX, JMP FAR [old_handler + offset].
    /// Handler processes the BIOS function, then cleans up the extra stack items.
    /// </summary>
    private void RegisterBiosChainHandler(int baseAddr, int offset, Action handler)
    {
        int chainAddr = baseAddr + offset;
        _bus.WriteBiosDirectly(chainAddr, 0xCF); // IRET placeholder
        _cpu.RegisterBiosHandler(chainAddr, () =>
        {
            handler();
            // Clean up DOS chain pattern stack (PUSH DS; PUSH DX before JMP FAR)
            _cpu.DX = _cpu.Pop(); // Restore DX
            _cpu.DS = _cpu.Pop(); // Restore DS
            DoIret();             // Pop IP/CS/FLAGS, return to original INT caller
        });
    }

    private void HandleInt18()
    {
        _crtBios?.Handle();
        DoIret();
    }

    private void HandleInt19()
    {
        _keyboardBios?.Handle();
        DoIret();
    }

    private void HandleInt1A()
    {
        _timerBios?.Handle();
        DoIret();
    }

    private void HandleInt1B()
    {
        _diskBios?.Handle();
        DoIret();
    }

    private void HandleInt1C()
    {
        _serialBios?.Handle();
        DoIret();
    }

    private void HandleInt1D()
    {
        _graphicsBios?.Handle();
        DoIret();
    }

    /// <summary>
    /// Activate the INT 21h DOS handler by writing IVT[21h] to point to our BIOS ROM handler.
    /// Called after the kernel has finished its own initialization (which sets IVT[21h] to a stub).
    /// The kernel never installs a real INT 21h handler, so we provide one.
    /// </summary>
    // Physical address of DOS stub at 0060:3673
    public const int DOS_STUB_PHYS = 0x3C73;

    public void ActivateInt21()
    {
        // Do NOT override IVT[21h] — the kernel has installed its own INT 21h handler
        // that routes through device drivers for disk I/O. Overwriting it would break
        // the kernel's file system access (FAT reads, COMMAND.COM loading, etc.).

        // Register a catch-all handler at the kernel's DOS stub address (0060:3673).
        // This intercepts INT 21h/2Fh/etc. during DOSINIT's first call when the
        // kernel's own dispatch pointers aren't set up yet.
        _cpu.RegisterBiosHandler(DOS_STUB_PHYS, HandleDosStub);

        // Also write our handler as the dispatch target in IO.SYS's dispatch table.
        // The stub at 0060:3673 does JMP FAR [CS:0204]; stub at 0060:3680 does JMP FAR [CS:0208].
        // Before DOSINIT sets up the kernel's handlers, these must point somewhere valid.
        var mem = _bus.GetMemoryDirect();
        ushort handlerOff = (ushort)(INT21_ADDR & 0xF);
        ushort handlerSeg = (ushort)(INT21_ADDR >> 4);
        // [0060:0204] = far ptr to our INT 21h handler
        int disp1 = 0x600 + 0x0204;
        mem[disp1 + 0] = (byte)(handlerOff & 0xFF);
        mem[disp1 + 1] = (byte)(handlerOff >> 8);
        mem[disp1 + 2] = (byte)(handlerSeg & 0xFF);
        mem[disp1 + 3] = (byte)(handlerSeg >> 8);
        // [0060:0208] = same
        int disp2 = 0x600 + 0x0208;
        mem[disp2 + 0] = (byte)(handlerOff & 0xFF);
        mem[disp2 + 1] = (byte)(handlerOff >> 8);
        mem[disp2 + 2] = (byte)(handlerSeg & 0xFF);
        mem[disp2 + 3] = (byte)(handlerSeg >> 8);

        Console.Error.WriteLine($"[BIOS] DOS stub handler at 0x{DOS_STUB_PHYS:X5}, dispatch [0204]/[0208]={handlerSeg:X4}:{handlerOff:X4}");
    }

    /// <summary>
    /// Remove the BIOS intercept at the DOS stub so the kernel's own dispatch code runs.
    /// Called after DOSINIT sets up the kernel's dispatch pointers at [0060:0204]/[0060:0208].
    /// </summary>
    /// <summary>
    /// Register the DOS stub handler at an additional physical address.
    /// Used when SYSINIT relocates the stub to a different segment (e.g., 2000:3673).
    /// </summary>
    public void RegisterDosStubAt(int physAddr)
    {
        _cpu.RegisterBiosHandler(physAddr, HandleDosStub);
        Console.Error.WriteLine($"[BIOS] DOS stub handler registered at 0x{physAddr:X5}");
    }

    public void DeactivateDosStub()
    {
        _cpu.UnregisterBiosHandler(DOS_STUB_PHYS);
        Console.Error.WriteLine($"[BIOS] DOS stub handler removed — kernel dispatch active");
    }

    private int _int21Count;
    private void HandleInt21()
    {
        _int21Count++;
        if (_int21Count <= 50)
            Console.Error.WriteLine($"[INT21] #{_int21Count} AH={_cpu.AH:X2} AL={_cpu.AL:X2} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4}");
        _dosBios?.HandleInt21();
        DoIret();
    }

    /// <summary>
    /// Catch-all handler for the kernel's DOS stub at 0060:3673.
    /// Many IVT entries (INT 20h, 22h-2Fh, etc.) point here.
    /// Determines the INT number from the stacked return address and dispatches.
    /// </summary>
    private int _dosStubCount;
    private void HandleDosStub()
    {
        // Read INT number from byte before the stacked return address
        var mem = _bus.GetMemoryDirect();
        int sp = (_cpu.SS << 4) + _cpu.SP;
        ushort stkIP = (ushort)(mem[sp] | (mem[sp + 1] << 8));
        ushort stkCS = (ushort)(mem[sp + 2] | (mem[sp + 3] << 8));
        int intByteAddr = ((stkCS << 4) + stkIP - 1) & 0xFFFFF;
        byte intNum = mem[intByteAddr];

        _dosStubCount++;
        if (_dosStubCount <= 30)
            Console.Error.WriteLine($"[DOS-STUB] #{_dosStubCount} INT {intNum:X2}h AH={_cpu.AH:X2} from {stkCS:X4}:{stkIP:X4}");

        switch (intNum)
        {
            case 0x20: // Terminate Program
                _cpu.Halted = true;
                break;
            case 0x21: // DOS Functions (shouldn't reach here if IVT[21h] is set correctly)
                _dosBios?.HandleInt21();
                break;
            case 0x2F: // Multiplex Interrupt
                HandleInt2F();
                break;
            default:
                // Unknown stub interrupt — just return
                break;
        }

        DoIret();
    }

    /// <summary>
    /// INT 2Fh Multiplex Interrupt handler.
    /// Returns "not installed" for most subfunctions.
    /// </summary>
    private void HandleInt2F()
    {
        byte ah = _cpu.AH;
        switch (ah)
        {
            case 0x11: // Network redirector - not installed
                _cpu.AL = 0x00;
                _cpu.Flags.CF = true;
                break;
            case 0x12: // DOS internal (used byAZSTTY.COM, command.com etc.)
                // Subfunction in AL
                _cpu.Flags.CF = false;
                break;
            case 0x15: // MSCDEX - not installed
                _cpu.AL = 0x00;
                break;
            case 0x16: // Windows - not installed
                _cpu.AL = 0x00;
                break;
            case 0x43: // XMS - not installed
                _cpu.AL = 0x00;
                break;
            default:
                // Not installed
                _cpu.AL = 0x00;
                break;
        }
    }
}
