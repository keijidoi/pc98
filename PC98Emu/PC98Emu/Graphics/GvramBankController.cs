using PC98Emu.Bus;

namespace PC98Emu.Graphics;

/// <summary>
/// Handles GVRAM bank switching via ports 0xA4 (write plane) and 0xA6 (display plane).
/// Updates SystemBus.GvramWritePlane and GvramDisplayPlane fields.
/// </summary>
public class GvramBankController : IDevice
{
    private readonly SystemBus _bus;

    public GvramBankController(SystemBus bus)
    {
        _bus = bus;
    }

    public byte ReadByte(int port) => port switch
    {
        0xA4 => _bus.GvramWritePlane,
        0xA6 => _bus.GvramDisplayPlane,
        _ => 0xFF
    };

    public void WriteByte(int port, byte value)
    {
        switch (port)
        {
            case 0xA4:
                _bus.GvramWritePlane = (byte)(value & 0x03); // planes 0-3
                break;
            case 0xA6:
                _bus.GvramDisplayPlane = (byte)(value & 0x03);
                break;
        }
    }

    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) => WriteByte(port, (byte)value);
    public void Reset() { _bus.GvramWritePlane = 0; _bus.GvramDisplayPlane = 0; }
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0xA4, 0xA6 };
}
