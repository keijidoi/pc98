namespace PC98Emu.Devices;
using PC98Emu.Bus;

public class Mouse : IDevice
{
    public byte ReadByte(int port) => 0x00;
    public void WriteByte(int port, byte value) { }
    public ushort ReadWord(int port) => 0;
    public void WriteWord(int port, ushort value) { }
    public void Reset() { }
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0x7FD9, 0x7FDB, 0x7FDD };
}
