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

        // Sound (slave PIC IRQ3 = system IRQ11 typically, but just wire to slave)
        _ym2608 = new YM2608(() => _slavePic.RaiseIRQ(3));

        // Other devices
        _rtc = new RTC();
        _serial = new Serial();
        _printer = new Printer();
        _systemPort = new SystemPort();

        // Disk controllers
        _sasi = new SASIController(_diskManager);

        // Renderers
        _textRenderer = new TextRenderer(_bus.GetMemoryDirect());
        _graphicsRenderer = new GraphicsRenderer(_bus.GetMemoryDirect(), _bus);

        // BIOS
        _bios = new CompatibleBios(_cpu, _bus);
        _bios.SetDiskManager(_diskManager);
        _bios.SetKeyboard(_keyboard);

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
        _bus.RegisterDevice(_ym2608);
        _bus.RegisterDevice(_rtc);
        _bus.RegisterDevice(_serial);
        _bus.RegisterDevice(_printer);
        _bus.RegisterDevice(_systemPort);
        _bus.RegisterDevice(_sasi);

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

        // Set initial CPU state: enable interrupts
        _cpu.Flags.IF = true;

        // Initialize stack pointer
        _cpu.SS = 0x0000;
        _cpu.SP = 0x0400;
    }

    public void LoadFloppyDisk(int drive, byte[] data)
    {
        var image = new D88Image(data);
        _diskManager.MountFloppy(drive, image);
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

    public void Boot()
    {
        _bootLoader.Boot(0);
    }

    public void Run()
    {
        // Initialize display and audio only for Run() (requires SDL2)
        _display = new Display(_textRenderer, _graphicsRenderer);
        _display.Init();

        _audioOutput = new AudioOutput(_ym2608);
        _audioOutput.Init();

        bool quit = false;
        _frameCycleAccumulator = 0;

        while (!quit)
        {
            int cycles = _cpu.Step();
            _scheduler.Advance(cycles);
            _frameCycleAccumulator += cycles;

            // Check for pending interrupts
            if (_masterPic.HasInterrupt() && _cpu.Flags.IF)
            {
                int vector = _masterPic.AcknowledgeInterrupt();
                _cpu.Interrupt((byte)vector);
            }

            // Frame timing
            if (_frameCycleAccumulator >= CyclesPerFrame)
            {
                _frameCycleAccumulator -= CyclesPerFrame;
                _display.RenderFrame();
                quit = !_display.PollEvents();

                // Feed audio samples (~44100 / 56.4 = ~782 samples per frame)
                _audioOutput.FeedSamples(782);
            }
        }

        _audioOutput.Dispose();
        _display.Dispose();
    }
}
