using PC98Emu.Bus;
using PC98Emu.CPU;
using PC98Emu.Scheduler;
using PC98Emu.Devices;
using PC98Emu.Disk;
using PC98Emu.Graphics;
using PC98Emu.Sound;
using PC98Emu.BIOS;

namespace PC98Emu;

public class Emulator
{
    // Core components
    private readonly SystemBus _bus;
    private readonly V30 _cpu;
    private readonly EventScheduler _scheduler;

    // Interrupt controllers
    private readonly PIC _masterPic;
    private readonly PIC _slavePic;

    // Timer
    private readonly PIT _pit;

    // DMA
    private readonly DMA _dma;

    // Floppy disk controller
    private readonly FDC _fdc;

    // Input devices
    private readonly Keyboard _keyboard;
    private readonly Mouse _mouse;

    // Graphics
    private readonly GDC _textGdc;
    private readonly GDC _graphicsGdc;
    private readonly GvramBankController _gvramBank;
    private readonly GRCG _grcg;
    private readonly AnalogPalette _analogPalette;
    private readonly TextRenderer _textRenderer;
    private readonly GraphicsRenderer _graphicsRenderer;

    // Sound
    private readonly YM2608 _ym2608;

    // Other devices
    private readonly RTC _rtc;
    private readonly Serial _serial;
    private readonly Printer _printer;
    private readonly SystemPort _systemPort;

    // Disk
    private readonly DiskManager _diskManager;
    private readonly SASIController _sasi;

    // Calendar IC
    private readonly CalendarIC _calendarIC;

    // BIOS
    private readonly CompatibleBios _bios;
    private readonly BootLoader _bootLoader;

    // Display/Audio (only initialized for Run())
    private Display? _display;
    private AudioOutput? _audioOutput;

    // Frame timing
    private const int CyclesPerFrame = 141844; // 8MHz / 56.4Hz
    private int _frameCycleAccumulator;

    public V30 CPU => _cpu;

    public Emulator()
    {
        _bus = new SystemBus();
        _cpu = new V30(_bus);
        _scheduler = new EventScheduler();

        // Interrupt controllers (PC-98: master at 0x00/0x02, slave at 0x08/0x0A)
        _masterPic = new PIC(0x00, 0x02);
        _slavePic = new PIC(0x08, 0x0A);

        // Timer (PC-98: ports 0x71, 0x73, 0x75, 0x77)
        _pit = new PIT(0x71, 0x73, 0x75, 0x77, () => _masterPic.RaiseIRQ(0));

        // DMA
        _dma = new DMA();

        // Disk manager
        _diskManager = new DiskManager();

        // FDC (IRQ6 on master PIC)
        _fdc = new FDC(_dma, _diskManager, () => _masterPic.RaiseIRQ(6), _bus.GetMemoryDirect());

        // Keyboard (IRQ1 on master PIC)
        _keyboard = new Keyboard(() => _masterPic.RaiseIRQ(1));

        // Mouse
        _mouse = new Mouse();

        // GDC
        _textGdc = new GDC(isText: true);
        _graphicsGdc = new GDC(isText: false);
        _gvramBank = new GvramBankController(_bus);
        _grcg = new GRCG(_bus);
        _analogPalette = new AnalogPalette();
        _bus.Grcg = _grcg; // Wire GRCG into SystemBus for GVRAM intercept

        // Sound (slave PIC IRQ3 = system IRQ11 typically, but just wire to slave)
        _ym2608 = new YM2608(() => _slavePic.RaiseIRQ(3));

        // Other devices
        _rtc = new RTC();
        _serial = new Serial();
        _printer = new Printer();
        _systemPort = new SystemPort();

        // Calendar IC (ports 0x80/0x82 - used by SYSINIT for hardware init)
        _calendarIC = new CalendarIC();

        // Disk controllers
        _sasi = new SASIController(_diskManager);

        // Renderers
        _textRenderer = new TextRenderer(_bus.GetMemoryDirect());
        _graphicsRenderer = new GraphicsRenderer(_bus.GetMemoryDirect(), _bus, _analogPalette);

        // BIOS
        _bios = new CompatibleBios(_cpu, _bus);
        _bios.SetDiskManager(_diskManager);
        _bios.SetKeyboard(_keyboard);
        _bios.SetTextGdc(_textGdc);

        // Boot loader
        _bootLoader = new BootLoader(_cpu, _bus, _diskManager);
    }

    public void Initialize()
    {
        // Register all devices on bus
        _bus.RegisterDevice(_masterPic);
        _bus.RegisterDevice(_slavePic);
        _bus.RegisterDevice(_pit);
        _bus.RegisterDevice(_dma);
        _bus.RegisterDevice(_fdc);
        _bus.RegisterDevice(_keyboard);
        _bus.RegisterDevice(_mouse);
        _bus.RegisterDevice(_textGdc);
        _bus.RegisterDevice(_graphicsGdc);
        _bus.RegisterDevice(_gvramBank);
        _bus.RegisterDevice(_grcg);
        _bus.RegisterDevice(_analogPalette);
        _bus.RegisterDevice(_ym2608);
        _bus.RegisterDevice(_rtc);
        _bus.RegisterDevice(_serial);
        _bus.RegisterDevice(_printer);
        _bus.RegisterDevice(_systemPort);
        _bus.RegisterDevice(_sasi);
        _bus.RegisterDevice(_calendarIC);

        // Initialize PIC with ICW sequence for master (vector base 0x08)
        _bus.WriteIoByte(0x00, 0x11); // ICW1: edge-triggered, cascade, ICW4 needed
        _bus.WriteIoByte(0x02, 0x08); // ICW2: vector base 0x08
        _bus.WriteIoByte(0x02, 0x04); // ICW3: slave on IRQ2
        _bus.WriteIoByte(0x02, 0x01); // ICW4: 8086 mode

        // Initialize PIC with ICW sequence for slave (vector base 0x10)
        _bus.WriteIoByte(0x08, 0x11); // ICW1
        _bus.WriteIoByte(0x0A, 0x10); // ICW2: vector base 0x10
        _bus.WriteIoByte(0x0A, 0x02); // ICW3: cascade identity
        _bus.WriteIoByte(0x0A, 0x01); // ICW4: 8086 mode

        // Initialize BIOS
        _bios.Initialize();

        // Schedule PIT recurring timer (~100Hz timer tick, 8MHz / 100 = 80000 cycles)
        _scheduler.ScheduleRecurring(80000, "PIT_Tick", () => _pit.Tick(80000));

        // Schedule GDC VSync ticks (~56.4Hz frame rate, 8MHz / 56.4 ≈ 141844 cycles)
        // Both text and graphics GDC need VSync to toggle for status register polling
        _scheduler.ScheduleRecurring(141844, "GDC_VSync", () =>
        {
            _textGdc.Tick(141844);
            _graphicsGdc.Tick(141844);
            // Raise IRQ 2 (VSYNC) on master PIC → INT 0Ah
            _masterPic.RaiseIRQ(2);
        });

        // Set initial CPU state: enable interrupts
        _cpu.Flags.IF = true;

        // Initialize stack pointer
        _cpu.SS = 0x0000;
        _cpu.SP = 0x0400;
    }

    public void LoadFloppyDisk(int drive, byte[] data, string format = "d88")
    {
        IDiskImage image = format.ToLowerInvariant() switch
        {
            "d88" or "d98" or "88d" or "98d" => new D88Image(data),
            "hdm" or "tfd" or "xdf" or "dup" => new HDMImage(data),
            "fdi" => new FDIImage(data),
            _ => new D88Image(data),
        };
        _diskManager.MountFloppy(drive, image);
    }

    public void LoadHardDisk(int drive, byte[] data, string format = "hdi")
    {
        IDiskImage image = format.ToLowerInvariant() switch
        {
            "hdi" => new HDIImage(data),
            "nhd" => new NHDImage(data),
            "nfd" => new NFDImage(data),
            _ => new HDIImage(data),
        };
        _diskManager.MountHDD(drive, image);
    }

    public void StepCpu()
    {
        int cycles = _cpu.Step();
        _scheduler.Advance(cycles);

        // Check for pending interrupts
        if (_masterPic.HasInterrupt() && _cpu.Flags.IF)
        {
            int vector = _masterPic.AcknowledgeInterrupt();
            _cpu.Interrupt((byte)vector);
        }
    }

    public void Boot(int drive = 0)
    {
        if (!_bootLoader.Boot(drive))
        {
            Console.WriteLine($"Boot failed from drive {drive}");
        }
    }

    /// <summary>
    /// Reset CPU, memory, and BIOS state for rebooting with a new disk image.
    /// </summary>
    private void ResetForReboot()
    {
        // Clear main memory (keep VRAM and BIOS ROM intact)
        var mem = _bus.GetMemoryDirect();
        Array.Clear(mem, 0, 0xA0000); // Clear conventional memory

        // Reset CPU
        _cpu.AX = 0; _cpu.BX = 0; _cpu.CX = 0; _cpu.DX = 0;
        _cpu.SI = 0; _cpu.DI = 0; _cpu.BP = 0;
        _cpu.CS = 0; _cpu.IP = 0;
        _cpu.DS = 0; _cpu.ES = 0;
        _cpu.SS = 0; _cpu.SP = 0x0400;
        _cpu.Flags.Value = 0;
        _cpu.Flags.IF = true;
        _cpu.Halted = false;

        // Unmount existing HDD
        _diskManager.UnmountHDD(0);

        // Reset DiskBios partition offset (set during previous boot)
        if (_bios.DiskBios != null)
            _bios.DiskBios.SetPartitionOffset(0);

        // Reset DosBios state
        if (_bios.DosBiosInstance != null)
            _bios.DosBiosInstance.Reset();

        // Re-initialize BIOS (sets up IVT, BDA, etc.)
        _bios.Initialize();

        Console.WriteLine("Emulator state reset for reboot.");
    }

    public void Run()
    {
        // Initialize display and audio (requires SDL2)
        bool hasDisplay = false;
        bool hasAudio = false;

        try
        {
            Font.InitKanjiRom();
            _display = new Display(_textRenderer, _graphicsRenderer, _textGdc);
            _display.Init();
            hasDisplay = true;
            Console.WriteLine("Display initialized (640x424)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Display init failed: {ex.Message}");
            Console.Error.WriteLine("Running in headless mode...");
        }

        try
        {
            _audioOutput = new AudioOutput(_ym2608);
            hasAudio = _audioOutput.Init();
            if (hasAudio)
                Console.WriteLine("Audio initialized (44100Hz stereo)");
            else
                Console.Error.WriteLine("Audio unavailable, continuing without sound");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Audio init failed: {ex.Message}");
        }

        _bus.IoDebug = true;
        _bus.EnableTextVramTrace(true);

        long stepCount = 0;

        // Watchpoint: track writes to MSDOS.SYS zero-on-disk area
        // Physical 0x196ED (=0C3C:D32D) to 0x1B1C0 (=0C3C:EDC0+)
        // This catches IO.SYS SYSINIT code relocation into MSDOS.SYS BSS
        int watchHitCount = 0;
        int watchLastStep = 0;
        // Watch IVT entries 0x60-0x107 (INT 18h through INT 41h) to catch corruption
        _bus.SetWatchpointRange(0x60, 0x107);
        _bus.OnWatchHit = (addr, oldVal, newVal) =>
        {
            watchHitCount++;
            if (watchHitCount <= 200)
                Console.Error.WriteLine($"[IVT-WATCH] addr=0x{addr:X4} {oldVal:X2}→{newVal:X2} CS:IP={_cpu.CS:X4}:{_cpu.IP:X4} step={stepCount} AX={_cpu.AX:X4}");
            watchLastStep = (int)stepCount;
        };
        Console.WriteLine("Emulation started. Close the window to exit.");
        bool quit = false;
        string? pendingHdiPath = null;
        if (_display != null)
        {
            _display.MenuBar.OnHdiSelected = (path) =>
            {
                pendingHdiPath = path;
                Console.WriteLine($"HDI selected: {path}");
            };
            _display.MenuBar.OnExitRequested = () => { quit = true; };
        }
        _frameCycleAccumulator = 0;
        int frameCount = 0;
        bool vramDumped = false;
        ushort prevCS = _cpu.CS;
        // Ring buffer for last N instructions before hang detection
        const int traceSize = 50000;
        var traceRing = new (ushort cs, ushort ip, ushort ax, ushort bx, ushort cx, ushort dx, ushort ds, ushort es, ushort sp, ushort flags, ushort si, ushort di, ushort bp)[traceSize];
        int traceIdx = 0;
        bool hangDetected = false;
        bool partitionOffsetSet = false;
        bool dosPatched = false; // One-shot: patch DOS kernel before first user program runs
        bool dosLoopPatched = false; // One-shot: DOSINIT2 keyboard wait loop patched
        bool bootMenuVramDirty = true; // Suppress rendering until boot menu cleanup is done
        int hltSkipCount = 0;
        int traceBuildBPB = 0; // trace next N instructions after buildBPB entry
        int buildBPBCount = 0; // count buildBPB entries to skip redundant SYSINIT calls
        bool msdosDumped = false; // One-shot: dump MSDOS.SYS zero areas on first entry
        bool msdosTracing = false; // Trace instructions in MSDOS.SYS zero areas
        byte[]? savedMsdosCode = null;  // MSDOS.SYS code saved BEFORE first DOSINIT (has valid JMP)
        byte[]? savedMsdosHeader = null;   // Dispatch table/header saved AFTER first DOSINIT
        const int MSDOS_SEG_BASE = 0x0C3C0; // Physical base of segment 0C3C
        const int MSDOS_CODE_START = 0x4240; // MSDOS.SYS code starts at this offset in segment
        const int MSDOS_SAVE_END = 0xE947;   // End of area overwritten by REP MOVSW
        const int MSDOS_HEADER_SIZE = 0x4240; // Full dispatch+workspace area (0000-423F)
        int dosinitTraceCount = 0; // Trace counter for second DOSINIT call
        bool dosinitSecondCall = false; // Flag: second DOSINIT call is active
        int sysinitTraceCount = 0; // Trace counter for SYSINIT execution
        bool sysinitActive = false; // Flag: tracing SYSINIT
        // commandComTraceCount removed — CMD trace disabled
        byte[]? savedIoSysRuntime = null; // IO.SYS runtime snapshot (segment 0060) saved before DOSINIT
        byte[]? savedDpb = null; // DPB bytes saved after initial boot reaches A>
        bool dpbSaved = false; // Flag: DPB has been saved from initial boot
        bool dpbRestoredAfterReboot = false; // Flag: DPB restored after reboot DOSINIT

        while (!quit)
        {
            try
            {
            stepCount++;
            // Detect crash: execution at 2000:2000 (data area)
            if (sysinitActive && _cpu.CS == 0x2000 && _cpu.IP == 0x2000)
            {
                Console.Error.WriteLine($"\n[SYSINIT-CRASH] At 2000:2000 step={stepCount}");
                Console.Error.WriteLine($"[SYSINIT-CRASH] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} SS={_cpu.SS:X4} SP={_cpu.SP:X4}");
                // Dump last 30 trace ring entries
                var cMem = _bus.GetMemoryDirect();
                int cSp = (_cpu.SS << 4) + _cpu.SP;
                Console.Error.Write("[SYSINIT-CRASH] Stack: ");
                for (int b = 0; b < 32 && cSp + b < cMem.Length; b++)
                    Console.Error.Write($"{cMem[cSp + b]:X2} ");
                Console.Error.WriteLine();
                // Dump trace ring
                for (int t = 0; t < traceSize; t++)
                {
                    var e = traceRing[(traceIdx + t) % traceSize];
                    if (e.cs == 0 && e.ip == 0) continue;
                    Console.Error.WriteLine($"  [RING] {e.cs:X4}:{e.ip:X4} AX={e.ax:X4} BX={e.bx:X4} CX={e.cx:X4} DX={e.dx:X4} DS={e.ds:X4} ES={e.es:X4} SP={e.sp:X4} FL={e.flags:X4} SI={e.si:X4} DI={e.di:X4} BP={e.bp:X4}");
                }
                sysinitActive = false;
            }
            // Trace kernel decision phase (between BPB reads and CRT calls)
            if (stepCount >= 880 && stepCount <= 1600 && (_cpu.CS == 0x0060 || _cpu.CS == 0x0636))
            {
                var mem = _bus.GetMemoryDirect();
                int physIP = V30.GetPhysicalAddress(_cpu.CS, _cpu.IP);
                byte b0 = mem[physIP], b1 = mem[physIP+1], b2 = mem[physIP+2];
                Console.Error.WriteLine($"[TRACE] #{stepCount} {_cpu.CS:X4}:{_cpu.IP:X4} [{b0:X2} {b1:X2} {b2:X2}] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} SI={_cpu.SI:X4} DI={_cpu.DI:X4} SP={_cpu.SP:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} FL={_cpu.Flags.Value:X4}");
            }
            // Debug: log when CS enters unexpected segments
            if (_cpu.CS != prevCS)
            {
                // Log all CS changes during early boot (before VRAM dump)
                if (!vramDumped)
                {
                    Console.Error.WriteLine($"[CS] #{stepCount} {prevCS:X4}→{_cpu.CS:X4}:{_cpu.IP:X4} AX={_cpu.AX:X4} SP={_cpu.SP:X4}");
                }
                prevCS = _cpu.CS;

                // Set partition offset when CS first enters IO.SYS (0060) or kernel (0636).
                // The secondary loader uses DA/UA=0x00 LBA reads WITHOUT partition offset;
                // the kernel uses them WITH partition offset to access partition-relative sectors.
                if (!partitionOffsetSet && (_cpu.CS == 0x0060 || _cpu.CS == 0x0636))
                {
                    partitionOffsetSet = true;

                    // Save IO.SYS pristine snapshot BEFORE any device driver init.
                    // Device drivers will overwrite parts of IO.SYS with disk I/O data.
                    // We need this pristine copy later for SYSINIT self-relocation after DOSINIT2.
                    {
                        var pristineMem = _bus.GetMemoryDirect();
                        savedIoSysRuntime = new byte[0x10000];
                        Array.Copy(pristineMem, 0x600, savedIoSysRuntime, 0, 0x10000);
                        Console.Error.Write("[BOOT] Saved pristine IO.SYS. 0B46: ");
                        for (int b = 0; b < 16; b++)
                            Console.Error.Write($"{savedIoSysRuntime[0x0B46 + b]:X2} ");
                        Console.Error.WriteLine();
                    }

                    // Fix IO.SYS loading gap: The PBR loads IO.SYS (65536 bytes) in two
                    // parts because the first read fills segment 0060 up to offset 0xF7FF
                    // (248 sectors * 256 = 63488 bytes at physical 0x600-0xFDFF), then
                    // switches to segment 1000 for the remaining 8 sectors (2048 bytes at
                    // physical 0x10000-0x107FF). This leaves a 512-byte gap at physical
                    // 0xFE00-0xFFFF. Close the gap by shifting the second part down.
                    var gapMem = _bus.GetMemoryDirect();
                    Array.Copy(gapMem, 0x10000, gapMem, 0xFE00, 2048);
                    Console.Error.WriteLine("[BOOT] Fixed IO.SYS loading gap: copied 2048 bytes from phys 0x10000 to 0xFE00");

                    var bootHdd = _diskManager.GetHDD(0);
                    if (bootHdd != null)
                    {
                        byte[] pbrSector = new byte[bootHdd.SectorSize];
                        if (bootHdd.ReadSector(1, 0, 1, pbrSector)) // PBR at cylinder 1
                        {
                            int hiddenSectors = pbrSector[0x1C] | (pbrSector[0x1D] << 8)
                                              | (pbrSector[0x1E] << 16) | (pbrSector[0x1F] << 24);
                            Console.Error.WriteLine($"[BOOT] Kernel entered CS={_cpu.CS:X4}, partition offset={hiddenSectors}");
                            if (hiddenSectors > 0 && hiddenSectors < 100000)
                            {
                                _bios.DiskBios?.SetPartitionOffset(hiddenSectors);

                                // Initialize FAT16 reader for DOS file I/O
                                // The PBR is at the partition start LBA
                                int pbrLba = (1 * bootHdd.Heads + 0) * bootHdd.SectorsPerTrack;
                                var fat16 = new PC98Emu.Disk.Fat16Reader(bootHdd, pbrLba);
                                if (fat16.Initialize())
                                {
                                    fat16.ListRootDir();
                                    _bios.SetFat16Reader(fat16);
                                }
                            }
                        }
                    }

                    // Patch IO.SYS: skip the 55 AA boot signature check at 0060:54FB.
                    // NEC MS-DOS IO.SYS verifies 55 AA at the end of the PBR physical sector
                    // during drive initialization. PC-98 SASI disks with 256-byte sectors often
                    // lack this signature (it's an IBM PC/AT convention). Without this patch,
                    // the drive init fails and MSDOS.SYS cannot be loaded.
                    // Original: 75 45 (JNZ 5542 = error path)
                    // Patched:  90 90 (NOP NOP = fall through to success path)
                    var mem = _bus.GetMemoryDirect();
                    int patchAddr = 0x0060 * 16 + 0x54FB; // Physical address of the JNZ
                    if (mem[patchAddr] == 0x75 && mem[patchAddr + 1] == 0x45)
                    {
                        mem[patchAddr] = 0x90;     // NOP
                        mem[patchAddr + 1] = 0x90; // NOP
                        Console.Error.WriteLine("[BOOT] Patched IO.SYS 55AA check at 0060:54FB (JNZ→NOP)");
                    }

                    // Pre-populate IO.SYS's SASI drive bitmap at [0060:0253].
                    // The detection function at 4F59 also sets this when BDA[04BC]!=0,
                    // but we pre-set it as a safety net.
                    if (_diskManager != null)
                    {
                        int driveMapAddr = 0x0060 * 16 + 0x0253;
                        byte sasiMap = 0;
                        for (int d = 0; d < 4; d++)
                        {
                            if (_diskManager.GetHDD(d) != null)
                                sasiMap |= (byte)(1 << d);
                        }
                        if (sasiMap != 0)
                        {
                            mem[driveMapAddr] = sasiMap;
                            Console.Error.WriteLine($"[BOOT] Pre-set IO.SYS drive bitmap [0060:0253] = 0x{sasiMap:X2}");
                        }
                    }

                    // Patch IO.SYS: NOP out the MOV [1979],CL at 0060:5630.
                    // The scanning function at 55FE counts non-boot HDD entries in the
                    // drive config table by checking DA types 0x81/0x91/0xA1 at [BP+1].
                    // For a single boot HDD (DA=0x80), no entries match, so CX stays at
                    // FFFF and CL=0xFF gets written to [1979]. The kernel then halts at
                    // 0060:4903 because 0xFF >= 4 (max drive count check).
                    // By NOPping the write, [1979] stays at 0 (its initial value),
                    // which correctly indicates no additional (non-boot) HDDs.
                    // Original: 88 0E 79 19 (MOV [1979], CL)
                    // Patched:  90 90 90 90 (NOP NOP NOP NOP)
                    int patchAddr1979 = 0x0060 * 16 + 0x5630;
                    if (mem[patchAddr1979] == 0x88 && mem[patchAddr1979 + 1] == 0x0E
                        && mem[patchAddr1979 + 2] == 0x79 && mem[patchAddr1979 + 3] == 0x19)
                    {
                        mem[patchAddr1979] = 0x90;
                        mem[patchAddr1979 + 1] = 0x90;
                        mem[patchAddr1979 + 2] = 0x90;
                        mem[patchAddr1979 + 3] = 0x90;
                        Console.Error.WriteLine("[BOOT] Patched IO.SYS [1979] write at 0060:5630 (MOV→NOP)");
                    }
                }

                // Activate INT 21h when kernel first exits to non-kernel code.
                if (!dosPatched && _cpu.CS != 0x0060 && _cpu.CS != 0x0636
                    && _cpu.CS < 0xE000 && stepCount > 1000)
                {
                    dosPatched = true;
                    Console.Error.WriteLine($"[DOS-INIT] CS={_cpu.CS:X4}:{_cpu.IP:X4} step={stepCount}");
                    _bios.ActivateInt21();
                }
            }
            // Periodic trace when stuck in low segments after SYSINIT (OUTSIDE CS-change block)
            if (stepCount > 1027000 && _cpu.CS < 0x0100 && stepCount % 500000 == 0)
            {
                var sMem = _bus.GetMemoryDirect();
                Console.Error.Write($"[POLL-LOOP] step={stepCount} {_cpu.CS:X4}:{_cpu.IP:X4} SI={_cpu.SI:X4} AX={_cpu.AX:X4} DS={_cpu.DS:X4} SS={_cpu.SS:X4} SP={_cpu.SP:X4} IF={(_cpu.Flags.IF ? 1 : 0)} code: ");
                int pPhys = V30.GetPhysicalAddress(_cpu.CS, _cpu.IP);
                for (int b = 0; b < 16 && pPhys + b < sMem.Length; b++)
                    Console.Error.Write($"{sMem[pPhys + b]:X2} ");
                Console.Error.WriteLine();
                // Dump IVT for timer interrupt
                ushort t08Off = (ushort)(sMem[0x20] | (sMem[0x21] << 8));
                ushort t08Seg = (ushort)(sMem[0x22] | (sMem[0x23] << 8));
                Console.Error.WriteLine($"[POLL-LOOP] INT08={t08Seg:X4}:{t08Off:X4} BDA[055F]={sMem[0x055F]:X2}");
                // Dump the loop context: what's at C7FF-C80B
                int loopBase = V30.GetPhysicalAddress(_cpu.CS, 0xC7F0);
                Console.Error.Write("[POLL-LOOP] Code C7F0-C820: ");
                for (int b = 0; b < 48 && loopBase + b < sMem.Length; b++)
                    Console.Error.Write($"{sMem[loopBase + b]:X2} ");
                Console.Error.WriteLine();
                // Dump memory that SI might be loaded from
                // Check what's at DS:SI and common poll targets
                int dsSi = (_cpu.DS << 4) + _cpu.SI;
                Console.Error.WriteLine($"[POLL-LOOP] DS:SI={_cpu.DS:X4}:{_cpu.SI:X4} phys={dsSi:X5} val={sMem[dsSi]:X2}{sMem[dsSi+1]:X2}");
                // Check 0060:0142 (referenced by code at C812)
                int flag0142 = sMem[0x600 + 0x0142] | (sMem[0x600 + 0x0143] << 8);
                Console.Error.WriteLine($"[POLL-LOOP] [0060:0142]={flag0142:X4}");
                // PIC state
                Console.Error.WriteLine($"[POLL-LOOP] PIC hasIRQ={_masterPic.HasInterrupt()} IF={_cpu.Flags.IF}");
                // Full text VRAM dump - check for A> prompt
                Console.Error.WriteLine("[VRAM-FULL] === Full text VRAM dump ===");
                for (int row = 0; row < 25; row++)
                {
                    var sb = new System.Text.StringBuilder();
                    bool hasContent = false;
                    for (int col = 0; col < 80; col++)
                    {
                        int addr = 0xA0000 + (row * 80 + col) * 2;
                        ushort ch = (ushort)(sMem[addr] | (sMem[addr + 1] << 8));
                        if (ch != 0 && ch != 0x0020) hasContent = true;
                        if (ch >= 0x20 && ch < 0x7F)
                            sb.Append((char)ch);
                        else if (ch == 0 || ch == 0x0020)
                            sb.Append(' ');
                        else
                            sb.Append($"<{ch:X4}>");
                    }
                    if (hasContent)
                        Console.Error.WriteLine($"[VRAM-FULL] Row{row:D2}: {sb}");
                }
                Console.Error.WriteLine("[VRAM-FULL] === End ===");
            }
            if (_cpu.Halted)
            {
                // If halted without display, just break
                if (!hasDisplay)
                {
                    Console.WriteLine("CPU halted.");
                    break;
                }

                // Fast-forward I/O delay loops during boot (port 0x5F only)
                // Port 0x5F is the I/O wait port — a pure timing delay.
                // Only skip in secondary loader segment (1D20) before boot completes.
                if (_cpu.CS == 0x1D20 && !vramDumped && _cpu.CX > 100)
                {
                    var dm = _bus.GetMemoryDirect();
                    int dip = (_cpu.CS << 4) + _cpu.IP;
                    // Pattern: E6 5F (OUT 5F,AL) followed by E2 xx (LOOP)
                    if (dip + 3 < dm.Length && dm[dip] == 0xE6 && dm[dip + 1] == 0x5F && dm[dip + 2] == 0xE2)
                    {
                        _cpu.CX = 1;
                    }
                }

                // Secondary loader boot menu bypass: HLT at 1D20:171F followed by
                // EB FD (JMP $-1) at 1D20:1720. This is the boot menu waiting for
                // keyboard input or timer timeout. Patch EB FD → NOP NOP and wake CPU
                // so execution falls through to 1D20:1722 (IO.SYS loading code).
                if (_cpu.CS == 0x1D20 && _cpu.IP == 0x1720)
                {
                    var patchMem = _bus.GetMemoryDirect();
                    int patchPhys = (0x1D20 << 4) + 0x1720;
                    if (patchMem[patchPhys] == 0xEB && patchMem[patchPhys + 1] == 0xFD)
                    {
                        patchMem[patchPhys] = 0x90;     // NOP
                        patchMem[patchPhys + 1] = 0x90;  // NOP
                        _cpu.Halted = false;

                        // Clear text VRAM to remove secondary loader boot menu residue.
                        // The boot menu writes 16-bit JIS codes that appear garbled.
                        for (int a = 0xA0000; a < 0xA2000; a += 2)
                        {
                            patchMem[a] = 0x20;     // space (low byte)
                            patchMem[a + 1] = 0x00;  // high byte = 0 (ANK)
                        }
                        for (int a = 0xA2000; a < 0xA4000; a += 2)
                        {
                            patchMem[a] = 0x00;     // invisible (only visible when text is written)
                            patchMem[a + 1] = 0x00;
                        }
                        // Show "Booting..." message after clearing VRAM
                        string bmsg = "Booting...";
                        for (int ci = 0; ci < bmsg.Length; ci++)
                        {
                            int boff = (12 * 80 + 35 + ci) * 2;
                            patchMem[0xA0000 + boff] = (byte)bmsg[ci];
                            patchMem[0xA0000 + boff + 1] = 0x00;
                        }
                        Console.Error.WriteLine("[BOOT-MENU] Patched secondary loader JMP at 1D20:1720 → NOP NOP, cleared VRAM, resuming");
                        continue;
                    }
                }

                // Handle keyboard input while DOS is waiting for buffered input
                bool dosWaiting = _bios.DosBiosInstance != null && _bios.DosBiosInstance.WaitingForInput;
                if (dosWaiting && _display != null && _display.HasKey())
                {
                    var (ascii, scancode, funcKey) = _display.DequeueKey();
                    _bios.DosBiosInstance!.HandleKeyInput(ascii, scancode, funcKey);
                    // If input complete, CPU is unhalted by HandleKeyInput
                    continue;
                }

                // Advance scheduler while halted (so timer IRQ fires)
                // Use larger step to avoid needing 80000 iterations to reach PIT tick
                _scheduler.Advance(100);
                _frameCycleAccumulator += 100;

                // Check for pending interrupts to wake from HLT
                // Don't wake if DOS is waiting for keyboard input — just acknowledge the IRQ
                if (_masterPic.HasInterrupt() && _cpu.Flags.IF)
                {
                    if (dosWaiting)
                    {
                        // Just acknowledge and discard the IRQ (timer tick etc.)
                        _masterPic.AcknowledgeInterrupt();
                    }
                    else
                    {
                        _cpu.Halted = false;
                        int vector = _masterPic.AcknowledgeInterrupt();
                        _cpu.Interrupt((byte)vector);
                    }
                }
            }
            else
            {
                // Record trace for hang detection (skip tight loops — only record IP changes)
                {
                    var prev = traceRing[(traceIdx + traceSize - 1) % traceSize];
                    if (_cpu.CS != prev.cs || _cpu.IP != prev.ip)
                    {
                        traceRing[traceIdx] = (_cpu.CS, _cpu.IP, _cpu.AX, _cpu.BX, _cpu.CX, _cpu.DX, _cpu.DS, _cpu.ES, _cpu.SP, _cpu.Flags.Value, _cpu.SI, _cpu.DI, _cpu.BP);
                        traceIdx = (traceIdx + 1) % traceSize;
                    }
                }

                // Trace around SYSINIT relocator
                if (stepCount >= 23755 && stepCount <= 23765)
                {
                    var rmem = _bus.GetMemoryDirect();
                    int rpa = (_cpu.CS << 4) + _cpu.IP;
                    Console.Error.Write($"[RELOC] #{stepCount} {_cpu.CS:X4}:{_cpu.IP:X4} [");
                    for (int b = 0; b < 12 && rpa + b < rmem.Length; b++)
                        Console.Error.Write($"{rmem[rpa + b]:X2} ");
                    Console.Error.WriteLine($"] AX={_cpu.AX:X4} CX={_cpu.CX:X4} SI={_cpu.SI:X4} DI={_cpu.DI:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} SP={_cpu.SP:X4}");

                    // Before REP MOVSW: dump source bytes that will overwrite 0A2D:486B
                    if (_cpu.CS == 0x0A2D && _cpu.IP == 0x4869) // REP MOVSW instruction
                    {
                        // 0A2D:486B = phys 0x0EB3B. Dest ES:DI. DI for phys 0x0EB3B = 0x0EB3B - (ES<<4)
                        int destDI486B = 0x0EB3B - (_cpu.ES << 4); // = 0x277B
                        int offsetInCopy = destDI486B - _cpu.DI; // = 0x2778
                        int srcSI = (_cpu.SI + offsetInCopy) & 0xFFFF;
                        int srcPhys = (_cpu.DS << 4) + srcSI;
                        Console.Error.Write($"[RELOC-SRC] 486B: destDI={destDI486B:X4} off={offsetInCopy:X4} srcSI={srcSI:X4} phys={srcPhys:X5} bytes=");
                        for (int b = 0; b < 16 && srcPhys + b < rmem.Length; b++)
                            Console.Error.Write($"{rmem[srcPhys + b]:X2} ");
                        Console.Error.WriteLine();
                        // Before copy at 486B
                        Console.Error.Write($"[RELOC-SRC] Before copy at 486B phys=0x0EB3B: ");
                        for (int b = 0; b < 16; b++)
                            Console.Error.Write($"{rmem[0x0EB3B + b]:X2} ");
                        Console.Error.WriteLine();
                        // EDC1 area: 0C3C:EDC1 = phys 0x1B181
                        int destDIEDC1 = 0x1B181 - (_cpu.ES << 4); // = 0xEDC1
                        int edcOff = destDIEDC1 - _cpu.DI; // = 0xEDBE
                        int edcSI = (_cpu.SI + edcOff) & 0xFFFF;
                        int edcPhys = (_cpu.DS << 4) + edcSI;
                        Console.Error.Write($"[RELOC-SRC] EDC1: destDI={destDIEDC1:X4} off={edcOff:X4} srcSI={edcSI:X4} phys={edcPhys:X5} bytes=");
                        for (int b = 0; b < 16 && edcPhys + b < rmem.Length; b++)
                            Console.Error.Write($"{rmem[edcPhys + b]:X2} ");
                        Console.Error.WriteLine();
                        Console.Error.WriteLine($"[RELOC-SRC] Copy: {_cpu.CX:X4} words from {_cpu.DS:X4}:{_cpu.SI:X4} to {_cpu.ES:X4}:{_cpu.DI:X4}");
                        // Check if source/dest overlap
                        int srcStart = (_cpu.DS << 4) + _cpu.SI;
                        int srcEnd = (_cpu.DS << 4) + ((_cpu.SI + _cpu.CX * 2) & 0xFFFF);
                        int dstStart = (_cpu.ES << 4) + _cpu.DI;
                        int dstEnd = (_cpu.ES << 4) + _cpu.DI + _cpu.CX * 2;
                        Console.Error.WriteLine($"[RELOC-SRC] Src phys: {srcStart:X5}→wraps, Dst phys: {dstStart:X5}→{dstEnd:X5}");
                    }
                }

                // Save MSDOS.SYS segment data before SYSINIT REP MOVSW overwrites it.
                // The REP MOVSW copies IO.SYS data to 0C3C:0003-E946, which overlaps with:
                //   1. The dispatch table at 0C3C:0003-001F (set by first DOSINIT call)
                //   2. MSDOS.SYS kernel code/data at 0C3C:4240+ (loaded from disk)
                // After the copy, the dispatch table has stale IO.SYS data and MSDOS.SYS
                // code is replaced by IO.SYS code. We save both areas and restore them.
                if (_cpu.CS == 0x0A2D && _cpu.IP == 0x4869 && savedMsdosHeader == null)
                {
                    var smem = _bus.GetMemoryDirect();
                    // Save header/dispatch area AFTER first DOSINIT (has kernel pointers)
                    savedMsdosHeader = new byte[MSDOS_HEADER_SIZE];
                    for (int b = 0; b < MSDOS_HEADER_SIZE; b++)
                        savedMsdosHeader[b] = smem[MSDOS_SEG_BASE + b];
                    Console.Error.Write("[SYSINIT-FIX] Header dispatch [0..1F]: ");
                    for (int b = 0; b < 32; b++)
                        Console.Error.Write($"{savedMsdosHeader[b]:X2} ");
                    Console.Error.WriteLine();
                }

                // Fix SYSINIT relocation: REP MOVSW at 0A2D:4869 copies IO.SYS segment
                // data to segment 0C3C. The copy source includes 0060:1161 which is zeros
                // on disk (supposed to be filled by earlier init). This overwrites the
                // continuation code at 0A2D:486B with zeros. The original continuation code
                // does: PUSH ES; MOV AX,0201; PUSH AX; RETF → jump to 0C3C:0201.
                // Fix: restore the continuation code after REP MOVSW destroys it.
                if (_cpu.CS == 0x0A2D && _cpu.IP == 0x486B)
                {
                    var smem = _bus.GetMemoryDirect();
                    int codeAddr = (_cpu.CS << 4) + _cpu.IP;
                    if (smem[codeAddr] == 0x00 && smem[codeAddr + 1] == 0x00)
                    {
                        // Restore: PUSH ES / MOV AX,0201 / PUSH AX / RETF
                        byte[] continuation = { 0x06, 0xB8, 0x01, 0x02, 0x50, 0xCB };
                        for (int c = 0; c < continuation.Length; c++)
                            smem[codeAddr + c] = continuation[c];
                        Console.Error.WriteLine($"[SYSINIT-FIX] Restored continuation code at 0A2D:486B → RETF to {_cpu.ES:X4}:0201");

                        // Restore MSDOS.SYS using TWO saved areas:
                        // 1. Header (saved AFTER first DOSINIT): has kernel dispatch pointers
                        // 2. Code (saved BEFORE first DOSINIT): has valid JMP at entry point
                        //    (DOSINIT zeroes its BSS during first call, destroying the entry)
                        if (savedMsdosHeader != null)
                        {
                            // Restore ONLY the dispatch table (0C3C:0003-001F).
                            // Do NOT restore 0020-01FF — those must keep the relocated
                            // SYSINIT values (e.g., [0023] = SYSINIT continuation segment).
                            for (int b = 3; b < 0x20; b++)
                                smem[MSDOS_SEG_BASE + b] = savedMsdosHeader[b];
                            Console.Error.WriteLine($"[SYSINIT-FIX] Restored dispatch table (0003-001F)");
                        }
                        if (savedMsdosCode != null)
                        {
                            // Restore MSDOS.SYS code (0C3C:4240-E946) from PRE-DOSINIT state
                            int codeSize = MSDOS_SAVE_END - MSDOS_CODE_START;
                            for (int b = 0; b < codeSize; b++)
                                smem[MSDOS_SEG_BASE + MSDOS_CODE_START + b] = savedMsdosCode[b];
                            Console.Error.Write($"[SYSINIT-FIX] Restored code@4240 (first 16): ");
                            for (int b = 0; b < 16; b++)
                                Console.Error.Write($"{savedMsdosCode[b]:X2} ");
                            Console.Error.WriteLine();
                        }
                        // Flag: arm tracing for second DOSINIT call (starts at 1060:0000)
                        dosinitSecondCall = false; // Will be set when CS enters 1060
                        dosinitTraceCount = -1; // -1 = armed but not started
                        // Remove the DOS stub intercept so the kernel's own dispatch runs.
                        // DOSINIT's first call has set up [0060:0204]/[0060:0208] with the
                        // kernel's actual INT 21h handler. From now on, let it dispatch natively.
                        _bios.DeactivateDosStub();
                        // Dump destination code at 0C3C:0201 to verify it has valid code
                        int destPhys = 0x0C3C0 + 0x0201;
                        Console.Error.Write($"[SYSINIT-FIX] Code at 0C3C:0201: ");
                        for (int b = 0; b < 32; b++)
                            Console.Error.Write($"{smem[destPhys + b]:X2} ");
                        Console.Error.WriteLine();
                        // Also dump what source 0060:EBE7 contains
                        int srcPhys = 0x600 + 0xEBE7;
                        Console.Error.Write($"[SYSINIT-FIX] Source 0060:EBE7: ");
                        for (int b = 0; b < 32; b++)
                            Console.Error.Write($"{smem[srcPhys + b]:X2} ");
                        Console.Error.WriteLine();
                        // Dump the zero-filled areas that should have code
                        Console.Error.Write($"[SYSINIT-FIX] 0C3C:0003 (copy start): ");
                        for (int b = 0; b < 16; b++)
                            Console.Error.Write($"{smem[0x0C3C0 + 3 + b]:X2} ");
                        Console.Error.WriteLine();
                    }
                }
                // Trace first 20 instructions after entering 0C3C via SYSINIT
                if (_cpu.CS == 0x0C3C && stepCount >= 23770 && stepCount <= 23800)
                {
                    var smem = _bus.GetMemoryDirect();
                    int pa = 0x0C3C0 + _cpu.IP;
                    Console.Error.Write($"[SYSINIT] #{stepCount} 0C3C:{_cpu.IP:X4} [");
                    for (int b = 0; b < 8 && pa + b < smem.Length; b++)
                        Console.Error.Write($"{smem[pa + b]:X2} ");
                    Console.Error.WriteLine($"] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} SS={_cpu.SS:X4} SP={_cpu.SP:X4}");
                }

                // Patch drive type table: change DA type 0xA0 (generic probe) → 0x80 (SASI)
                // so boot drive detection at 0060:4921 matches BDA[0584]=0x80.
                if (!hangDetected && _cpu.CS == 0x0060 && _cpu.IP == 0x4905)
                {
                    var wmem = _bus.GetMemoryDirect();
                    int tablePhys = 0x0060 * 16 + 0x2C86;
                    for (int d = 0; d < 26; d++)
                    {
                        int entryAddr = tablePhys + d * 2;
                        byte driveType = wmem[entryAddr + 1]; // high byte
                        if ((driveType & 0xF0) == 0xA0)
                        {
                            wmem[entryAddr + 1] = (byte)((driveType & 0x0F) | 0x80);
                            Console.Error.WriteLine($"[BOOT] Patched drive type[{d}]: 0x{driveType:X2} → 0x{wmem[entryAddr + 1]:X2}");
                        }
                    }

                    // BDA[055D] should only have SASI low nibble set.
                    // Do NOT set the high nibble - it causes IO.SYS to detect
                    // additional IDE/SCSI drives, inflating the unit count.
                }

                // Protect IO.SYS INT 1Bh hook from being overwritten by disk data.
                // MSDOS.SYS reads disk data (FAT/root dir) into IO.SYS buffers at
                // 087C:1000 (phys 0x97C0), which overlaps the hook code at 0636:40C8
                // (phys 0xA428). This corrupts the JMP FAR target at 0636:40D0.
                // Fix: when the hook is entered, verify and restore the JMP FAR target.
                if (_cpu.CS == 0x0636 && _cpu.IP == 0x40C8)
                {
                    var hmem = _bus.GetMemoryDirect();
                    int hookJmpPhys = 0x6360 + 0x40D0; // JMP FAR opcode at 40D0
                    // Expected: EA 00 00 03 E8 (JMP FAR E803:0000)
                    if (hmem[hookJmpPhys] != 0xEA ||
                        hmem[hookJmpPhys + 1] != 0x00 || hmem[hookJmpPhys + 2] != 0x00 ||
                        hmem[hookJmpPhys + 3] != 0x03 || hmem[hookJmpPhys + 4] != 0xE8)
                    {
                        Console.Error.WriteLine($"[HOOK-FIX] JMP FAR at 0636:40D0 corrupted, restoring E803:0000");
                        // Restore the entire hook: PUSH AX / AND AL,78 / CMP AL,70 / POP AX / JNZ +5 / JMP FAR E803:0000
                        int hookBase = 0x6360 + 0x40C8;
                        byte[] hookCode = { 0x50, 0x24, 0x78, 0x3C, 0x70, 0x58, 0x74, 0x05, 0xEA, 0x00, 0x00, 0x03, 0xE8 };
                        for (int hb = 0; hb < hookCode.Length; hb++)
                            hmem[hookBase + hb] = hookCode[hb];
                    }
                }

                // Log when callbacks enter IO.SYS area during DOSINIT for diagnostics
                if (dosinitSecondCall && _cpu.CS == 0x0060 && _cpu.IP == 0x0000 && stepCount > 1000000)
                {
                    var smem = _bus.GetMemoryDirect();
                    Console.Error.Write($"[IOSYS-ENTRY] 0060:0000 step={stepCount} SP={_cpu.SP:X4} SI={_cpu.SI:X4}");
                    Console.Error.Write($" code={smem[0x600]:X2} {smem[0x601]:X2} {smem[0x602]:X2} {smem[0x603]:X2}");
                    Console.Error.WriteLine($" {smem[0x604]:X2} {smem[0x605]:X2}");
                }
                // Trace MSDOS.SYS loading code path
                if (_cpu.CS == 0x0060)
                {
                    var mem = _bus.GetMemoryDirect();
                    switch (_cpu.IP)
                    {
                        case 0x5A80:
                            Console.Error.WriteLine($"[MSDOS] MSDOS.SYS loader entered, BL(drive)={_cpu.BL:X2} DS={_cpu.DS:X4}");
                            // After SYSINIT, the driver re-init assigns wrong drive letter.
                            // Force drive to 0 (boot HDD = drive A:) for the second call.
                            if (sysinitActive && _cpu.BL != 0x00)
                            {
                                Console.Error.WriteLine($"[MSDOS] Patching drive: 0x{_cpu.BL:X2} → 0x00");
                                _cpu.BL = 0x00;
                            }
                            break;
                        case 0x5AA9: // before CALL 5B12 (init, sub-cmd 0)
                            Console.Error.WriteLine($"[MSDOS] Call 5B12 (init)");
                            break;
                        case 0x5AAC: // before CALL 5B25 (build BPB, sub-cmd 1)
                            Console.Error.WriteLine($"[MSDOS] Call 5B25 (buildBPB)");
                            break;
                        case 0x5AAF: // JNZ check after 5B25
                            {
                                Console.Error.WriteLine($"[MSDOS] After buildBPB: ZF={(_cpu.Flags.ZF ? 1 : 0)} AX={_cpu.AX:X4}");
                                // Dump BPB table and related state
                                var wmem = _bus.GetMemoryDirect();
                                int seg0060 = 0x0060 * 16;
                                int val27C4 = wmem[seg0060 + 0x27C4] | (wmem[seg0060 + 0x27C5] << 8);
                                int val2CBB = wmem[seg0060 + 0x2CBB];
                                int val2CBA = wmem[seg0060 + 0x2CBA];
                                int val2CFD = wmem[seg0060 + 0x2CFD];
                                Console.Error.WriteLine($"[MSDOS] BPB state: [27C4]={val27C4:X4} [2CBB]={val2CBB:X2} [2CBA]={val2CBA:X2} [2CFD]={val2CFD:X2}");
                                // Dump first 34 bytes of BPB table for drive 0 (HDD table at 2B4E)
                                int tblBase = seg0060 + 0x2B4E;
                                var bpbBytes = new System.Text.StringBuilder();
                                for (int b = 0; b < 34; b++)
                                    bpbBytes.Append($"{wmem[tblBase + b]:X2} ");
                                Console.Error.WriteLine($"[MSDOS] BPB table[0060:2B4E+0..33]: {bpbBytes}");
                                // Also dump floppy table at 1B58
                                int ftblBase = seg0060 + 0x1B58;
                                var fbpbBytes = new System.Text.StringBuilder();
                                for (int b = 0; b < 34; b++)
                                    fbpbBytes.Append($"{wmem[ftblBase + b]:X2} ");
                                Console.Error.WriteLine($"[MSDOS] BPB table[0060:1B58+0..33]: {fbpbBytes}");
                            }
                            break;
                        case 0x5AB1: // CALL 5C17
                            Console.Error.WriteLine($"[MSDOS] Call 5C17");
                            break;
                        case 0x5AB4: // JNZ check after 5C17
                            Console.Error.WriteLine($"[MSDOS] After 5C17: ZF={(_cpu.Flags.ZF ? 1 : 0)} AX={_cpu.AX:X4}");
                            break;
                        case 0x5AB6: // CALL 5B3C (media check, sub-cmd 2)
                            Console.Error.WriteLine($"[MSDOS] Call 5B3C (mediaCheck)");
                            break;
                        case 0x5AB9: // JNZ check after 5B3C
                            Console.Error.WriteLine($"[MSDOS] After mediaCheck: ZF={(_cpu.Flags.ZF ? 1 : 0)} AX={_cpu.AX:X4}");
                            break;
                        case 0x5ABB: // CALL 5B65
                            Console.Error.WriteLine($"[MSDOS] Call 5B65");
                            break;
                        case 0x5ABE: // CALL 5BC3 (read root dir)
                            Console.Error.WriteLine($"[MSDOS] Call 5BC3 (readRootDir)");
                            break;
                        case 0x5AC1: // JNZ check after 5BC3
                            Console.Error.WriteLine($"[MSDOS] After readRootDir: ZF={(_cpu.Flags.ZF ? 1 : 0)} AX={_cpu.AX:X4}");
                            break;
                        case 0x5AEB: // Error: MSDOS.SYS error message
                            Console.Error.WriteLine($"[MSDOS] ERROR PATH: BX=5A31 (MSDOS.SYS error)");
                            break;
                        case 0x5AF0: // Error: not found
                            Console.Error.WriteLine($"[MSDOS] ERROR PATH: BX=5A5F (not found)");
                            break;
                        case 0x5CBF: // Device driver call wrapper - CALL FAR 0060:3487
                        {
                            int cmdBase = 0x0060 * 16 + 0x059A;
                            byte reqCode = mem[cmdBase];
                            byte driveId = mem[cmdBase + 1];
                            byte subCmd = mem[cmdBase + 2];
                            ushort status = (ushort)(mem[cmdBase + 3] | (mem[cmdBase + 4] << 8));
                            Console.Error.WriteLine($"[MSDOS] DevDriver CALL: req={reqCode:X2} drive={driveId:X2} subcmd={subCmd:X2} status={status:X4}");
                            break;
                        }
                        case 0x5CCA: // After device driver call - check result
                        {
                            int cmdBase = 0x0060 * 16 + 0x059A;
                            ushort status = (ushort)(mem[cmdBase + 3] | (mem[cmdBase + 4] << 8));
                            Console.Error.WriteLine($"[MSDOS] DevDriver RESULT: status={status:X4} (error={((status & 0x8000) != 0 ? "YES" : "no")})");
                            break;
                        }
                    }
                }
                // Trace device driver handler entries in segment 0636
                if (_cpu.CS == 0x0636)
                {
                    // Skip redundant SYSINIT buildBPB calls that cause stack overflow.
                    // The BPB is already filled by the IO.SYS call. SYSINIT at E809
                    // loops calling 060A for each "unit", but the SIMPLIFIED path at
                    // 1EF7 runs garbage code that corrupts the stack (~0x3F bytes/iter).
                    if (_cpu.IP == 0x060A && buildBPBCount > 0)
                    {
                        var skipMem = _bus.GetMemoryDirect();
                        int stackPhys = (_cpu.SS << 4) + _cpu.SP;
                        ushort retCS = (ushort)(skipMem[stackPhys + 2] | (skipMem[stackPhys + 3] << 8));
                        if (retCS == 0xE809)
                        {
                            ushort retIP = (ushort)(skipMem[stackPhys] | (skipMem[stackPhys + 1] << 8));
                            // Set command block status to "done, no error" (0x0100)
                            int cmdBase = 0x0060 * 16 + 0x059A;
                            skipMem[cmdBase + 3] = 0x00;
                            skipMem[cmdBase + 4] = 0x01;
                            // Simulate RETF
                            _cpu.CS = retCS;
                            _cpu.IP = retIP;
                            _cpu.SP += 4;
                            Console.Error.WriteLine($"[DEVDRV] SYSINIT buildBPB call SKIPPED → {retCS:X4}:{retIP:X4} SP={_cpu.SP:X4}");
                        }
                    }
                    // Skip device driver delay loop at 0636:10F0 (POP AX / LOOP pattern)
                    // CX counts down from ~FC00; on real hardware this is a sub-ms delay.
                    if (_cpu.IP == 0x10F0 && _cpu.CX > 1)
                    {
                        _cpu.CX = 1; // exit loop on next iteration
                    }
                    switch (_cpu.IP)
                    {
                        case 0x1CFE:
                        {
                            Console.Error.WriteLine($"[DEVDRV] cmd0_init entered, AX={_cpu.AX:X4} BX={_cpu.BX:X4}");
                            // Also dump [0037], [00AD] for init state
                            var imem = _bus.GetMemoryDirect();
                            int seg0060 = 0x0060 * 16;
                            Console.Error.WriteLine($"[DEVDRV] init state: [0060:0037]={imem[seg0060+0x0037]:X2} [0060:00AD]={imem[seg0060+0x00AD]:X2} [0060:3452]={imem[seg0060+0x3452]:X2}");
                            break;
                        }
                        case 0x1D6F:
                        {
                            buildBPBCount++;
                            traceBuildBPB = 200;
                            var bpbMem = _bus.GetMemoryDirect();
                            int seg0060b = 0x0060 * 16;
                            int drv = bpbMem[seg0060b + 0x1770];
                            Console.Error.WriteLine($"[DEVDRV] cmd1_buildBPB #{buildBPBCount} entered, AX={_cpu.AX:X4} drv={drv}");

                            // Directly populate the HDD BPB table from the disk image's PBR.
                            // The SASI shortcut at 1D72 skips PBR reading for type 0x80,
                            // so we pre-fill the table before the handler runs.
                            var hdd = _diskManager?.GetHDD(0);
                            if (hdd != null)
                            {
                                // Read PBR sector (at partition offset LBA=264)
                                int partitionLba = 264;
                                int secPerCyl = hdd.Heads * hdd.SectorsPerTrack;
                                int pbrCyl = partitionLba / secPerCyl;
                                int pbrRem = partitionLba % secPerCyl;
                                int pbrHead = pbrRem / hdd.SectorsPerTrack;
                                int pbrSec = (pbrRem % hdd.SectorsPerTrack) + 1;
                                byte[] pbrBuf = new byte[hdd.SectorSize];
                                if (hdd.ReadSector(pbrCyl, pbrHead, pbrSec, pbrBuf))
                                {
                                    // BPB is at PBR+0x0B, 13 bytes:
                                    //  +0: bytes_per_sector (2)
                                    //  +2: sectors_per_cluster (1)
                                    //  +3: reserved_sectors (2)
                                    //  +5: num_FATs (1)
                                    //  +6: root_dir_entries (2)
                                    //  +8: total_sectors_16 (2)
                                    //  +A: media_descriptor (1)
                                    //  +B: sectors_per_FAT (2)
                                    // Write 13 bytes of BPB into table at 0060:2B4E
                                    int tblAddr = seg0060b + 0x2B4E;
                                    for (int b = 0; b < 13; b++)
                                        bpbMem[tblAddr + b] = pbrBuf[0x0B + b];

                                    int bps = pbrBuf[0x0B] | (pbrBuf[0x0C] << 8);
                                    int spc = pbrBuf[0x0D];
                                    int media = pbrBuf[0x15];
                                    Console.Error.WriteLine($"[BPB-FILL] Wrote BPB to 0060:2B4E from PBR: bps={bps} spc={spc} media=0x{media:X2}");

                                    // Also write sector size to [0060:27C4] directly
                                    // (the kernel reads it at 0636:4738 from the BPB table,
                                    //  but in case the SASI path skips that too)
                                    bpbMem[seg0060b + 0x27C4] = pbrBuf[0x0B];
                                    bpbMem[seg0060b + 0x27C5] = pbrBuf[0x0C];
                                    Console.Error.WriteLine($"[BPB-FILL] Set [27C4]={bps:X4}");
                                }
                                else
                                {
                                    Console.Error.WriteLine("[BPB-FILL] ERROR: Failed to read PBR sector");
                                }
                            }
                            break;
                        }
                        case 0x1D71: // after JO not taken (next instruction)
                            Console.Error.WriteLine($"[DEVDRV] buildBPB@1D71: JO not taken, continuing. AL={_cpu.AL:X2}");
                            break;
                        case 0x1D7F: // JNZ after TEST [BX+1E53], 02h
                            Console.Error.WriteLine($"[DEVDRV] buildBPB@1D7F: JNZ bit1 check. ZF={(_cpu.Flags.ZF ? 1 : 0)}");
                            break;
                        case 0x1D8E: // after JNZ taken (bit 1 set)
                            Console.Error.WriteLine($"[DEVDRV] buildBPB@1D8E: normal path, setting up XLAT");
                            break;
                        case 0x1D13: // JO target from buildBPB entry
                            Console.Error.WriteLine($"[DEVDRV] buildBPB@1D13: JO taken! OF was set at entry. AX={_cpu.AX:X4}");
                            break;
                        case 0x1DAD: // after JNZ in buildBPB
                            Console.Error.WriteLine($"[DEVDRV] buildBPB@1DAD: AL={_cpu.AL:X2} AH={_cpu.AH:X2} [1771]={_bus.GetMemoryDirect()[0x0060*16+0x1771]:X2}");
                            break;
                        case 0x1DC0: // before PUSH ES / INT 1Bh setup
                            Console.Error.WriteLine($"[DEVDRV] buildBPB@1DC0: AL={_cpu.AL:X2} AX={_cpu.AX:X4}");
                            break;
                        case 0x1DD8: // MOV ES, CS:[0030]
                            Console.Error.WriteLine($"[DEVDRV] buildBPB@1DD8: CS:[0030]={_bus.GetMemoryDirect()[0x6360+0x0030]:X2}{_bus.GetMemoryDirect()[0x6360+0x0031]:X2}");
                            break;
                        case 0x1DEE: // MOV AH, D6h
                            Console.Error.WriteLine($"[DEVDRV] buildBPB@1DEE: about to set AH=D6, current AX={_cpu.AX:X4}");
                            break;
                        case 0x1DF0: // INT 1Bh call inside buildBPB
                            Console.Error.WriteLine($"[DEVDRV] buildBPB INT 1Bh: AH={_cpu.AH:X2} AL={_cpu.AL:X2} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} ES:BP={_cpu.ES:X4}:{_cpu.BP:X4}");
                            break;
                        case 0x1EF7: // simplified path (skip disk read)
                            Console.Error.WriteLine($"[DEVDRV] buildBPB@1EF7: SIMPLIFIED path (no disk read!)");
                            break;
                        case 0x234D:
                            Console.Error.WriteLine($"[DEVDRV] cmd2_mediaCheck entered, AX={_cpu.AX:X4} BX={_cpu.BX:X4}");
                            break;
                        case 0x24D9:
                            Console.Error.WriteLine($"[DEVDRV] cmd4_read entered, AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} ES:DI={_cpu.ES:X4}:{_cpu.DI:X4}");
                            break;
                        case 0x4738: // MOV AX, [BX] before writing to [27C4]
                        {
                            var rmem4738 = _bus.GetMemoryDirect();
                            int bx4738 = _cpu.BX;
                            int physAddr = _cpu.DS * 16 + bx4738;
                            int val = rmem4738[physAddr] | (rmem4738[physAddr + 1] << 8);
                            Console.Error.WriteLine($"[DEVDRV] 4738: MOV AX,[BX={bx4738:X4}] DS={_cpu.DS:X4} -> phys={physAddr:X5} val={val:X4} [2CBB]={rmem4738[0x0060*16+0x2CBB]:X2} [2CBA]={rmem4738[0x0060*16+0x2CBA]:X2} [2CFD]={rmem4738[0x0060*16+0x2CFD]:X2}");
                            break;
                        }
                        case 0x21A3: // get drive type function
                        {
                            Console.Error.WriteLine($"[DEVDRV] getDriveType: AL(drive)={_cpu.AL:X2} AH={_cpu.AH:X2}");
                            // Prevent out-of-range drive number crash (max 26 drives A-Z)
                            if (_cpu.AL >= 0x1A)
                            {
                                _cpu.AL = 0x00; // return "no drive"
                                _cpu.IP = 0x21F8; // skip to RET
                            }
                            break;
                        }
                    }
                }
                // Log when getDriveType returns (after RET at 0636:21F8 = 7F58 in file)
                if (_cpu.CS == 0x0636 && _cpu.IP == 0x21F8)
                {
                    Console.Error.WriteLine($"[DEVDRV] getDriveType returns: AL={_cpu.AL:X2} (type={_cpu.AL & 0xF0:X2})");
                }
                // Log cmd0_init return - when it JMPs to 0060:34E4 (success return)
                // At return, [BX+0D] = number of units. BX should point to command block (059A).
                // So [059A+0D] = [05A7] = number of units
                if (_cpu.CS == 0x0060 && _cpu.IP == 0x34E4)
                {
                    var rmem = _bus.GetMemoryDirect();
                    int cmdBase = 0x0060 * 16 + 0x059A;
                    byte units = rmem[cmdBase + 0x0D]; // [05A7]
                    byte cmdCode = rmem[cmdBase + 0x02]; // command code
                    ushort statusW = (ushort)(rmem[cmdBase + 3] | (rmem[cmdBase + 4] << 8));

                    // Fix: SASI driver reports 4 max units (partition slots) but our disk
                    // has only 1 partition. Patch units to 1 after cmd0_init to prevent
                    // inflated drive letter assignment.
                    if (cmdCode == 0x00 && units > 1)
                    {
                        Console.Error.WriteLine($"[DEVDRV] Patching init units: {units} → 1");
                        rmem[cmdBase + 0x0D] = 1;
                        _cpu.AL = 1; // AX low byte also returns unit count
                        units = 1;
                    }

                    Console.Error.WriteLine($"[DEVDRV] dispatch return: [05A7](units)={units:X2} status={statusW:X4} AX={_cpu.AX:X4}");
                    // Dump drive type table entries (first 30) - table is in segment 0060
                    int tblBase = 0x0060 * 16 + 0x2C86;
                    Console.Error.Write("[DEVDRV] driveTypes[0..29]: ");
                    for (int d = 0; d < 30; d++)
                    {
                        ushort dt = (ushort)(rmem[tblBase + d*2] | (rmem[tblBase + d*2 + 1] << 8));
                        if (dt != 0) Console.Error.Write($"[{d:X2}]={dt:X4} ");
                    }
                    Console.Error.WriteLine();
                }

                if (traceBuildBPB > 0)
                {
                    var tmem = _bus.GetMemoryDirect();
                    int tphys = V30.GetPhysicalAddress(_cpu.CS, _cpu.IP);
                    Console.Error.WriteLine($"[BPB-TRACE] {_cpu.CS:X4}:{_cpu.IP:X4} [{tmem[tphys]:X2} {tmem[tphys+1]:X2} {tmem[tphys+2]:X2}] AX={_cpu.AX:X4} FL={_cpu.Flags.Value:X4}");
                    traceBuildBPB--;
                }
                // Trace DOSINIT second call to understand where it exits
                if (dosinitTraceCount == -1 && _cpu.CS == 0x1060)
                {
                    dosinitSecondCall = true;
                    dosinitTraceCount = 0;
                    Console.Error.WriteLine($"[DOSINIT2] === Second DOSINIT call entered at 1060:{_cpu.IP:X4} ===");
                }
                if (dosinitSecondCall && dosinitTraceCount < 500)
                {
                    var dm = _bus.GetMemoryDirect();
                    int pa = V30.GetPhysicalAddress(_cpu.CS, _cpu.IP);
                    if (pa < dm.Length && _cpu.CS < 0xE000) // skip BIOS ROM tracing
                    {
                        Console.Error.Write($"[DOSINIT2] #{dosinitTraceCount} {_cpu.CS:X4}:{_cpu.IP:X4} [");
                        for (int b = 0; b < 6 && pa + b < dm.Length; b++)
                            Console.Error.Write($"{dm[pa + b]:X2} ");
                        Console.Error.WriteLine($"] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} SS={_cpu.SS:X4} SP={_cpu.SP:X4} SI={_cpu.SI:X4} DI={_cpu.DI:X4}");
                    }
                    dosinitTraceCount++;
                }
                // Detect DOSINIT2 keyboard wait loop.
                // After displaying boot banner, DOSINIT2 waits for F5/F8 keys
                // with a timer-based timeout. Patch tight loops and continuously
                // inject Enter keys + advance timer to break out.
                if (dosinitSecondCall && _cpu.CS == 0x1060 && stepCount > 1012000)
                {
                    var kmem = _bus.GetMemoryDirect();
                    // Skip the MS-DOS 6 boot menu (F5/F8 key check with timeout).
                    // The boot menu runs at 1060:E2BD-E4F4. After it completes,
                    // execution continues at 1060:E4F4+. We skip directly there.
                    if (!dosLoopPatched && _cpu.IP >= 0xE2BD && _cpu.IP <= 0xE500)
                    {
                        dosLoopPatched = true;
                        Console.Error.WriteLine($"[BOOTMENU] First entry at IP={_cpu.IP:X4} SP={_cpu.SP:X4}");

                        // Patch boot menu code in memory to skip HLT/timeout mechanism.
                        // The boot menu at E2BD-E321 displays a banner, beeps, and enters
                        // HLT at E31F / JMP E31F at E320. INT 04 at E302 normally installs
                        // a timer handler for the F5/F8 timeout. We don't emulate this
                        // timer mechanism, so we NOP out the blocking instructions:
                        //   E302: CD 04 (INT 04) → 90 90 (NOP NOP) - skip timer setup
                        //   E31F: F4 (HLT) → 90 (NOP) - don't halt
                        //   E320: EB FD (JMP E31F) → 90 90 (NOP NOP) - don't loop
                        // After NOPing, the code falls through from the beep to E322
                        // (a display subroutine). E322 ends with RET at E366, which
                        // pops the return address from the outer loop (1060:2000).
                        int patchBase = 0x10600;
                        kmem[patchBase + 0xE302] = 0x90; // NOP (was CD)
                        kmem[patchBase + 0xE303] = 0x90; // NOP (was 04)
                        kmem[patchBase + 0xE31F] = 0x90; // NOP (was F4=HLT)
                        kmem[patchBase + 0xE320] = 0x90; // NOP (was EB)
                        kmem[patchBase + 0xE321] = 0x90; // NOP (was FD)
                        Console.Error.WriteLine("[BOOTMENU] Patched: INT 04 → NOP, HLT → NOP, JMP → NOP");

                        // Also NOP the beep delay loops (E30F-E31E) to speed up boot.
                        // The delay is: 8 outer loops × 65535 inner loops of OUT 5F.
                        for (int i = 0xE30F; i <= 0xE31E; i++)
                            kmem[patchBase + i] = 0x90;
                        Console.Error.WriteLine("[BOOTMENU] Patched: beep delay loops → NOP");

                        // Populate drive availability bitmap
                        _cpu.Flags.IF = true;
                        int drvBitmapPhys = 0x600 + 0x047E;
                        kmem[drvBitmapPhys] = 0x01;
                        kmem[drvBitmapPhys + 1] = 0x00;

                        // The CPU is at E316 (inside the delay loop). The PUSH CX at
                        // E312 already executed (SP went from 12B4 to 12B2, pushing CX=8).
                        // Undo the PUSH so the RET at E366 pops the correct return address.
                        _cpu.SP += 2;
                        Console.Error.WriteLine($"[BOOTMENU] Adjusted SP: {(_cpu.SP-2):X4} → {_cpu.SP:X4}");

                        // Patch the floppy drive scan JMP at D55D to exit the function
                        // instead of looping back to E4F4 (boot menu display).
                        // D55D: E9 94 0F (JMP E4F4) → E9 11 00 (JMP D571 = POP DS; POP AX; RET)
                        kmem[patchBase + 0xD55D] = 0xE9;
                        kmem[patchBase + 0xD55E] = 0x11;
                        kmem[patchBase + 0xD55F] = 0x00;
                        Console.Error.WriteLine("[BOOTMENU] Patched: D55D JMP E4F4 → JMP D571 (exit drive scan)");

                        // Patch D511 (SASI drive scan) to return CX=1 immediately.
                        // D511 is called from D858 (CALL D511) in the drive count function.
                        // Original code enters a complex SASI loop that reads runtime data.
                        // Replace with: XOR CX,CX; INC CX; RET (returns CX=1 = one HDD)
                        kmem[patchBase + 0xD511] = 0x33; // XOR CX,CX
                        kmem[patchBase + 0xD512] = 0xC9;
                        kmem[patchBase + 0xD513] = 0x41; // INC CX
                        kmem[patchBase + 0xD514] = 0xC3; // RET
                        Console.Error.WriteLine("[BOOTMENU] Patched: D511 SASI scan → XOR CX,CX; INC CX; RET");

                        // NOP the boot menu display loop JMP at D830.
                        // The loop: D732 → display+drive scan → D82A DEC CX → D82E JE D833 → D830 JMP D732
                        // CX starts at 0x5860 (timeout counter). NOP the JMP so loop runs once then falls through.
                        kmem[patchBase + 0xD830] = 0x90; // NOP (was E9)
                        kmem[patchBase + 0xD831] = 0x90; // NOP (was FF)
                        kmem[patchBase + 0xD832] = 0x90; // NOP (was FE)
                        Console.Error.WriteLine("[BOOTMENU] Patched: D830 JMP D732 → NOP (exit boot menu loop)");

                        // Clean VRAM and enable rendering
                        for (int pos = 0; pos < 80 * 25; pos++)
                        {
                            int a = 0xA0000 + pos * 2;
                            int aa = 0xA2000 + pos * 2;
                            ushort code = (ushort)(kmem[a] | (kmem[a + 1] << 8));
                            if (code >= 0x100)
                            {
                                kmem[a] = 0x20;
                                kmem[a + 1] = 0x00;
                                kmem[aa] = 0x00;
                                kmem[aa + 1] = 0x00;
                            }
                            else if (code <= 0x20)
                            {
                                kmem[aa] = 0x00;
                                kmem[aa + 1] = 0x00;
                            }
                        }
                        bootMenuVramDirty = false;

                        // Restore DPB after reboot's DOSINIT completes.
                        // The boot menu is the last DOSINIT2 step before COMMAND.COM.
                        // The DPB at 0060:0ABC may not be properly initialized after
                        // reboot because the DOSINIT2 code path differs slightly.
                        // Restore the saved DPB from the initial boot.
                        if (savedDpb != null && !dpbRestoredAfterReboot)
                        {
                            dpbRestoredAfterReboot = true;
                            int dpbPhys = 0x600 + 0x0ABC;
                            Array.Copy(savedDpb, 0, kmem, dpbPhys, savedDpb.Length);
                            Console.Error.Write($"[DPB-RESTORE] Restored driver data at 0060:0ABC (+0E=");
                            Console.Error.Write($"{kmem[dpbPhys + 0x0E]:X2}{kmem[dpbPhys + 0x0F]:X2}): ");
                            for (int b = 0; b < 32; b++)
                                Console.Error.Write($"{kmem[dpbPhys + b]:X2} ");
                            Console.Error.WriteLine();
                        }
                    }
                }

                int cycles = _cpu.Step();
                _scheduler.Advance(cycles);
                _frameCycleAccumulator += cycles;

                // Trace SYSINIT execution after DOSINIT2 returns
                if (sysinitActive && sysinitTraceCount < 500 && _cpu.CS < 0xE000)
                {
                    var tm = _bus.GetMemoryDirect();
                    int tpa = (_cpu.CS << 4) + _cpu.IP;
                    Console.Error.Write($"[SYSINIT] #{sysinitTraceCount} {_cpu.CS:X4}:{_cpu.IP:X4} [");
                    for (int b = 0; b < 6 && tpa + b < tm.Length; b++)
                        Console.Error.Write($"{tm[tpa + b]:X2} ");
                    Console.Error.WriteLine($"] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} SS={_cpu.SS:X4} SP={_cpu.SP:X4} SI={_cpu.SI:X4} DI={_cpu.DI:X4}");
                    sysinitTraceCount++;
                }

                // Trace DOSINIT2 return: when CS leaves 1060 after DOSINIT2 started
                // Detect DOSINIT2 return to SYSINIT: CS leaves 1060 and arrives at 0DA0
                // (the relocated SYSINIT segment). Must not trigger on INT calls to BIOS/IVT.
                if (dosinitSecondCall && !sysinitActive && _cpu.CS == 0x0DA0 && _cpu.IP == 0x0600)
                {
                    var retMem = _bus.GetMemoryDirect();
                    int retPhys = (_cpu.CS << 4) + _cpu.IP;
                    Console.Error.WriteLine($"\n[DOSINIT2-RET] {_cpu.CS:X4}:{_cpu.IP:X4} (phys 0x{retPhys:X5}) step={stepCount}");
                    Console.Error.WriteLine($"[DOSINIT2-RET] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} SS={_cpu.SS:X4} SP={_cpu.SP:X4}");
                    Console.Error.Write("[DOSINIT2-RET] Code: ");
                    for (int b = 0; b < 32 && retPhys + b < retMem.Length; b++)
                        Console.Error.Write($"{retMem[retPhys + b]:X2} ");
                    Console.Error.WriteLine();
                    // Also dump what's at 0000:0000 and 0060:0000
                    Console.Error.Write("[DOSINIT2-RET] [0000:0000]: ");
                    for (int b = 0; b < 16; b++) Console.Error.Write($"{retMem[b]:X2} ");
                    Console.Error.WriteLine();
                    Console.Error.Write("[DOSINIT2-RET] [0060:0000]: ");
                    for (int b = 0; b < 16; b++) Console.Error.Write($"{retMem[0x600 + b]:X2} ");
                    Console.Error.WriteLine();
                    // Dump INT 21h IVT entry (at 0x84-0x87)
                    ushort int21Off = (ushort)(retMem[0x84] | (retMem[0x85] << 8));
                    ushort int21Seg = (ushort)(retMem[0x86] | (retMem[0x87] << 8));
                    int int21Phys = (int21Seg << 4) + int21Off;
                    Console.Error.Write($"[DOSINIT2-RET] INT 21h → {int21Seg:X4}:{int21Off:X4} (phys {int21Phys:X5}) code: ");
                    for (int b = 0; b < 16 && int21Phys + b < retMem.Length; b++)
                        Console.Error.Write($"{retMem[int21Phys + b]:X2} ");
                    Console.Error.WriteLine();

                    // Dump context
                    Console.Error.WriteLine($"[DOSINIT2-RET] SI={_cpu.SI:X4} DI={_cpu.DI:X4} BP={_cpu.BP:X4}");
                    int stkPhys = (_cpu.SS << 4) + _cpu.SP;
                    Console.Error.Write("[DOSINIT2-RET] Stack: ");
                    for (int s = 0; s < 16 && stkPhys + s * 2 < retMem.Length; s++)
                        Console.Error.Write($"{(ushort)(retMem[stkPhys + s*2] | (retMem[stkPhys + s*2+1] << 8)):X4} ");
                    Console.Error.WriteLine();

                    // DOSINIT2 overwrites the return area with kernel dispatch data.
                    // Only restore the corrupted entry point from disk. The rest of the
                    // segment has runtime-initialized SYSINIT code (BSS filled at boot).
                    int dstBase = _cpu.CS << 4;

                    // Dump what's CURRENTLY at FE03 (runtime-initialized SYSINIT code)
                    int fe03Phys = dstBase + 0xFE03;
                    Console.Error.Write("[DOSINIT2-RET] Runtime FE03: ");
                    for (int b = 0; b < 32 && fe03Phys + b < retMem.Length; b++)
                        Console.Error.Write($"{retMem[fe03Phys + b]:X2} ");
                    Console.Error.WriteLine();
                    // Count non-zero bytes at FE03-FFFF
                    int nzFE = 0;
                    for (int b = 0xFE03; b < 0x10000 && dstBase + b < retMem.Length; b++)
                        if (retMem[dstBase + b] != 0) nzFE++;
                    Console.Error.WriteLine($"[DOSINIT2-RET] Non-zero bytes at FE03-FFFF: {nzFE}/{0x10000 - 0xFE03}");

                    // Step 1: Restore MSDOS.SYS kernel data FIRST (before mini SYSINIT,
                    // because 0C3C:0000-423F overlaps with 0DA0:0600 = 0C3C:1C40).
                    if (savedMsdosHeader != null)
                    {
                        for (int b = 0; b < savedMsdosHeader.Length; b++)
                            retMem[MSDOS_SEG_BASE + b] = savedMsdosHeader[b];
                        Console.Error.Write("[DOSINIT2-RELOC] Restored MSDOS workspace (0000-423F): ");
                        for (int b = 0; b < 16; b++)
                            Console.Error.Write($"{retMem[MSDOS_SEG_BASE + b]:X2} ");
                        Console.Error.WriteLine();
                    }
                    if (savedMsdosCode != null)
                    {
                        int codeSize = MSDOS_SAVE_END - MSDOS_CODE_START;
                        for (int b = 0; b < codeSize; b++)
                            retMem[MSDOS_SEG_BASE + MSDOS_CODE_START + b] = savedMsdosCode[b];
                        Console.Error.WriteLine("[DOSINIT2-RELOC] Restored MSDOS code area (4240-E946)");
                    }
                    // Point INT 21h to our BIOS ROM handler. The 0C3C:0000 entry is
                    // the DOSINIT entry point (not INT 21h handler), and 0060:3673 is
                    // corrupted. Our DosBios implements the necessary file I/O.
                    retMem[0x84] = 0x00; retMem[0x85] = 0x00; // offset 0x0000
                    retMem[0x86] = 0x21; retMem[0x87] = 0xE8; // segment 0xE821 → phys 0xE8210
                    Console.Error.WriteLine("[DOSINIT2-RELOC] Fixed INT 21h IVT → E821:0000 (BIOS ROM handler)");
                    // Fix INT 2Fh IVT → BIOS ROM handler at 0xE8220
                    retMem[0xBC] = 0x00; retMem[0xBD] = 0x00; // offset 0x0000
                    retMem[0xBE] = 0x22; retMem[0xBF] = 0xE8; // segment 0xE822 → phys 0xE8220
                    Console.Error.WriteLine("[DOSINIT2-RELOC] Fixed INT 2Fh IVT → E822:0000 (BIOS ROM handler)");

                    // Step 2: Inject mini SYSINIT AFTER restores (0DA0:0600 = 0C3C:1C40
                    // overlaps with the header area we just restored, so write it last).
                    // Place code + data at a high offset in segment 0DA0 that doesn't
                    // overlap with 0C3C:0000-423F. 0DA0:4300 = phys 0x11D00 is safe.
                    int miniBase = 0x4300;
                    int miniPhys = dstBase + miniBase;

                    // Write COMMAND.COM path
                    string cmdPath = "A:\\COMMAND.COM\0";
                    for (int i = 0; i < cmdPath.Length; i++)
                        retMem[miniPhys + 0x80 + i] = (byte)cmdPath[i];

                    // Write empty command tail: length=0, CR
                    retMem[miniPhys + 0xC0] = 0x00;
                    retMem[miniPhys + 0xC1] = 0x0D;

                    // Write parameter block
                    int pb = miniPhys + 0xA0;
                    retMem[pb + 0] = 0x00; retMem[pb + 1] = 0x00; // env = 0
                    retMem[pb + 2] = (byte)((miniBase + 0xC0) & 0xFF);
                    retMem[pb + 3] = (byte)((miniBase + 0xC0) >> 8); // offset
                    retMem[pb + 4] = 0xA0; retMem[pb + 5] = 0x0D; // segment 0DA0
                    retMem[pb + 6] = 0x5C; retMem[pb + 7] = 0x00;
                    retMem[pb + 8] = 0xA0; retMem[pb + 9] = 0x0D;
                    retMem[pb + 10] = 0x6C; retMem[pb + 11] = 0x00;
                    retMem[pb + 12] = 0xA0; retMem[pb + 13] = 0x0D;

                    // Write code
                    ushort pathOff = (ushort)(miniBase + 0x80);
                    ushort paramOff = (ushort)(miniBase + 0xA0);
                    byte[] miniSysinit = {
                        0xFC,                   // CLD
                        0xB8, 0xA0, 0x0D,       // MOV AX, 0DA0h
                        0x8E, 0xD8,             // MOV DS, AX
                        0x8E, 0xC0,             // MOV ES, AX
                        0xB4, 0x0E,             // MOV AH, 0Eh (set default drive)
                        0xB2, 0x00,             // MOV DL, 0 (drive A:)
                        0xCD, 0x21,             // INT 21h
                        0xBC, 0x00, 0x04,       // MOV SP, 0400h
                        (byte)0xBA, (byte)(pathOff & 0xFF), (byte)(pathOff >> 8), // MOV DX, pathOff
                        (byte)0xBB, (byte)(paramOff & 0xFF), (byte)(paramOff >> 8), // MOV BX, paramOff
                        0xB8, 0x00, 0x4B,       // MOV AX, 4B00h (EXEC)
                        0xCD, 0x21,             // INT 21h
                        0xF4,                   // HLT
                    };
                    for (int i = 0; i < miniSysinit.Length; i++)
                        retMem[miniPhys + i] = miniSysinit[i];

                    _cpu.CS = 0x0DA0;
                    _cpu.IP = (ushort)miniBase;
                    _cpu.SS = 0x0DA0;
                    _cpu.SP = 0x0400;

                    Console.Error.WriteLine($"[DOSINIT2-RELOC] Injected mini SYSINIT at 0DA0:{miniBase:X4} → EXEC A:\\COMMAND.COM");

                    sysinitActive = true;
                    sysinitTraceCount = 0;
                }

                // After DOSINIT's second call REP MOVSB overwrites the IVT,
                // restore critical BIOS handler IVT entries that the kernel needs.
                // DOSINIT writes its own IVT template which may have stale/wrong values
                // for hardware BIOS handlers. Re-patch them to our ROM handlers.
                // Continuously ensure critical BIOS IVT entries point to our ROM handlers.
                // DOSINIT's second call (and subsequent kernel init) overwrites the IVT
                // multiple times with kernel-internal pointers. The disk and CRT BIOS
                // handlers MUST point to our ROM stubs for hardware access to work.
                // Guard IVT restoration: only during DOSINIT2 phase, NOT after SYSINIT.
                // SYSINIT sets up the final DOS IVT. Continuous restoration breaks DOS
                // data structures (e.g., DPB chain pointer at 0000:0004 = IVT INT 01h slot).
                if (dosinitSecondCall && !sysinitActive) // stop once SYSINIT takes over
                {
                    var ivtMem = _bus.GetMemoryDirect();
                    ushort ivt1bOff = (ushort)(ivtMem[0x6C] | (ivtMem[0x6D] << 8));
                    ushort ivt1bSeg = (ushort)(ivtMem[0x6E] | (ivtMem[0x6F] << 8));
                    int ivt1bPhys = (ivt1bSeg << 4) + ivt1bOff;
                    if (ivt1bPhys != 0xE8030 && ivt1bPhys < 0xE0000)
                    {
                        // Restore critical IVT entries. Disk reads to ES:BP=0000:0000
                        // corrupt the first 256 bytes of memory (IVT area).
                        // DO NOT restore INT 00h-07h: DOS kernel uses those IVT slots
                        // for internal data (e.g., INT 01h at 0004 = DPB chain pointer).

                        // INT 08h-17h (hardware IRQs) → IRQ_STUB_BASE + (i+8)*16
                        for (int i = 0; i < 16; i++)
                        {
                            int stubPhys = 0xE8060 + (i + 8) * 16;
                            ushort stubOff = (ushort)(stubPhys & 0xF);
                            ushort stubSeg = (ushort)((stubPhys >> 4) & 0xFFFF);
                            int ivtAddr = (0x08 + i) * 4;
                            ivtMem[ivtAddr] = (byte)stubOff;
                            ivtMem[ivtAddr + 1] = (byte)(stubOff >> 8);
                            ivtMem[ivtAddr + 2] = (byte)stubSeg;
                            ivtMem[ivtAddr + 3] = (byte)(stubSeg >> 8);
                        }
                        // INT 18h (CRT) at 0x60 → E800:0000
                        ivtMem[0x60] = 0x00; ivtMem[0x61] = 0x00;
                        ivtMem[0x62] = 0x00; ivtMem[0x63] = 0xE8;
                        // INT 19h (Keyboard) at 0x64 → E801:0000
                        ivtMem[0x64] = 0x00; ivtMem[0x65] = 0x00;
                        ivtMem[0x66] = 0x01; ivtMem[0x67] = 0xE8;
                        // INT 1Ah (Timer) at 0x68 → E802:0000
                        ivtMem[0x68] = 0x00; ivtMem[0x69] = 0x00;
                        ivtMem[0x6A] = 0x02; ivtMem[0x6B] = 0xE8;
                        // INT 1Bh (Disk) at 0x6C → E803:0000
                        ivtMem[0x6C] = 0x00; ivtMem[0x6D] = 0x00;
                        ivtMem[0x6E] = 0x03; ivtMem[0x6F] = 0xE8;
                        // INT 21h (DOS) at 0x84 → 0060:3673 (IO.SYS DOS stub dispatcher)
                        ivtMem[0x84] = 0x73; ivtMem[0x85] = 0x36;
                        ivtMem[0x86] = 0x60; ivtMem[0x87] = 0x00;
                    }

                    // Repair critical BDA values that DOSINIT2 overwrites
                    // BDA[0584] = boot DA/UA (0x80 for SASI HDD unit 0)
                    if (ivtMem[0x0584] != 0x80)
                        ivtMem[0x0584] = 0x80;
                    // BDA[0402] = equipment flags (bit 0 = SASI HDD present)
                    if ((ivtMem[0x0402] & 0x01) == 0)
                        ivtMem[0x0402] |= 0x01;
                    // BDA[055D] = HDD presence bitmap (both SASI and IDE bits)
                    if (ivtMem[0x055D] != 0x01)
                        ivtMem[0x055D] = 0x01;
                    // BDA[04BC] = HDD BIOS work flag (non-zero = HDDs present)
                    if (ivtMem[0x04BC] == 0x00)
                        ivtMem[0x04BC] = 0x01;
                }

                // Dump MSDOS.SYS and save code area when CS first enters 0C3C
                // This fires BEFORE the first DOSINIT call, so the MSDOS.SYS code
                // (including the JMP at 1060:0000) is still intact.
                if (!msdosDumped && _cpu.CS == 0x0C3C)
                {
                    msdosDumped = true;
                    // Save MSDOS.SYS CODE area BEFORE DOSINIT zeroes its BSS
                    var smemCode = _bus.GetMemoryDirect();
                    int codeSize = MSDOS_SAVE_END - MSDOS_CODE_START;
                    savedMsdosCode = new byte[codeSize];
                    for (int b = 0; b < codeSize; b++)
                        savedMsdosCode[b] = smemCode[MSDOS_SEG_BASE + MSDOS_CODE_START + b];
                    Console.Error.Write($"[MSDOS-SAVE] Code@4240 first 32 bytes: ");
                    for (int b = 0; b < 32; b++)
                        Console.Error.Write($"{savedMsdosCode[b]:X2} ");
                    Console.Error.WriteLine();
                    msdosTracing = true;
                    var dm = _bus.GetMemoryDirect();
                    Console.Error.WriteLine($"\n[MSDOS-DUMP] First entry to 0C3C at IP={_cpu.IP:X4} step={stepCount}");
                    Console.Error.WriteLine($"[MSDOS-DUMP] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} SS={_cpu.SS:X4} SP={_cpu.SP:X4}");
                    // Dump dispatch table and MSDOS.SYS header to find original DOSINIT entry
                    Console.Error.Write($"[MSDOS-DUMP] Dispatch 0C3C:0000: ");
                    for (int b = 0; b < 32; b++)
                        Console.Error.Write($"{dm[0x0C3C0 + b]:X2} ");
                    Console.Error.WriteLine();
                    Console.Error.Write($"[MSDOS-DUMP] Header@10000: ");
                    for (int b = 0; b < 32; b++)
                        Console.Error.Write($"{dm[0x10000 + b]:X2} ");
                    Console.Error.WriteLine();
                    Console.Error.Write($"[MSDOS-DUMP] MSDOS code@10600: ");
                    for (int b = 0; b < 32; b++)
                        Console.Error.Write($"{dm[0x10600 + b]:X2} ");
                    Console.Error.WriteLine();
                    // Dump zero-on-disk areas: MSDOS.SYS offsets 0x9000-0x96FF
                    // Physical: 0x10600 + offset = MSDOS.SYS file offset
                    // In segment 0C3C: 0C3C:0000 = phys 0x0C3C0, MSDOS.SYS starts at 0C3C:4240 = phys 0x10600
                    // So MSDOS.SYS offset X = 0C3C:(4240+X) = phys (0x10600+X)
                    // Zero area at MSDOS offset 0x9000 = 0C3C:D240 = phys 0x19600
                    int[] dumpOffsets = { 0xD240, 0xD300, 0xD320, 0xD340, 0xD700, 0xD740, 0xD760, 0xD780, 0xD7A0, 0xED00, 0xEDC0, 0xEDE0 };
                    foreach (int doff in dumpOffsets)
                    {
                        int pa = 0x0C3C0 + doff;
                        Console.Error.Write($"[MSDOS-DUMP] 0C3C:{doff:X4} (phys {pa:X5}): ");
                        for (int b = 0; b < 32 && pa + b < dm.Length; b++)
                            Console.Error.Write($"{dm[pa + b]:X2} ");
                        Console.Error.WriteLine();
                    }
                    // Summary: count non-zero bytes in MSDOS.SYS zero-on-disk range
                    int nz1 = 0, nz2 = 0, nz3 = 0;
                    for (int i = 0; i < 0x500; i++) // offset 0x9000-0x94FF
                        if (dm[0x19600 + i] != 0) nz1++;
                    for (int i = 0; i < 0x100; i++) // offset 0x9500-0x95FF
                        if (dm[0x19B00 + i] != 0) nz2++;
                    for (int i = 0; i < 0x500; i++) // offset 0x9560-0xA05F
                        if (dm[0x19B60 + i] != 0) nz3++;
                    Console.Error.WriteLine($"[MSDOS-DUMP] Zero-area fill: 9000-94FF={nz1}/1280, 9500-95FF={nz2}/256, 9560-9A5F={nz3}/1280");
                }

                // Trace instructions in MSDOS.SYS zero-on-disk areas (0C3C:D200+)
                // Stop tracing once we enter zeros (to avoid log flood)
                if (msdosTracing && _cpu.CS == 0x0C3C && _cpu.IP >= 0xD200)
                {
                    var dm = _bus.GetMemoryDirect();
                    int pa = 0x0C3C0 + _cpu.IP;
                    if (dm[pa] != 0x00) // only trace non-zero code
                    {
                        Console.Error.Write($"[MSDOS-TRACE] #{stepCount} 0C3C:{_cpu.IP:X4} [");
                        for (int b = 0; b < 8 && pa + b < dm.Length; b++)
                            Console.Error.Write($"{dm[pa + b]:X2} ");
                        Console.Error.WriteLine($"] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} SP={_cpu.SP:X4} SI={_cpu.SI:X4} DI={_cpu.DI:X4}");
                    }
                }

                // Detect MSDOS.SYS code leaving valid range
                // MSDOS.SYS in segment 0C3C: valid IO.SYS area 0x0000-0x3A3F, MSDOS.SYS 0x4240-0xE23F
                if (!hangDetected && _cpu.CS == 0x0C3C && _cpu.IP > 0xE23F)
                {
                    hangDetected = true;
                    var dm = _bus.GetMemoryDirect();
                    int pa = (_cpu.CS << 4) + _cpu.IP;
                    Console.Error.WriteLine($"\n[MSDOS-OOB] IP beyond MSDOS.SYS at {_cpu.CS:X4}:{_cpu.IP:X4} (phys 0x{pa:X5}) step={stepCount}");
                    Console.Error.WriteLine($"[MSDOS-OOB] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} SS={_cpu.SS:X4} SP={_cpu.SP:X4}");
                    Console.Error.Write($"[MSDOS-OOB] Code at IP: ");
                    for (int b = 0; b < 32 && pa + b < dm.Length; b++)
                        Console.Error.Write($"{dm[pa + b]:X2} ");
                    Console.Error.WriteLine();
                    // Dump stack
                    int stk = (_cpu.SS << 4) + _cpu.SP;
                    Console.Error.Write("[MSDOS-OOB] Stack: ");
                    for (int s = 0; s < 16 && stk + s * 2 < dm.Length; s++)
                        Console.Error.Write($"{(ushort)(dm[stk + s*2] | (dm[stk + s*2+1] << 8)):X4} ");
                    Console.Error.WriteLine();
                    // Dump last trace entries
                    Console.Error.WriteLine("[MSDOS-OOB] Last 500 trace entries:");
                    int start = (traceIdx + traceSize - 500) % traceSize;
                    for (int t = 0; t < 500; t++)
                    {
                        var e = traceRing[(start + t) % traceSize];
                        if (e.cs == 0 && e.ip == 0) continue;
                        Console.Error.WriteLine($"  {e.cs:X4}:{e.ip:X4} AX={e.ax:X4} BX={e.bx:X4} CX={e.cx:X4} DX={e.dx:X4} DS={e.ds:X4} ES={e.es:X4} SP={e.sp:X4} FL={e.flags:X4} SI={e.si:X4} DI={e.di:X4} BP={e.bp:X4}");
                    }
                    // Check IO.SYS gap: physical 0xFE00-0xFFFF (0060:F800-0060:F9FF)
                    Console.Error.Write("[MSDOS-OOB] IO.SYS gap (0060:F800-F80F): ");
                    for (int b = 0; b < 16; b++)
                        Console.Error.Write($"{dm[0x600 + 0xF800 + b]:X2} ");
                    Console.Error.WriteLine();
                }

                // Detect entry to loaded code segments and dump memory
                if (!hangDetected && _cpu.CS >= 0x2000 && _cpu.CS < 0xA000 && prevCS < 0x2000)
                {
                    var gameMem = _bus.GetMemoryDirect();
                    int gamePhys = (_cpu.CS << 4) + _cpu.IP;
                    Console.Error.WriteLine($"\n[GAME-ENTRY] {_cpu.CS:X4}:{_cpu.IP:X4} (phys 0x{gamePhys:X5}) step={stepCount}");
                    Console.Error.WriteLine($"[GAME-ENTRY] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} SS={_cpu.SS:X4} SP={_cpu.SP:X4}");

                    // Check if area has valid code (non-zero)
                    int nonZero = 0;
                    int checkStart = _cpu.CS << 4;
                    for (int i = 0; i < 0x10000 && checkStart + i < gameMem.Length; i++)
                        if (gameMem[checkStart + i] != 0) nonZero++;

                    // SYSINIT relocation fix: DOSINIT2 returns to segment 2000 expecting
                    // SYSINIT code there, but IO.SYS was loaded to physical 0x600 (segment 0060)
                    // and SYSINIT was never relocated. Copy entire IO.SYS to target segment.
                    // Check if code at entry point is zeros (SYSINIT not relocated)
                    bool entryIsZero = true;
                    for (int b = 0; b < 16 && gamePhys + b < gameMem.Length; b++)
                        if (gameMem[gamePhys + b] != 0) { entryIsZero = false; break; }

                    Console.Error.WriteLine($"[GAME-ENTRY] entryIsZero={entryIsZero} dosinitSecondCall={dosinitSecondCall} sysinitActive={sysinitActive}");
                    if (entryIsZero && dosinitSecondCall && !sysinitActive)
                    {
                        int dstBase = _cpu.CS << 4;
                        // IO.SYS in memory is corrupted by DOSINIT2, reload from disk
                        var hddForReloc = _diskManager?.GetHDD(0);
                        if (hddForReloc != null)
                        {
                            int ioSysLBA = 460; // IO.SYS starts at LBA 460
                            int sectorCount = 248;
                            byte[] sectorBuf = new byte[hddForReloc.SectorSize];
                            // Reload IO.SYS to target segment (for SYSINIT at 2000:xxxx)
                            Console.Error.WriteLine($"[SYSINIT-RELOC] Reloading IO.SYS from disk ({sectorCount} sectors @ LBA {ioSysLBA}) to 0x{dstBase:X5}");
                            for (int s = 0; s < sectorCount; s++)
                            {
                                int lba = ioSysLBA + s;
                                int c = lba / (8 * 33);
                                int h = (lba / 33) % 8;
                                int sec = (lba % 33) + 1;
                                if (hddForReloc.ReadSector(c, h, sec, sectorBuf))
                                {
                                    for (int b = 0; b < hddForReloc.SectorSize && dstBase + s * hddForReloc.SectorSize + b < gameMem.Length; b++)
                                        gameMem[dstBase + s * hddForReloc.SectorSize + b] = sectorBuf[b];
                                }
                            }

                            // Restore the FULL IO.SYS from disk. The device driver code
                            // (segment 0636, offsets 0x1CFE-0x43BB+) at IO.SYS offset 0x5D60+
                            // gets corrupted with 0x20 (spaces) during DOSINIT/SYSINIT init.
                            // Only the first 0xE5B bytes were restored previously, but the
                            // device driver code at higher offsets also needs restoration.
                            // Save runtime-initialized data tables before the full restore.
                            int origBase = 0x600;

                            // Save runtime data that was filled during init (before restoring from disk)
                            int bpbTableOff = 0x2B4E; // BPB table at 0060:2B4E (13 bytes)
                            int driveTypeOff = 0x2C86; // drive type table at 0060:2C86 (60 bytes)
                            int sectorSizeOff = 0x27C4; // sector size at 0060:27C4 (2 bytes)
                            byte[] savedBpb = new byte[13];
                            byte[] savedDriveTypes = new byte[60];
                            byte[] savedSectorSize = new byte[2];
                            Array.Copy(gameMem, origBase + bpbTableOff, savedBpb, 0, 13);
                            Array.Copy(gameMem, origBase + driveTypeOff, savedDriveTypes, 0, 60);
                            Array.Copy(gameMem, origBase + sectorSizeOff, savedSectorSize, 0, 2);

                            // Restore IO.SYS from disk, but SKIP the first 0xE5C bytes
                            // (0060:0000-0x0E5B) which DOSINIT intentionally overwrote with
                            // DOS kernel data (IVT, BDA, NUL device header, MCBs, INT 20/21).
                            int dosinitProtect = 0xE5C; // bytes of IO.SYS overwritten by DOSINIT
                            int skipSectors = dosinitProtect / hddForReloc.SectorSize; // first N sectors to skip
                            int corruptEnd = sectorCount * hddForReloc.SectorSize;
                            int sectorsToRestore = sectorCount - skipSectors;
                            Console.Error.WriteLine($"[SYSINIT-RELOC] Restoring IO.SYS offsets 0x{skipSectors * hddForReloc.SectorSize:X4}-0x{corruptEnd-1:X4} ({sectorsToRestore} sectors, skipping first {skipSectors}) to 0x{origBase:X5}");
                            for (int s = skipSectors; s < sectorCount; s++)
                            {
                                int lba = ioSysLBA + s;
                                int c2 = lba / (8 * 33);
                                int h2 = (lba / 33) % 8;
                                int s2 = (lba % 33) + 1;
                                if (hddForReloc.ReadSector(c2, h2, s2, sectorBuf))
                                {
                                    int memOff = origBase + s * hddForReloc.SectorSize;
                                    int copyLen = Math.Min(hddForReloc.SectorSize, corruptEnd - s * hddForReloc.SectorSize);
                                    for (int b = 0; b < copyLen && memOff + b < gameMem.Length; b++)
                                        gameMem[memOff + b] = sectorBuf[b];
                                }
                            }
                            // Restore runtime-initialized data tables
                            Array.Copy(savedBpb, 0, gameMem, origBase + bpbTableOff, 13);
                            Array.Copy(savedDriveTypes, 0, gameMem, origBase + driveTypeOff, 60);
                            Array.Copy(savedSectorSize, 0, gameMem, origBase + sectorSizeOff, 2);
                            Console.Error.WriteLine("[SYSINIT-RELOC] Runtime data tables preserved across full IO.SYS restore");

                            // Dump restored jump table entries to verify
                            Console.Error.Write("[SYSINIT-RELOC] Jump table @0060: ");
                            for (int jtOff = 0x1C8; jtOff <= 0x20C; jtOff += 4)
                            {
                                ushort off2 = (ushort)(gameMem[origBase + jtOff] | (gameMem[origBase + jtOff + 1] << 8));
                                ushort seg2 = (ushort)(gameMem[origBase + jtOff + 2] | (gameMem[origBase + jtOff + 3] << 8));
                                Console.Error.Write($"[{jtOff:X3}]={seg2:X4}:{off2:X4} ");
                            }
                            Console.Error.WriteLine();
                        }
                        Console.Error.Write($"[SYSINIT-RELOC] Code at {_cpu.CS:X4}:{_cpu.IP:X4}: ");
                        for (int b = 0; b < 32; b++)
                            Console.Error.Write($"{gameMem[gamePhys + b]:X2} ");
                        Console.Error.WriteLine();

                        // Patch the delay function at IO.SYS offset AAFD in BOTH segments.
                        // Original: PUSH CX; XOR CX,CX; IN AL,82; AND AL,DH; CMP AL,DL; LOOPNZ; POP CX; RET
                        // The delay waits for port 82 status bits (calendar IC / hardware init).
                        // Without full hardware emulation, these loops time out and trigger
                        // a destructive stack unwind at AAA3 that crashes into IO.SYS data.
                        // Patch: PUSH CX; CMP AL,AL; POP CX; RET (always succeeds with ZF=1)
                        byte[] delayPatch = { 0x51, 0x38, 0xC0, 0x59, 0xC3 };
                        foreach (int segBase in new[] { 0x600, dstBase })
                        {
                            int patchAddr = segBase + 0xAAFD;
                            for (int b = 0; b < delayPatch.Length && patchAddr + b < gameMem.Length; b++)
                                gameMem[patchAddr + b] = delayPatch[b];
                        }
                        Console.Error.WriteLine("[SYSINIT-RELOC] Patched delay function at AAFD (always-succeed)");

                        // Register BIOS handlers at both stub addresses
                        int stub0060 = 0x600 + 0x3673; // 0x3C73
                        int stub2000 = dstBase + 0x3673; // 0x23673
                        _bios.ActivateInt21(); // Re-registers at 0060:3673
                        _bios.RegisterDosStubAt(stub2000);
                        Console.Error.WriteLine($"[SYSINIT-RELOC] Dispatch ptrs at 0060 & {_cpu.CS:X4} → E821:0000, stubs at 0x{stub0060:X5} & 0x{stub2000:X5}");

                        // Repair BDA
                        gameMem[0x0584] = 0x80; // boot DA/UA
                        gameMem[0x0402] |= 0x01; // SASI HDD present
                        gameMem[0x055D] = 0x01; // HDD bitmap (SASI only, no IDE/SCSI)
                        gameMem[0x04BC] = 0x01; // HDD work flag

                        // Enable SYSINIT execution tracing
                        sysinitActive = true;
                        sysinitTraceCount = 0;
                        Console.Error.WriteLine("[SYSINIT-RELOC] SYSINIT tracing enabled");
                    }
                    else
                    {
                        Console.Error.WriteLine($"[GAME-ENTRY] Non-zero bytes in segment: {nonZero}/65536");
                        Console.Error.Write("[GAME-ENTRY] Code: ");
                        for (int b = 0; b < 32 && gamePhys + b < gameMem.Length; b++)
                            Console.Error.Write($"{gameMem[gamePhys + b]:X2} ");
                        Console.Error.WriteLine();
                    }
                }

                // Detect error handler entry at 0636:43BB
                if (!hangDetected && _cpu.CS == 0x0636 && _cpu.IP == 0x43BB)
                {
                    Console.Error.WriteLine($"\n[ERROR-HANDLER] Entered 0636:43BB after {stepCount} steps");
                    Console.Error.WriteLine($"[ERROR-HANDLER] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} SS={_cpu.SS:X4} SP={_cpu.SP:X4}");
                    var errMem = _bus.GetMemoryDirect();
                    int errSp = (_cpu.SS << 4) + _cpu.SP;
                    ushort stkIP = (ushort)(errMem[errSp] | (errMem[errSp + 1] << 8));
                    ushort stkCS = (ushort)(errMem[errSp + 2] | (errMem[errSp + 3] << 8));
                    int intByteAddr = (stkCS << 4) + stkIP - 1;
                    byte intByte = (intByteAddr >= 0 && intByteAddr < errMem.Length) ? errMem[intByteAddr] : (byte)0xFF;
                    Console.Error.WriteLine($"[ERROR-HANDLER] Stacked={stkCS:X4}:{stkIP:X4} [BX-1]={intByte:X2}");
                    // Code at INT site
                    int intSite = (stkCS << 4) + stkIP - 4;
                    Console.Error.Write($"[ERROR-HANDLER] Code: ");
                    for (int b = 0; b < 12 && intSite + b >= 0 && intSite + b < errMem.Length; b++)
                        Console.Error.Write($"{errMem[intSite + b]:X2} ");
                    Console.Error.WriteLine();
                    // IVT scan for entries near 0060:3670 or 0636:43BB
                    Console.Error.Write("[ERROR-HANDLER] IVT: ");
                    for (int v = 0; v < 256; v++)
                    {
                        ushort vOff = (ushort)(errMem[v * 4] | (errMem[v * 4 + 1] << 8));
                        ushort vSeg = (ushort)(errMem[v * 4 + 2] | (errMem[v * 4 + 3] << 8));
                        if ((vSeg == 0x0636 && vOff == 0x43BB) || (vSeg == 0x0060 && vOff >= 0x3670 && vOff <= 0x3680))
                            Console.Error.Write($"[{v:X2}]={vSeg:X4}:{vOff:X4} ");
                    }
                    Console.Error.WriteLine();
                }
                // Detect when 0060:3673 handler is entered
                if (!hangDetected && _cpu.CS == 0x0060 && _cpu.IP == 0x3673)
                {
                    var hMem = _bus.GetMemoryDirect();
                    int hSp = (_cpu.SS << 4) + _cpu.SP;
                    ushort hIP = (ushort)(hMem[hSp] | (hMem[hSp + 1] << 8));
                    ushort hCS = (ushort)(hMem[hSp + 2] | (hMem[hSp + 3] << 8));
                    int hIntAddr = (hCS << 4) + hIP - 1;
                    byte hIntNum = (hIntAddr >= 0 && hIntAddr < hMem.Length) ? hMem[hIntAddr] : (byte)0xFF;
                    Console.Error.WriteLine($"[DOS-INT] 0060:3673 stacked={hCS:X4}:{hIP:X4} INT#{hIntNum:X2} AX={_cpu.AX:X4}");
                    // Dump handler code
                    int hAddr = 0x00600 + 0x3673;
                    Console.Error.Write("[DOS-INT] Code at 0060:3673: ");
                    for (int b = 0; b < 32 && hAddr + b < hMem.Length; b++)
                        Console.Error.Write($"{hMem[hAddr + b]:X2} ");
                    Console.Error.WriteLine();
                    // Also dump the IVT[21h] to see if it's set up
                    Console.Error.WriteLine($"[DOS-INT] IVT[21]={hMem[0x84]:X2}{hMem[0x85]:X2}:{hMem[0x86]:X2}{hMem[0x87]:X2}");
                }

                // Detect wild execution into BIOS ROM (non-handler addresses)
                if (!hangDetected)
                {
                    int wildPhys = (_cpu.CS << 4) + _cpu.IP;
                    wildPhys &= 0xFFFFF;
                    if (wildPhys >= 0xE8000 && wildPhys <= 0xFFFFF)
                    {
                        var mem4 = _bus.GetMemoryDirect();
                        if (mem4[wildPhys] == 0x00) // zeroed area = no valid code
                        {
                            hangDetected = true;
                            Console.Error.WriteLine($"\n[WILD] CPU at zeroed BIOS ROM: {_cpu.CS:X4}:{_cpu.IP:X4} (phys 0x{wildPhys:X5}) after {stepCount} steps");
                            Console.Error.WriteLine($"[WILD] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} SS={_cpu.SS:X4} SP={_cpu.SP:X4}");
                            // Dump bytes at last traced instruction to see the opcode
                            var lastE = traceRing[(traceIdx + traceSize - 1) % traceSize];
                            int lastPhys = (lastE.cs << 4) + lastE.ip;
                            Console.Error.Write($"[WILD] Last instr at {lastE.cs:X4}:{lastE.ip:X4} bytes: ");
                            for (int b = 0; b < 8 && lastPhys + b < mem4.Length; b++)
                                Console.Error.Write($"{mem4[lastPhys + b]:X2} ");
                            Console.Error.WriteLine();
                            // Check IVT - scan for the wild destination
                            Console.Error.Write("[WILD] IVT scan for destination: ");
                            for (int iv = 0; iv < 256; iv++)
                            {
                                ushort ivOff = (ushort)(mem4[iv * 4] | (mem4[iv * 4 + 1] << 8));
                                ushort ivSeg = (ushort)(mem4[iv * 4 + 2] | (mem4[iv * 4 + 3] << 8));
                                if (ivSeg == _cpu.CS && ivOff == _cpu.IP)
                                    Console.Error.Write($"INT {iv:X2} ");
                            }
                            Console.Error.WriteLine();
                            Console.Error.WriteLine($"[WILD] Last {traceSize} instructions:");
                            for (int t = 0; t < traceSize; t++)
                            {
                                var e = traceRing[(traceIdx + t) % traceSize];
                                if (e.cs == 0 && e.ip == 0) continue;
                                Console.Error.WriteLine($"  {e.cs:X4}:{e.ip:X4} AX={e.ax:X4} BX={e.bx:X4} CX={e.cx:X4} DX={e.dx:X4} DS={e.ds:X4} ES={e.es:X4} SP={e.sp:X4} FL={e.flags:X4} SI={e.si:X4} DI={e.di:X4} BP={e.bp:X4}");
                            }
                            // Stack dump
                            int stackBase2 = (_cpu.SS << 4) + _cpu.SP;
                            Console.Error.Write("[WILD] Stack: ");
                            for (int s = 0; s < 16 && stackBase2 + s * 2 < mem4.Length; s++)
                            {
                                ushort w = (ushort)(mem4[stackBase2 + s * 2] | (mem4[stackBase2 + s * 2 + 1] << 8));
                                Console.Error.Write($"{w:X4} ");
                            }
                            Console.Error.WriteLine();
                        }
                    }
                }

                // Fix IO.SYS cluster normalization infinite loop at 0636:15A9.
                // The loop shifts SI right until SI==0x0100, but when the driver's
                // internal data at [DI+0E] is 0 (uninitialized after reboot), the
                // code at 15E2 sets SI=0 and enters the loop which never terminates.
                // Fix: set SI = bytes_per_cluster = bps * spc (e.g., 1024*8=8192=0x2000).
                // The loop will shift SI: 0x2000→0x1000→0x800→0x400→0x200→0x100 (5 shifts).
                // Also write the correct bytes_per_cluster value to [DI+0E].
                if (_cpu.CS == 0x0636 && _cpu.IP == 0x15A9 && _cpu.SI == 0)
                {
                    var fmem = _bus.GetMemoryDirect();
                    // Read BPB from 0060:2B4E: bytes-per-sector and sectors-per-cluster
                    int bpbBase = 0x600 + 0x2B4E;
                    ushort bps = (ushort)(fmem[bpbBase] | (fmem[bpbBase + 1] << 8));
                    byte spc = fmem[bpbBase + 2];
                    if (bps == 0) bps = 1024;
                    if (spc == 0) spc = 8;
                    ushort clusterSize = (ushort)(bps * spc); // 0x2000 = 8192
                    _cpu.SI = clusterSize;
                    // Write bytes_per_cluster to [DI+0E] so future calls skip normalization
                    int dpbAddr = 0x600 + _cpu.DI + 0x0E;
                    if (dpbAddr + 1 < fmem.Length)
                    {
                        fmem[dpbAddr] = (byte)(clusterSize & 0xFF);
                        fmem[dpbAddr + 1] = (byte)(clusterSize >> 8);
                    }
                    Console.Error.WriteLine($"[DPB-FIX] SI=0 → SI={clusterSize:X4} (bps={bps}*spc={spc}), [DI+0E]={clusterSize:X4}");
                }

                // Detect JMP $ (EB FE) or JMP $-1 (EB FD) infinite loop
                if (!hangDetected)
                {
                    var mem3 = _bus.GetMemoryDirect();
                    int pa = (_cpu.CS << 4) + _cpu.IP;
                    // Skip HLT+JMP pattern (F4 EB FD): this is a normal idle loop
                    // waiting for timer interrupt, not a hang.
                    bool isHltIdle = pa > 0 && mem3[pa - 1] == 0xF4 && mem3[pa] == 0xEB && mem3[pa + 1] == 0xFD;
                    if (pa >= 0 && pa + 1 < mem3.Length && mem3[pa] == 0xEB && (mem3[pa + 1] == 0xFE || mem3[pa + 1] == 0xFD) && !isHltIdle)
                    {
                        hangDetected = true;
                        Console.Error.WriteLine($"\n[HANG] Detected JMP $ at {_cpu.CS:X4}:{_cpu.IP:X4} after {stepCount} steps");
                        Console.Error.WriteLine($"[HANG] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} SS={_cpu.SS:X4} SP={_cpu.SP:X4} SI={_cpu.SI:X4} DI={_cpu.DI:X4} BP={_cpu.BP:X4} FL={_cpu.Flags.Value:X4}");
                        // Dump code around hang point
                        int csBase = _cpu.CS << 4;
                        Console.Error.WriteLine("[HANG] Code dump around halt:");
                        for (int off = (pa & ~0xF) - 0x80; off <= (pa & ~0xF) + 0xC0; off += 16)
                        {
                            if (off < csBase || off + 16 > mem3.Length) continue;
                            Console.Error.Write($"  {_cpu.CS:X4}:{off - csBase:X4}: ");
                            for (int b = 0; b < 16 && off + b < mem3.Length; b++)
                                Console.Error.Write($"{mem3[off + b]:X2} ");
                            Console.Error.WriteLine();
                        }
                        Console.Error.WriteLine($"[HANG] Last {traceSize} instructions:");
                        for (int t = 0; t < traceSize; t++)
                        {
                            var e = traceRing[(traceIdx + t) % traceSize];
                            if (e.cs == 0 && e.ip == 0) continue;
                            Console.Error.WriteLine($"  {e.cs:X4}:{e.ip:X4} AX={e.ax:X4} BX={e.bx:X4} CX={e.cx:X4} DX={e.dx:X4} DS={e.ds:X4} ES={e.es:X4} SP={e.sp:X4} FL={e.flags:X4} SI={e.si:X4} DI={e.di:X4} BP={e.bp:X4}");
                        }
                        // Stack dump
                        int stackBase = (_cpu.SS << 4) + _cpu.SP;
                        Console.Error.Write("[HANG] Stack: ");
                        for (int s = 0; s < 16 && stackBase + s * 2 < mem3.Length; s++)
                        {
                            ushort w = (ushort)(mem3[stackBase + s * 2] | (mem3[stackBase + s * 2 + 1] << 8));
                            Console.Error.Write($"{w:X4} ");
                        }
                        Console.Error.WriteLine();
                        // Dump code around the hang point (larger range)
                        int hangAddr = pa;
                        int segBase = _cpu.CS << 4;
                        // Dump code from segment:3F80 to segment:4420 (the CRT loop and halt handler)
                        int dumpStart2 = segBase + 0x3F80;
                        int dumpEnd2 = segBase + 0x4470;
                        Console.Error.Write($"[HANG] Code dump {_cpu.CS:X4}:3F80-4470:\n");
                        for (int off = dumpStart2; off < dumpEnd2 && off < mem3.Length; off += 16)
                        {
                            Console.Error.Write($"  {_cpu.CS:X4}:{off - segBase:X4}: ");
                            for (int b = 0; b < 16 && off + b < mem3.Length; b++)
                                Console.Error.Write($"{mem3[off + b]:X2} ");
                            Console.Error.WriteLine();
                        }
                        // Dump the CRT table data at the table pointer area
                        Console.Error.Write($"[HANG] Table area {_cpu.CS:X4}:4440-4470:\n");
                        int tableBase = segBase + 0x4440;
                        for (int off = tableBase; off < tableBase + 48 && off < mem3.Length; off += 16)
                        {
                            Console.Error.Write($"  {_cpu.CS:X4}:{off - segBase:X4}: ");
                            for (int b = 0; b < 16 && off + b < mem3.Length; b++)
                                Console.Error.Write($"{mem3[off + b]:X2} ");
                            Console.Error.WriteLine();
                        }
                        // Dump IVT entries for key interrupts
                        Console.Error.Write("[HANG] IVT: ");
                        for (int v = 0x18; v <= 0x21; v++)
                        {
                            ushort vOff = (ushort)(mem3[v * 4] | (mem3[v * 4 + 1] << 8));
                            ushort vSeg = (ushort)(mem3[v * 4 + 2] | (mem3[v * 4 + 3] << 8));
                            Console.Error.Write($"[{v:X2}]={vSeg:X4}:{vOff:X4} ");
                        }
                        Console.Error.WriteLine();
                    }
                }

                // Detect DOSINIT2 drive scan loop at 1060:D551-D570.
                // After the boot menu skip, DOSINIT2 enters a drive configuration
                // loop that scans BDA equipment flags. If the loop runs >2M steps
                // without exiting, dump state and force exit to E4F4.
                if (dosinitSecondCall && _cpu.CS == 0x1060 && !hangDetected)
                {
                    var lmem = _bus.GetMemoryDirect();
                    int lpa = V30.GetPhysicalAddress(_cpu.CS, _cpu.IP);
                    // Detect tight loop: JNZ at D55B that always branches
                    if (_cpu.IP >= 0xD540 && _cpu.IP <= 0xD5C0 && stepCount > 2000000)
                    {
                        if (!dosLoopPatched || stepCount > 3000000)
                        {
                            hangDetected = true;
                            Console.Error.WriteLine($"\n[LOOP-DETECT] Stuck at 1060:{_cpu.IP:X4} step={stepCount}");
                            Console.Error.WriteLine($"[LOOP-DETECT] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4} SS={_cpu.SS:X4} SP={_cpu.SP:X4} FL={_cpu.Flags.Value:X4}");
                            // Dump code D540-D5E0
                            Console.Error.Write("[LOOP-DETECT] Code 1060:D540: ");
                            int dumpBase = V30.GetPhysicalAddress(0x1060, 0xD540);
                            for (int b = 0; b < 160 && dumpBase + b < lmem.Length; b++)
                            {
                                if (b > 0 && b % 16 == 0)
                                {
                                    Console.Error.WriteLine();
                                    Console.Error.Write($"[LOOP-DETECT] Code 1060:{0xD540+b:X4}: ");
                                }
                                Console.Error.Write($"{lmem[dumpBase + b]:X2} ");
                            }
                            Console.Error.WriteLine();
                            // Dump referenced memory: BDA[0482], DS:[flags]
                            Console.Error.WriteLine($"[LOOP-DETECT] BDA[0482]={lmem[0x0482]:X2} BDA[0501]={lmem[0x0501]:X2} [0060:A1FC]={lmem[0x600+0xA1FC]:X2}{lmem[0x600+0xA1FD]:X2}");
                            // Dump last 200 trace entries
                            Console.Error.WriteLine("[LOOP-DETECT] Last 200 trace:");
                            int start = (traceIdx + traceSize - 200) % traceSize;
                            for (int t = 0; t < 200; t++)
                            {
                                var e = traceRing[(start + t) % traceSize];
                                if (e.cs == 0 && e.ip == 0) continue;
                                Console.Error.WriteLine($"  {e.cs:X4}:{e.ip:X4} AX={e.ax:X4} BX={e.bx:X4} CX={e.cx:X4} DX={e.dx:X4} DS={e.ds:X4} ES={e.es:X4} SP={e.sp:X4} FL={e.flags:X4} SI={e.si:X4} DI={e.di:X4} BP={e.bp:X4}");
                            }
                        }
                    }
                }

                // Check for pending interrupts
                if (_masterPic.HasInterrupt() && _cpu.Flags.IF)
                {
                    int vector = _masterPic.AcknowledgeInterrupt();
                    _cpu.Interrupt((byte)vector);
                }
            }

            // Frame timing
            if (_frameCycleAccumulator >= CyclesPerFrame)
            {
                _frameCycleAccumulator -= CyclesPerFrame;
                frameCount++;

                // Dump VRAM state after 60 frames (~1 second)
                if (frameCount == 60 && !vramDumped)
                {
                    vramDumped = true;
                    var mem = _bus.GetMemoryDirect();

                    // Check text VRAM (0xA0000-0xA1FFF)
                    int textNonZero = 0;
                    for (int i = 0xA0000; i < 0xA2000; i++)
                        if (mem[i] != 0) textNonZero++;

                    // Check text attribute VRAM (0xA2000-0xA3FFF)
                    int attrNonZero = 0;
                    for (int i = 0xA2000; i < 0xA4000; i++)
                        if (mem[i] != 0) attrNonZero++;

                    // Check GVRAM planes
                    int[] planeNonZero = new int[4];
                    int[] planeBase = { 0xA8000, 0xB0000, 0xB8000, 0xE0000 };
                    for (int p = 0; p < 4; p++)
                        for (int i = 0; i < 32000; i++)
                            if (mem[planeBase[p] + i] != 0) planeNonZero[p]++;

                    Console.Error.WriteLine($"[VRAM] Frame {frameCount}, CS:IP={_cpu.CS:X4}:{_cpu.IP:X4}");
                    Console.Error.WriteLine($"[VRAM] Text VRAM non-zero bytes: {textNonZero}/8192");
                    Console.Error.WriteLine($"[VRAM] Attr VRAM non-zero bytes: {attrNonZero}/8192");
                    Console.Error.WriteLine($"[VRAM] GVRAM Plane0(B) non-zero: {planeNonZero[0]}/32000");
                    Console.Error.WriteLine($"[VRAM] GVRAM Plane1(R) non-zero: {planeNonZero[1]}/32000");
                    Console.Error.WriteLine($"[VRAM] GVRAM Plane2(G) non-zero: {planeNonZero[2]}/32000");
                    Console.Error.WriteLine($"[VRAM] GVRAM Plane3(I) non-zero: {planeNonZero[3]}/32000");

                    // Dump first few text VRAM chars
                    if (textNonZero > 0)
                    {
                        Console.Error.Write("[VRAM] First text chars: ");
                        for (int i = 0; i < 80 && i < 160; i += 2)
                        {
                            ushort ch = (ushort)(mem[0xA0000 + i] | (mem[0xA0000 + i + 1] << 8));
                            if (ch != 0) Console.Error.Write($"[{i/2}]={ch:X4} ");
                        }
                        Console.Error.WriteLine();
                    }

                    // Check I/O port activity for GDC
                    Console.Error.WriteLine($"[VRAM] Text VRAM write count: {_bus.TextVramWriteCount}");
                    Console.Error.WriteLine($"[VRAM] GDC Text display: {_textGdc.DisplayEnabled}");
                    Console.Error.WriteLine($"[VRAM] GDC Gfx display: {_graphicsGdc.DisplayEnabled}");

                    // Dump PC-9821 identification bytes at 0xF8E80
                    Console.Error.Write("[BIOS] PC-9821 ID at F8E80: ");
                    for (int i = 0; i < 6; i++)
                        Console.Error.Write($"{mem[0xF8E80 + i]:X2} ");
                    Console.Error.WriteLine();
                    // Dump BDA boot DA/UA
                    Console.Error.WriteLine($"[BIOS] BDA[0584] boot DA/UA: {mem[0x0584]:X2}, BDA[0585]: {mem[0x0585]:X2}");
                    Console.Error.WriteLine($"[BIOS] BDA[0501] flags: {mem[0x0501]:X2}, BDA[0402] equip: {mem[0x0402]:X2}");
                    Console.Error.WriteLine($"[BIOS] BDA[055D] HDD bits: {mem[0x055D]:X2}");

                    // Dump attr VRAM contents to see what pattern was written
                    Console.Error.Write("[VRAM] Attr sample (first 40 chars): ");
                    for (int i = 0; i < 40; i++)
                    {
                        byte a = mem[0xA2000 + i * 2];
                        if (a != 0) Console.Error.Write($"[{i}]={a:X2} ");
                    }
                    Console.Error.WriteLine();
                }

                // Second diagnostic at frame 300 (~5 sec)
                if (frameCount == 300)
                {
                    var mem2 = _bus.GetMemoryDirect();
                    int textNZ2 = 0, attrNZ2 = 0;
                    for (int i = 0xA0000; i < 0xA2000; i++) if (mem2[i] != 0) textNZ2++;
                    for (int i = 0xA2000; i < 0xA4000; i++) if (mem2[i] != 0) attrNZ2++;
                    Console.Error.WriteLine($"[VRAM2] Frame {frameCount}, CS:IP={_cpu.CS:X4}:{_cpu.IP:X4}");
                    Console.Error.WriteLine($"[VRAM2] Text={textNZ2}/8192 Attr={attrNZ2}/8192");
                    Console.Error.WriteLine($"[VRAM2] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} ES={_cpu.ES:X4}");

                    // Dump bytes at CS:IP to see what instruction is executing
                    int physAddr2 = (_cpu.CS << 4) + _cpu.IP;
                    Console.Error.Write($"[VRAM2] Bytes at CS:IP: ");
                    for (int i = 0; i < 16 && physAddr2 + i < mem2.Length; i++)
                        Console.Error.Write($"{mem2[physAddr2 + i]:X2} ");
                    Console.Error.WriteLine();

                    // Dump text VRAM as readable rows (only non-empty rows)
                    for (int tr = 0; tr < 25; tr++)
                    {
                        bool hasContent = false;
                        for (int tc = 0; tc < 80; tc++)
                        {
                            ushort ch2 = (ushort)(mem2[0xA0000 + (tr * 80 + tc) * 2] | (mem2[0xA0000 + (tr * 80 + tc) * 2 + 1] << 8));
                            if (ch2 != 0x0020 && ch2 != 0x0000) { hasContent = true; break; }
                        }
                        if (!hasContent) continue;
                        Console.Error.Write($"[VRAM2] Row{tr:D2}: ");
                        for (int tc = 0; tc < 80; tc++)
                        {
                            ushort ch2 = (ushort)(mem2[0xA0000 + (tr * 80 + tc) * 2] | (mem2[0xA0000 + (tr * 80 + tc) * 2 + 1] << 8));
                            if (ch2 >= 0x20 && ch2 < 0x7F)
                                Console.Error.Write((char)ch2);
                            else if (ch2 == 0x0020 || ch2 == 0x0000)
                                Console.Error.Write(' ');
                            else
                                Console.Error.Write($"<{ch2:X2}>");
                        }
                        Console.Error.WriteLine();
                    }

                    // Dump last 200 trace entries at frame 300 for debugging
                    Console.Error.WriteLine("[VRAM2] Last 50 trace:");
                    int trStart = (traceIdx + traceSize - 50) % traceSize;
                    for (int t = 0; t < 50; t++)
                    {
                        var e = traceRing[(trStart + t) % traceSize];
                        if (e.cs == 0 && e.ip == 0) continue;
                        Console.Error.WriteLine($"  {e.cs:X4}:{e.ip:X4} AX={e.ax:X4} BX={e.bx:X4} CX={e.cx:X4} DX={e.dx:X4} DS={e.ds:X4} ES={e.es:X4} SP={e.sp:X4} FL={e.flags:X4} SI={e.si:X4} DI={e.di:X4}");
                    }

                    // Save DPB/driver data after initial boot reaches A> prompt.
                    // DI=0ABC is the IO.SYS block device driver's internal DPB-like
                    // structure at DS=0060. Save it so reboot can restore it.
                    if (!dpbSaved)
                    {
                        var dmem = _bus.GetMemoryDirect();
                        int dpbPhys = 0x600 + 0x0ABC;
                        savedDpb = new byte[256]; // Save 256 bytes of driver internal data
                        Array.Copy(dmem, dpbPhys, savedDpb, 0, 256);
                        dpbSaved = true;
                        Console.Error.Write("[DPB-SAVE] Driver data at 0060:0ABC (+0E=");
                        Console.Error.Write($"{dmem[dpbPhys + 0x0E]:X2}{dmem[dpbPhys + 0x0F]:X2}): ");
                        for (int b = 0; b < 32; b++)
                            Console.Error.Write($"{savedDpb[b]:X2} ");
                        Console.Error.WriteLine();
                    }
                }

                if (hasDisplay)
                {
                    // Suppress rendering until boot menu cleanup is done
                    if (!bootMenuVramDirty)
                    {
                        _display!.RenderFrame();
                    }
                    if (!_display!.PollEvents())
                        quit = true;
                }

                // Handle HDI selection from menu (reboot overrides quit)
                if (pendingHdiPath != null)
                {
                    string hdiPath = pendingHdiPath;
                    pendingHdiPath = null;
                    try
                    {
                        byte[] data = File.ReadAllBytes(hdiPath);
                        string ext = Path.GetExtension(hdiPath).ToLowerInvariant().TrimStart('.');
                        Console.Error.WriteLine($"[HDI-MOUNT] Loading as drive B: {Path.GetFileName(hdiPath)} ({data.Length} bytes)");

                        // Mount as HDD 1 (drive B:) — keep existing DOS environment on HDD 0
                        LoadHardDisk(1, data, ext);

                        // Create Fat16Reader for the new disk
                        var newHdd = _diskManager.GetHDD(1);
                        if (newHdd != null)
                        {
                            // Calculate PBR LBA (partition starts at cylinder 1)
                            int pbrLba = (1 * newHdd.Heads + 0) * newHdd.SectorsPerTrack;
                            var fat16B = new PC98Emu.Disk.Fat16Reader(newHdd, pbrLba);
                            if (fat16B.Initialize())
                            {
                                fat16B.ListRootDir();
                                _bios.SetFat16ReaderForDrive(1, fat16B); // Drive B:
                                Console.Error.WriteLine($"[HDI-MOUNT] Drive B: mounted successfully");

                                // Dump MAKYO\CONFIG.SYS if present
                                var (cfgCl, cfgSz) = fat16B.FindFile("MAKYO\\CONFIG.SYS");
                                if (cfgCl != 0)
                                {
                                    byte[]? cfgData = fat16B.ReadFile(cfgCl, cfgSz);
                                    if (cfgData != null)
                                        Console.Error.WriteLine($"[HDI-MOUNT] MAKYO\\CONFIG.SYS content:\n{System.Text.Encoding.GetEncoding(932).GetString(cfgData)}");
                                }
                                // Also dump root CONFIG.SYS
                                var (cfgCl2, cfgSz2) = fat16B.FindFile("CONFIG.SYS");
                                if (cfgCl2 != 0)
                                {
                                    byte[]? cfgData2 = fat16B.ReadFile(cfgCl2, cfgSz2);
                                    if (cfgData2 != null)
                                        Console.Error.WriteLine($"[HDI-MOUNT] CONFIG.SYS content:\n{System.Text.Encoding.GetEncoding(932).GetString(cfgData2)}");
                                }

                                // Parse AUTOEXEC.BAT and auto-execute the game
                                var (autoCl, autoSz) = fat16B.FindFile("AUTOEXEC.BAT");
                                if (autoCl != 0)
                                {
                                    byte[]? autoData = fat16B.ReadFile(autoCl, autoSz);
                                    if (autoData != null)
                                    {
                                        string autoText = System.Text.Encoding.ASCII.GetString(autoData);
                                        Console.Error.WriteLine($"[HDI-MOUNT] AUTOEXEC.BAT content:\n{autoText}");

                                        // Parse: find CD and last executable command
                                        string? cdDir = null;
                                        string? exeName = null;
                                        foreach (var rawLine in autoText.Split('\n'))
                                        {
                                            string line = rawLine.Trim().TrimEnd('\r');
                                            // Strip NUL bytes and control chars
                                            line = new string(line.Where(c => c >= 0x20).ToArray()).Trim();
                                            if (string.IsNullOrEmpty(line)) continue;
                                            string upper = line.ToUpperInvariant();
                                            if (upper.StartsWith("REM ") || upper.StartsWith("@ECHO") ||
                                                upper.StartsWith("SET ") || upper.StartsWith("PATH") ||
                                                upper.StartsWith("LH ")) continue;
                                            if (upper.StartsWith("CD ") || upper.StartsWith("CD\\"))
                                            {
                                                cdDir = line.Substring(upper.StartsWith("CD\\") ? 2 : 3).Trim().TrimStart('\\');
                                                continue;
                                            }
                                            // Last remaining line is the executable
                                            exeName = line.Split(' ')[0]; // Take command name without args
                                        }

                                        if (exeName != null)
                                        {
                                            // Build full path on B: drive
                                            string gamePath = cdDir != null ? $"{cdDir}\\{exeName}" : exeName;
                                            Console.Error.WriteLine($"[HDI-MOUNT] Auto-exec: searching for '{gamePath}'");

                                            // Try with common extensions (.EXE, .COM, .BAT)
                                            string[] exts = { ".EXE", ".COM", ".BAT", "" };
                                            byte[]? gameData = null;
                                            string foundName = "";
                                            foreach (var tryExt in exts)
                                            {
                                                string tryPath = gamePath.Contains('.') ? gamePath : gamePath + tryExt;
                                                var (gc, gs) = fat16B.FindFile(tryPath);
                                                if (gc != 0)
                                                {
                                                    gameData = fat16B.ReadFile(gc, gs);
                                                    foundName = tryPath;
                                                    Console.Error.WriteLine($"[HDI-MOUNT] Found: '{foundName}' size={gs}");

                                                    // If BAT file, parse it to find the real executable
                                                    if (tryExt == ".BAT" && gameData != null)
                                                    {
                                                        string batHex = BitConverter.ToString(gameData);
                                                        Console.Error.WriteLine($"[HDI-MOUNT] BAT hex: {batHex}");
                                                        string batText = System.Text.Encoding.GetEncoding(932).GetString(gameData); // Shift-JIS
                                                        Console.Error.WriteLine($"[HDI-MOUNT] BAT content: [{batText.Trim()}]");
                                                        string? realExe = null;
                                                        foreach (var bLine in batText.Split('\n'))
                                                        {
                                                            string bl = new string(bLine.Trim().TrimEnd('\r').Where(c => c >= 0x20).ToArray()).Trim();
                                                            if (string.IsNullOrEmpty(bl)) continue;
                                                            string bu = bl.ToUpperInvariant();
                                                            if (bu.StartsWith("REM ") || bu.StartsWith("@ECHO") || bu.StartsWith("SET ") || bu.StartsWith("PAUSE")) continue;
                                                            realExe = bl.Split(' ')[0];
                                                        }
                                                        if (realExe != null)
                                                        {
                                                            // Search for the real executable in same directory
                                                            string realPath = cdDir != null ? $"{cdDir}\\{realExe}" : realExe;
                                                            Console.Error.WriteLine($"[HDI-MOUNT] BAT references: '{realPath}'");
                                                            string[] realExts = { ".EXE", ".COM", "" };
                                                            foreach (var re in realExts)
                                                            {
                                                                string rp = realPath.Contains('.') ? realPath : realPath + re;
                                                                var (rc, rs) = fat16B.FindFile(rp);
                                                                if (rc != 0)
                                                                {
                                                                    gameData = fat16B.ReadFile(rc, rs);
                                                                    foundName = rp;
                                                                    Console.Error.WriteLine($"[HDI-MOUNT] Found real game: '{foundName}' size={rs}");
                                                                    break;
                                                                }
                                                            }
                                                        }
                                                    }
                                                    break;
                                                }
                                            }

                                            if (gameData != null && gameData.Length > 0)
                                            {
                                                Console.Error.WriteLine($"[HDI-MOUNT] Loading game '{foundName}' ({gameData.Length} bytes)");

                                                // Load and execute directly
                                                var mem = _bus.GetMemoryDirect();
                                                bool isExe = gameData.Length >= 2 && gameData[0] == 0x4D && gameData[1] == 0x5A;
                                                ushort loadSeg = 0x1800; // Load above DOS kernel (ends ~0x1600)

                                                if (isExe)
                                                {
                                                    // Parse EXE header
                                                    int headerSize = (gameData[8] | (gameData[9] << 8)) * 16;
                                                    int initSS = gameData[0x0E] | (gameData[0x0F] << 8);
                                                    int initSP = gameData[0x10] | (gameData[0x11] << 8);
                                                    int initIP = gameData[0x14] | (gameData[0x15] << 8);
                                                    int initCS = gameData[0x16] | (gameData[0x17] << 8);
                                                    int relocCount = gameData[6] | (gameData[7] << 8);
                                                    int relocOff = gameData[0x18] | (gameData[0x19] << 8);

                                                    ushort pspSeg = loadSeg;
                                                    ushort codeSeg = (ushort)(pspSeg + 0x10); // PSP is 256 bytes = 16 paragraphs

                                                    // Build proper PSP
                                                    int pspAddr = pspSeg << 4;
                                                    Array.Clear(mem, pspAddr, 256);
                                                    mem[pspAddr] = 0xCD;      // INT 20h
                                                    mem[pspAddr + 1] = 0x20;
                                                    mem[pspAddr + 2] = 0xFF;  // Top of memory (low)
                                                    mem[pspAddr + 3] = 0x9F;  // Top of memory (high) = 0x9FFF
                                                    // Parent PSP = self (no parent)
                                                    mem[pspAddr + 0x16] = (byte)(pspSeg & 0xFF);
                                                    mem[pspAddr + 0x17] = (byte)(pspSeg >> 8);
                                                    // JFT: handles 0-4 = SFT indices 0-4, rest = 0xFF (unused)
                                                    for (int jj = 0; jj < 20; jj++)
                                                        mem[pspAddr + 0x18 + jj] = (byte)(jj < 5 ? jj : 0xFF);
                                                    // Environment segment (point to a valid empty environment)
                                                    ushort envSeg = (ushort)(pspSeg - 1); // Just before PSP
                                                    mem[(envSeg << 4)] = 0x00; // Empty environment (double NUL)
                                                    mem[(envSeg << 4) + 1] = 0x00;
                                                    mem[pspAddr + 0x2C] = (byte)(envSeg & 0xFF);
                                                    mem[pspAddr + 0x2D] = (byte)(envSeg >> 8);
                                                    // Max handle count
                                                    mem[pspAddr + 0x32] = 20;
                                                    mem[pspAddr + 0x33] = 0;
                                                    // JFT pointer (far pointer to PSP+0x18)
                                                    mem[pspAddr + 0x34] = 0x18;
                                                    mem[pspAddr + 0x35] = 0x00;
                                                    mem[pspAddr + 0x36] = (byte)(pspSeg & 0xFF);
                                                    mem[pspAddr + 0x37] = (byte)(pspSeg >> 8);
                                                    // INT 21h / RETF at PSP:0050
                                                    mem[pspAddr + 0x50] = 0xCD;
                                                    mem[pspAddr + 0x51] = 0x21;
                                                    mem[pspAddr + 0x52] = 0xCB; // RETF

                                                    // Copy code (skip EXE header)
                                                    int codeLen = gameData.Length - headerSize;
                                                    int loadAddr = codeSeg << 4;
                                                    Array.Copy(gameData, headerSize, mem, loadAddr, codeLen);

                                                    // Apply relocations
                                                    for (int r = 0; r < relocCount; r++)
                                                    {
                                                        int rOff = relocOff + r * 4;
                                                        if (rOff + 3 >= gameData.Length) break;
                                                        int rOffset = gameData[rOff] | (gameData[rOff + 1] << 8);
                                                        int rSeg = gameData[rOff + 2] | (gameData[rOff + 3] << 8);
                                                        int fixAddr = loadAddr + rSeg * 16 + rOffset;
                                                        if (fixAddr + 1 < mem.Length)
                                                        {
                                                            ushort val = (ushort)(mem[fixAddr] | (mem[fixAddr + 1] << 8));
                                                            val += codeSeg;
                                                            mem[fixAddr] = (byte)(val & 0xFF);
                                                            mem[fixAddr + 1] = (byte)(val >> 8);
                                                        }
                                                    }

                                                    // Set CPU registers
                                                    _cpu.CS = (ushort)(codeSeg + initCS);
                                                    _cpu.IP = (ushort)initIP;
                                                    _cpu.SS = (ushort)(codeSeg + initSS);
                                                    _cpu.SP = (ushort)initSP;
                                                    _cpu.DS = pspSeg;
                                                    _cpu.ES = pspSeg;
                                                    Console.Error.WriteLine($"[HDI-MOUNT] EXE header: headerSize={headerSize} initCS={initCS:X4} initIP={initIP:X4} initSS={initSS:X4} initSP={initSP:X4} relocs={relocCount} relocOff={relocOff:X4}");
                                                    Console.Error.WriteLine($"[HDI-MOUNT] EXE loaded: pspSeg={pspSeg:X4} codeSeg={codeSeg:X4} codeLen={codeLen} CS:IP={_cpu.CS:X4}:{_cpu.IP:X4} SS:SP={_cpu.SS:X4}:{_cpu.SP:X4}");
                                                    // Dump first 16 bytes at entry point
                                                    int entryPhys = (_cpu.CS << 4) + _cpu.IP;
                                                    string hexEntry = "";
                                                    for (int hh = 0; hh < 16 && entryPhys + hh < mem.Length; hh++)
                                                        hexEntry += $"{mem[entryPhys + hh]:X2} ";
                                                    Console.Error.WriteLine($"[HDI-MOUNT] Entry bytes: {hexEntry}");
                                                }
                                                else
                                                {
                                                    // COM file
                                                    ushort pspSeg = loadSeg;
                                                    int pspAddr = pspSeg << 4;
                                                    Array.Clear(mem, pspAddr, 256);
                                                    mem[pspAddr] = 0xCD;
                                                    mem[pspAddr + 1] = 0x20;
                                                    Array.Copy(gameData, 0, mem, pspAddr + 0x100, gameData.Length);
                                                    _cpu.CS = pspSeg;
                                                    _cpu.IP = 0x0100;
                                                    _cpu.SS = pspSeg;
                                                    _cpu.SP = 0xFFFE;
                                                    _cpu.DS = pspSeg;
                                                    _cpu.ES = pspSeg;
                                                    Console.Error.WriteLine($"[HDI-MOUNT] COM loaded: CS:IP={_cpu.CS:X4}:0100");
                                                }

                                                // Reset DOS memory allocations for the directly loaded game
                                                // The game will resize itself with AH=4A, then allocate with AH=48
                                                _bios.DosBiosInstance!.ResetAllocationsForDirectLoad(loadSeg, (ushort)(0xA000 - loadSeg));

                                                // Set current PSP to the game's PSP
                                                _bios.DosBiosInstance!.SetCurrentPSP(loadSeg);

                                                // Re-install sound driver stub IVT (gets overwritten during boot)
                                                mem[0x104] = 0x00; mem[0x105] = 0x00; // INT 41h offset
                                                mem[0x106] = 0x25; mem[0x107] = 0xE8; // INT 41h segment (E825:0000 = 0xE8250)
                                                Console.Error.WriteLine($"[SOUND] Re-set IVT[41h] → E825:0000 before game launch");

                                                // Re-install INT 2Ah (network/critical section) IRET stub
                                                mem[0xA8] = 0x00; mem[0xA9] = 0x00; // INT 2Ah offset
                                                mem[0xAA] = 0x24; mem[0xAB] = 0xE8; // INT 2Ah segment (E824:0000 = 0xE8240)
                                                Console.Error.WriteLine($"[BIOS] Re-set IVT[2Ah] → E824:0000 before game launch");

                                                // Switch DOS current drive to B: and set current directory to MAKYO
                                                _bios.DosBiosInstance!.SetCurrentDrive(1);
                                                _bios.DosBiosInstance!.SetCurrentDirectory(1, "MAKYO");

                                                // Clear screen for game
                                                for (int a = 0xA0000; a < 0xA2000; a += 2)
                                                    { mem[a] = 0x20; mem[a + 1] = 0x00; }
                                                for (int a = 0xA2000; a < 0xA4000; a += 2)
                                                    { mem[a] = 0x00; mem[a + 1] = 0x00; }

                                                Console.Error.WriteLine($"[HDI-MOUNT] Game launched!");

                                                // Enable I/O debug to see what ports the game accesses
                                                _bus.IoDebug = true;

                                                // Unhalt CPU for game execution
                                                _cpu.Halted = false;

                                                // Trace first 200 instructions of game
                                                for (int t = 0; t < 200; t++)
                                                {
                                                    var gmem = _bus.GetMemoryDirect();
                                                    int traceAddr = ((_cpu.CS << 4) + _cpu.IP) & 0xFFFFF;
                                                    byte op = gmem[traceAddr];
                                                    byte op2 = traceAddr + 1 < gmem.Length ? gmem[traceAddr + 1] : (byte)0;
                                                    Console.Error.WriteLine($"[GAME-TRACE] {t}: {_cpu.CS:X4}:{_cpu.IP:X4} op={op:X2}{op2:X2} AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4} DS={_cpu.DS:X4} SS:SP={_cpu.SS:X4}:{_cpu.SP:X4} F={(_cpu.Flags.CF?"C":".")}{(_cpu.Flags.ZF?"Z":".")}{(_cpu.Flags.IF?"I":".")}");
                                                    if (_cpu.Halted) { Console.Error.WriteLine("[GAME-TRACE] CPU halted!"); break; }
                                                    try { StepCpu(); }
                                                    catch (Exception ex) { Console.Error.WriteLine($"[GAME-TRACE] Exception: {ex.Message}"); break; }
                                                }
                                            }
                                            else
                                            {
                                                Console.Error.WriteLine($"[HDI-MOUNT] Game executable not found: '{gamePath}'");
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                Console.Error.WriteLine($"[HDI-MOUNT] FAT16 init failed for drive B:");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[HDI-MOUNT] Failed to load HDI: {ex.Message}");
                        Console.Error.WriteLine(ex.StackTrace);
                    }
                }

                if (hasAudio)
                    _audioOutput!.FeedSamples(782);
            }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CRASH] After {_cpu.Cycles} cycles at CS:IP={_cpu.CS:X4}:{_cpu.IP:X4}");
                Console.Error.WriteLine($"[CRASH] AX={_cpu.AX:X4} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4}");
                Console.Error.WriteLine($"[CRASH] DS={_cpu.DS:X4} ES={_cpu.ES:X4} SS={_cpu.SS:X4} SP={_cpu.SP:X4}");
                var mem = _bus.GetMemoryDirect();
                int physAddr = (_cpu.CS << 4) + _cpu.IP;
                if (physAddr >= 0 && physAddr + 8 <= mem.Length)
                {
                    Console.Error.Write($"[CRASH] Bytes at CS:IP: ");
                    for (int i = 0; i < 8; i++)
                        Console.Error.Write($"{mem[physAddr + i]:X2} ");
                    Console.Error.WriteLine();
                }
                Console.Error.WriteLine($"[CRASH] {ex.GetType().Name}: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                break;
            }
        }

        if (hasAudio) _audioOutput?.Dispose();
        if (hasDisplay) _display?.Dispose();
    }
}
