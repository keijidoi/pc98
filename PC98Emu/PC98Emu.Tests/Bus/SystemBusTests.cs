using Xunit;
using PC98Emu.Bus;

namespace PC98Emu.Tests.Bus;

public class SystemBusTests
{
    [Fact]
    public void ReadWriteMemoryByte()
    {
        var bus = new SystemBus();
        bus.WriteMemoryByte(0x1000, 0xAB);
        Assert.Equal(0xAB, bus.ReadMemoryByte(0x1000));
    }

    [Fact]
    public void ReadWriteMemoryWord()
    {
        var bus = new SystemBus();
        bus.WriteMemoryWord(0x2000, 0x1234);
        Assert.Equal(0x34, bus.ReadMemoryByte(0x2000));
        Assert.Equal(0x12, bus.ReadMemoryByte(0x2001));
        Assert.Equal(0x1234, bus.ReadMemoryWord(0x2000));
    }

    [Fact]
    public void UnmappedMemoryReturnsZero()
    {
        var bus = new SystemBus();
        Assert.Equal(0x00, bus.ReadMemoryByte(0xFFFFF));
    }

    [Fact]
    public void IoPortDispatchesToDevice()
    {
        var bus = new SystemBus();
        var device = new StubDevice(0x42, new[] { 0x60, 0x62 });
        bus.RegisterDevice(device);
        bus.WriteIoByte(0x60, 0x99);
        Assert.Equal(0x99, bus.ReadIoByte(0x60));
    }

    [Fact]
    public void UnmappedIoPortReturns0xFF()
    {
        var bus = new SystemBus();
        Assert.Equal(0xFF, bus.ReadIoByte(0x999));
    }

    [Fact]
    public void BiosRomAreaIsReadOnly()
    {
        var bus = new SystemBus();
        bus.SetBiosRomArea(true);
        bus.WriteMemoryByte(0xE8000, 0xAB);
        Assert.Equal(0x00, bus.ReadMemoryByte(0xE8000));
        bus.WriteBiosDirectly(0xE8000, 0xCD);
        Assert.Equal(0xCD, bus.ReadMemoryByte(0xE8000));
    }

    [Fact]
    public void IsBiosArea_ReturnsTrueForRomRange()
    {
        var bus = new SystemBus();
        Assert.True(bus.IsBiosArea(0xE8000));
        Assert.True(bus.IsBiosArea(0xFFFFF));
        Assert.False(bus.IsBiosArea(0xE7FFF));
    }
}

public class StubDevice : IDevice
{
    private readonly Dictionary<int, byte> _ports = new();
    private readonly int[] _portRange;
    private readonly byte _defaultValue;

    public StubDevice(byte defaultValue, int[] portRange)
    {
        _defaultValue = defaultValue;
        _portRange = portRange;
    }

    public byte ReadByte(int port) => _ports.GetValueOrDefault(port, _defaultValue);
    public void WriteByte(int port, byte value) => _ports[port] = value;
    public ushort ReadWord(int port) => (ushort)(ReadByte(port) | (ReadByte(port + 1) << 8));
    public void WriteWord(int port, ushort value) { WriteByte(port, (byte)value); WriteByte(port + 1, (byte)(value >> 8)); }
    public void Reset() => _ports.Clear();
    public void Tick(int cycles) { }
    public int[] GetPortRange() => _portRange;
}
