namespace PC98Emu.Devices;
using PC98Emu.Bus;

public class Printer : IDevice
{
    public byte ReadByte(int port)
    {
        if (port == 0x42) return 0x04; // busy
        return 0xFF;
    }
    public void WriteByte(int port, byte value) { }
    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) { }
    public void Reset() { }
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0x40, 0x42 };
}
