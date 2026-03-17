namespace PC98Emu.Devices;
using PC98Emu.Bus;

public class Serial : IDevice
{
    public byte ReadByte(int port)
    {
        if (port == 0x32) return 0x05; // TX ready, no RX
        return 0x00;
    }
    public void WriteByte(int port, byte value) { }
    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) { }
    public void Reset() { }
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0x30, 0x31, 0x32, 0x33 };
}
