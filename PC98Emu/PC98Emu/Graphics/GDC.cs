using PC98Emu.Bus;

namespace PC98Emu.Graphics;

/// <summary>
/// uPD7220 GDC (Graphic Display Controller) emulation.
/// Two instances are used: one for text (ports 0x60/0x62) and one for graphics (ports 0xA0/0xA2).
/// </summary>
public class GDC : IDevice
{
    private readonly bool _isText;
    private readonly int _basePort; // 0x60 for text, 0xA0 for graphics

    // Status register bits
    private const byte StatusVSync = 0x20;    // bit 5
    private const byte StatusHBlank = 0x10;   // bit 4
    private const byte StatusDma = 0x08;      // bit 3
    private const byte StatusDrawing = 0x04;  // bit 2
    private const byte StatusFifoEmpty = 0x01; // bit 0
    private const byte StatusFifoFull = 0x02;  // bit 1

    // Command codes
    private const byte CmdReset = 0x00;
    private const byte CmdSyncDe0 = 0x0E;
    private const byte CmdSyncDe1 = 0x0F;
    private const byte CmdStart = 0x0D;
    private const byte CmdStop = 0x0C;
    private const byte CmdCsrForm = 0x4B;
    private const byte CmdCsrW = 0x49;
    private const byte CmdCsrR = 0xE0;
    private const byte CmdPitch = 0x47;
    private const byte CmdWrite = 0x20;
    private const byte CmdRead = 0xA0;

    // State
    public bool DisplayEnabled { get; private set; }

    /// <summary>Enable display (equivalent to CmdStart via I/O).</summary>
    public void Start() => DisplayEnabled = true;

    /// <summary>Disable display (equivalent to CmdStop via I/O).</summary>
    public void Stop() => DisplayEnabled = false;
    public ushort CursorAddress { get; private set; }
    public byte Pitch { get; private set; } = 40; // default pitch

    private byte _statusRegister;
    private bool _vsync;

    // Command processing
    private byte _currentCommand;
    private int _parameterCount;
    private int _expectedParameters;
    private readonly byte[] _parameterBuffer = new byte[16];

    // FIFO for read data
    private readonly Queue<byte> _readFifo = new();

    // Scroll area parameters (up to 4 scroll areas, each 4 bytes)
    private readonly byte[] _scrollParams = new byte[16];
    private int _scrollParamIndex;

    // Cursor form parameters
    private readonly byte[] _csrFormParams = new byte[5];

    // Sync parameters
    private readonly byte[] _syncParams = new byte[8];

    // VSync timing: 56.4 Hz at 8 MHz => ~141844 cycles per frame
    private const int CyclesPerFrame = 141844;
    private const int CyclesPerVSync = 1000; // VSync pulse duration
    private int _frameCycleCounter;

    public GDC(bool isText)
    {
        _isText = isText;
        _basePort = isText ? 0x60 : 0xA0;
        _statusRegister = StatusFifoEmpty; // FIFO starts empty
    }

    public int[] GetPortRange()
    {
        return new[] { _basePort, _basePort + 2 };
    }

    public byte ReadByte(int port)
    {
        if (port == _basePort) // Even port: status register
        {
            return ReadStatus();
        }
        else if (port == _basePort + 2) // Odd port: data read
        {
            if (_readFifo.Count > 0)
                return _readFifo.Dequeue();
            return 0x00;
        }
        return 0xFF;
    }

    public void WriteByte(int port, byte value)
    {
        if (port == _basePort) // Even port (0x60/0xA0): parameter data
        {
            WriteParameter(value);
        }
        else if (port == _basePort + 2) // Odd port (0x62/0xA2): command/parameter
        {
            // If we're currently expecting parameters for an active command, treat as parameter
            if (_expectedParameters > 0 && _parameterCount < _expectedParameters)
            {
                WriteParameter(value);
            }
            else
            {
                WriteCommand(value);
            }
        }
    }

    public ushort ReadWord(int port)
    {
        byte lo = ReadByte(port);
        byte hi = ReadByte(port + 1);
        return (ushort)(lo | (hi << 8));
    }

    public void WriteWord(int port, ushort value)
    {
        WriteByte(port, (byte)value);
        WriteByte(port + 1, (byte)(value >> 8));
    }

    public void Reset()
    {
        DisplayEnabled = false;
        CursorAddress = 0;
        _currentCommand = 0;
        _parameterCount = 0;
        _expectedParameters = 0;
        _statusRegister = StatusFifoEmpty;
        _readFifo.Clear();
        _frameCycleCounter = 0;
        _vsync = false;
    }

    public void Tick(int cycles)
    {
        _frameCycleCounter += cycles;

        if (_frameCycleCounter >= CyclesPerFrame)
        {
            _frameCycleCounter -= CyclesPerFrame;
            _vsync = true;
        }

        if (_vsync)
        {
            _statusRegister |= StatusVSync;
            // VSync lasts for a short period
            if (_frameCycleCounter >= CyclesPerVSync)
            {
                _vsync = false;
                _statusRegister &= unchecked((byte)~StatusVSync);
            }
        }
    }

    public byte ReadStatus()
    {
        byte status = _statusRegister;

        // FIFO empty bit
        if (_readFifo.Count == 0)
            status |= StatusFifoEmpty;
        else
            status &= unchecked((byte)~StatusFifoEmpty);

        // FIFO full bit
        if (_readFifo.Count >= 16)
            status |= StatusFifoFull;
        else
            status &= unchecked((byte)~StatusFifoFull);

        return status;
    }

    public void WriteCommand(byte cmd)
    {
        _currentCommand = cmd;
        _parameterCount = 0;

        // Determine expected parameter count based on command
        switch (cmd)
        {
            case CmdReset:
                _expectedParameters = 0;
                DisplayEnabled = false;
                break;

            case CmdSyncDe0:
            case CmdSyncDe1:
                _expectedParameters = 8;
                if ((cmd & 0x01) != 0)
                    DisplayEnabled = true; // DE bit
                break;

            case CmdStart:
                _expectedParameters = 0;
                DisplayEnabled = true;
                break;

            case CmdStop:
                _expectedParameters = 0;
                DisplayEnabled = false;
                break;

            case CmdCsrForm:
                _expectedParameters = 3; // Up to 3 parameter bytes
                break;

            case CmdCsrW:
                _expectedParameters = 2; // Low byte, high byte
                break;

            case CmdCsrR:
                _expectedParameters = 0;
                // Queue cursor position data into read FIFO
                _readFifo.Enqueue((byte)(CursorAddress & 0xFF));
                _readFifo.Enqueue((byte)((CursorAddress >> 8) & 0xFF));
                _readFifo.Enqueue(0); // dot address / cursor form data
                _readFifo.Enqueue(0);
                _readFifo.Enqueue(0);
                break;

            case CmdPitch:
                _expectedParameters = 1;
                break;

            case CmdWrite:
                // WRITE command: variable number of parameters
                _expectedParameters = 0; // Accept parameters until next command
                break;

            case CmdRead:
                _expectedParameters = 0;
                break;

            default:
                // SCROLL commands: 0x70-0x7F
                if (cmd >= 0x70 && cmd <= 0x7F)
                {
                    _expectedParameters = 16; // Up to 16 parameter bytes for scroll areas
                    _scrollParamIndex = 0;
                }
                else
                {
                    _expectedParameters = 0;
                }
                break;
        }
    }

    public void WriteParameter(byte param)
    {
        if (_parameterCount < _parameterBuffer.Length)
            _parameterBuffer[_parameterCount] = param;

        switch (_currentCommand)
        {
            case CmdSyncDe0:
            case CmdSyncDe1:
                if (_parameterCount < 8)
                    _syncParams[_parameterCount] = param;
                break;

            case CmdCsrW:
                if (_parameterCount == 0)
                    CursorAddress = (ushort)((CursorAddress & 0xFF00) | param);
                else if (_parameterCount == 1)
                    CursorAddress = (ushort)((CursorAddress & 0x00FF) | (param << 8));
                break;

            case CmdCsrForm:
                if (_parameterCount < 3)
                    _csrFormParams[_parameterCount] = param;
                break;

            case CmdPitch:
                Pitch = param;
                break;

            case var c when c >= 0x70 && c <= 0x7F: // SCROLL
                if (_scrollParamIndex < _scrollParams.Length)
                    _scrollParams[_scrollParamIndex++] = param;
                break;
        }

        _parameterCount++;
    }
}
