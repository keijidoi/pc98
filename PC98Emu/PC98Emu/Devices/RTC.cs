namespace PC98Emu.Devices;
using PC98Emu.Bus;

public class RTC : IDevice
{
    private int _command;
    private int _outputBit = 0;

    public byte ReadByte(int port)
    {
        var now = DateTime.Now;
        return (byte)(_outputBit & 1);
    }

    public void WriteByte(int port, byte value)
    {
        _command = value & 0x0F;
        bool stb = (value & 0x08) != 0;
        bool clk = (value & 0x04) != 0;
    }

    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) => WriteByte(port, (byte)value);
    public void Reset() { _command = 0; }
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0x20 };
}
