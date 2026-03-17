using PC98Emu.Bus;

namespace PC98Emu.Disk;

/// <summary>
/// Basic SASI (Shugart Associates System Interface) controller stub.
/// Maps to ports 0x0CC0-0x0CCC on the PC-98.
/// </summary>
public class SASIController : IDevice
{
    private readonly DiskManager _diskManager;
    private byte _status;
    private byte _data;

    // Status bits
    private const byte STATUS_BSY = 0x08;
    private const byte STATUS_REQ = 0x01;
    private const byte STATUS_MSG = 0x04;
    private const byte STATUS_CD  = 0x02;
    private const byte STATUS_IO  = 0x40;

    public SASIController(DiskManager diskManager)
    {
        _diskManager = diskManager;
        _status = 0;
    }

    public byte ReadByte(int port)
    {
        return (port & 0x0F) switch
        {
            0x00 => _data,      // Data register
            0x02 => _status,    // Status register
            _ => 0xFF
        };
    }

    public void WriteByte(int port, byte value)
    {
        switch (port & 0x0F)
        {
            case 0x00:
                _data = value;
                break;
            case 0x02:
                // Command / reset
                break;
        }
    }

    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) => WriteByte(port, (byte)value);
    public void Reset() { _status = 0; _data = 0; }
    public void Tick(int cycles) { }

    public int[] GetPortRange() => new[]
    {
        0x0CC0, 0x0CC2, 0x0CC4, 0x0CC6, 0x0CC8, 0x0CCA, 0x0CCC
    };
}
