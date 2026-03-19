namespace PC98Emu.Devices;
using PC98Emu.Bus;

/// <summary>
/// PC-98 calendar IC (uPD4990A-compatible) auxiliary port interface.
/// Ports 0x80 (data) and 0x82 (command/status).
///
/// After OUT 82,C0 (finalize command), SYSINIT reads calendar data through
/// a serial protocol with specific status transitions on port 82:
///   A4 → 00 → AC → 00 → BC → 00 → 00 (done)
/// Each delay loop reads port 82 once and exits when the condition matches.
/// </summary>
public class CalendarIC : IDevice
{
    private byte _lastWrite82;
    private int _readState;
    private bool _serialActive;

    // Complete status sequence for the serial read protocol after OUT 82,C0:
    // A4 (data ready), 00 (completion), AC (2nd data), 00, BC (3rd data), 00, 00 (final)
    private static readonly byte[] SerialStates = { 0xA4, 0x00, 0xAC, 0x00, 0xBC, 0x00, 0x00 };

    public byte ReadByte(int port)
    {
        if (port == 0x80) return 0x00;

        if (port == 0x82)
        {
            if (_serialActive)
            {
                byte val = SerialStates[_readState];
                if (_readState < SerialStates.Length - 1)
                    _readState++;
                return val;
            }
            return _lastWrite82;
        }
        return 0xFF;
    }

    public void WriteByte(int port, byte value)
    {
        if (port == 0x82)
        {
            _lastWrite82 = value;
            if (value == 0xC0)
            {
                _serialActive = true;
                _readState = 0;
            }
            else
            {
                _serialActive = false;
                _readState = 0;
            }
        }
    }

    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) => WriteByte(port, (byte)value);
    public void Reset() { _lastWrite82 = 0; _readState = 0; _serialActive = false; }
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0x80, 0x82 };
}
