using PC98Emu.Graphics;

namespace PC98Emu.Bus;

public class SystemBus
{
    private readonly byte[] _memory = new byte[0x100000]; // 1MB
    private readonly IDevice?[] _ioMap = new IDevice?[0x10000]; // 64K I/O ports
    private bool _biosRomProtect;

    public byte GvramDisplayPlane;
    public byte GvramWritePlane;
    public GRCG? Grcg; // Set by Emulator after construction

    public void RegisterDevice(IDevice device)
    {
        foreach (var port in device.GetPortRange())
            _ioMap[port] = device;
    }

    public void SetBiosRomArea(bool readOnly) => _biosRomProtect = readOnly;
    public bool IsBiosArea(int address) => address >= 0xE8000 && address <= 0xFFFFF;

    public void WriteBiosDirectly(int address, byte value)
    {
        _memory[address & 0xFFFFF] = value;
    }

    public byte ReadMemoryByte(int address)
    {
        address &= 0xFFFFF;

        // GRCG TCR mode: intercept GVRAM reads
        if (Grcg != null && Grcg.Enabled && (Grcg.Mode & 0xC0) == 0x80
            && address >= 0xA8000 && address <= 0xBFFFF)
        {
            int gvramOffset = (address - 0xA8000) % 0x8000;
            return Grcg.ReadGvram(_memory, gvramOffset);
        }

        return _memory[address];
    }

    // Text VRAM write tracking for debugging
    public int TextVramWriteCount { get; private set; }
    private bool _textVramTraceEnabled;
    public void EnableTextVramTrace(bool enable) => _textVramTraceEnabled = enable;

    // Memory watchpoint for debugging (range-based)
    private int _watchStart = -1;
    private int _watchEnd = -1;
    private bool _watchEnabled;
    public Action<int, byte, byte>? OnWatchHit; // (address, oldVal, newVal)
    public void SetWatchpoint(int address)
    {
        _watchStart = address & 0xFFFFF;
        _watchEnd = _watchStart;
        _watchEnabled = true;
    }
    public void SetWatchpointRange(int startAddr, int endAddr)
    {
        _watchStart = startAddr & 0xFFFFF;
        _watchEnd = endAddr & 0xFFFFF;
        _watchEnabled = true;
    }

    public void WriteMemoryByte(int address, byte value)
    {
        address &= 0xFFFFF;
        if (_biosRomProtect && IsBiosArea(address)) return;
        if (_watchEnabled && address >= _watchStart && address <= _watchEnd && value != _memory[address])
            OnWatchHit?.Invoke(address, _memory[address], value);
        if (address >= 0xA0000 && address < 0xA2000 && value != 0)
        {
            TextVramWriteCount++;
            if (_textVramTraceEnabled && TextVramWriteCount <= 10)
                Console.Error.WriteLine($"[TVRAM] Write #{TextVramWriteCount} addr=0x{address:X5} val=0x{value:X2}");
        }

        // GRCG intercept: GVRAM writes in range 0xA8000-0xBFFFF when GRCG is active
        if (Grcg != null && Grcg.Enabled && address >= 0xA8000 && address <= 0xBFFFF)
        {
            int gvramOffset = (address - 0xA8000) % 0x8000; // Offset within one plane
            Grcg.WriteGvram(_memory, gvramOffset, value);
            return;
        }

        _memory[address] = value;
    }

    public ushort ReadMemoryWord(int address)
    {
        return (ushort)(ReadMemoryByte(address) | (ReadMemoryByte(address + 1) << 8));
    }

    public void WriteMemoryWord(int address, ushort value)
    {
        WriteMemoryByte(address, (byte)value);
        WriteMemoryByte(address + 1, (byte)(value >> 8));
    }

    // Debug: track unhandled I/O
    private readonly HashSet<int> _warnedPorts = new();
    public bool IoDebug { get; set; }

    public byte ReadIoByte(int port)
    {
        port &= 0xFFFF;
        var device = _ioMap[port];
        if (device == null && IoDebug && _warnedPorts.Add(port))
            Console.WriteLine($"[IO] Unhandled read port 0x{port:X4}");
        return device?.ReadByte(port) ?? 0xFF;
    }

    public void WriteIoByte(int port, byte value)
    {
        port &= 0xFFFF;
        var device = _ioMap[port];
        if (device == null && IoDebug && _warnedPorts.Add(port))
            Console.WriteLine($"[IO] Unhandled write port 0x{port:X4} = 0x{value:X2}");
        device?.WriteByte(port, value);
    }

    public ushort ReadIoWord(int port)
    {
        port &= 0xFFFF;
        var device = _ioMap[port];
        return device?.ReadWord(port) ?? 0xFFFF;
    }

    public void WriteIoWord(int port, ushort value)
    {
        port &= 0xFFFF;
        var device = _ioMap[port];
        device?.WriteWord(port, value);
    }

    public byte[] GetMemoryDirect() => _memory;
}
