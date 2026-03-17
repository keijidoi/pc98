namespace PC98Emu.Devices;
using PC98Emu.Bus;

public class SystemPort : IDevice
{
    public bool BeepEnabled { get; private set; }
    public byte ReadByte(int port) => 0xFF;
    public void WriteByte(int port, byte value)
    {
        if (port == 0x37) BeepEnabled = (value & 0x08) != 0;
    }
    public ushort ReadWord(int port) => 0xFFFF;
    public void WriteWord(int port, ushort value) { }
    public void Reset() => BeepEnabled = false;
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0x35, 0x37 };
}
