namespace PC98Emu.Bus;

public class SystemBus
{
    private readonly byte[] _memory = new byte[0x100000]; // 1MB
    private readonly IDevice?[] _ioMap = new IDevice?[0x10000]; // 64K I/O ports
    private bool _biosRomProtect;

    public byte GvramDisplayPlane;
    public byte GvramWritePlane;

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
        return _memory[address];
    }

    public void WriteMemoryByte(int address, byte value)
    {
        address &= 0xFFFFF;
        if (_biosRomProtect && IsBiosArea(address)) return;
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

    public byte ReadIoByte(int port)
    {
        port &= 0xFFFF;
        var device = _ioMap[port];
        return device?.ReadByte(port) ?? 0xFF;
    }

    public void WriteIoByte(int port, byte value)
    {
        port &= 0xFFFF;
        var device = _ioMap[port];
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
