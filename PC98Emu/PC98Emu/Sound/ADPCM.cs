namespace PC98Emu.Sound;

/// <summary>
/// ADPCM decoder for YM2608. 4-bit delta PCM with step size adaptation.
/// </summary>
public class ADPCM
{
    // Standard ADPCM step size table (IMA/OKI style adapted for YM2608)
    private static readonly int[] StepTable =
    {
        16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66,
        73, 80, 88, 97, 107, 118, 130, 143, 157, 173, 190, 209, 230, 253,
        279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876,
        963, 1060, 1166, 1282, 1411, 1552
    };

    private static readonly int[] IndexAdjust = { -1, -1, -1, -1, 2, 5, 7, 9 };

    // ADPCM state
    private int _stepIndex;
    private int _accumulator;

    // Memory buffer for ADPCM data
    private readonly byte[] _memory = new byte[256 * 1024]; // 256KB ADPCM RAM

    // Registers
    private int _startAddr;
    private int _endAddr;
    private int _deltaN; // playback rate (delta-N)
    private int _level; // volume level
    private byte _control; // control flags
    private bool _playing;
    private int _currentAddr;
    private int _playAccumulator;
    private bool _nibbleHigh;
    private byte _currentByte;

    public bool Busy => _playing;

    public ADPCM()
    {
        Reset();
    }

    public void Reset()
    {
        _stepIndex = 0;
        _accumulator = 0;
        _startAddr = 0;
        _endAddr = 0;
        _deltaN = 0;
        _level = 0;
        _control = 0;
        _playing = false;
        _currentAddr = 0;
        _playAccumulator = 0;
        _nibbleHigh = false;
    }

    public void WriteRegister(int reg, byte value)
    {
        switch (reg)
        {
            case 0x00: // Control
                _control = value;
                if ((value & 0x80) != 0) // Reset
                {
                    _playing = false;
                    _stepIndex = 0;
                    _accumulator = 0;
                }
                if ((value & 0x20) != 0) // Start
                {
                    _playing = true;
                    _currentAddr = _startAddr << 5; // Address is in 32-byte units
                    _stepIndex = 0;
                    _accumulator = 0;
                    _nibbleHigh = false;
                    _playAccumulator = 0;
                }
                break;
            case 0x02: // Start addr low
                _startAddr = (_startAddr & 0xFF00) | value;
                break;
            case 0x03: // Start addr high
                _startAddr = (_startAddr & 0x00FF) | (value << 8);
                break;
            case 0x04: // End addr low
                _endAddr = (_endAddr & 0xFF00) | value;
                break;
            case 0x05: // End addr high
                _endAddr = (_endAddr & 0x00FF) | (value << 8);
                break;
            case 0x09: // Delta-N low
                _deltaN = (_deltaN & 0xFF00) | value;
                break;
            case 0x0A: // Delta-N high
                _deltaN = (_deltaN & 0x00FF) | (value << 8);
                break;
            case 0x0B: // Level
                _level = value;
                break;
        }
    }

    public byte ReadRegister(int reg)
    {
        return 0; // Most ADPCM registers are write-only
    }

    /// <summary>
    /// Decode a single 4-bit ADPCM nibble into a PCM sample.
    /// </summary>
    public short DecodeSample(byte nibble)
    {
        nibble &= 0x0F;
        int sign = (nibble & 8) != 0 ? -1 : 1;
        int magnitude = nibble & 7;

        int step = StepTable[_stepIndex];
        int delta = ((magnitude * 2 + 1) * step) >> 3;

        _accumulator += sign * delta;
        _accumulator = Math.Clamp(_accumulator, -32768, 32767);

        // Adjust step index
        _stepIndex += IndexAdjust[magnitude];
        _stepIndex = Math.Clamp(_stepIndex, 0, StepTable.Length - 1);

        return (short)_accumulator;
    }

    /// <summary>
    /// Generate one ADPCM sample at the native rate.
    /// </summary>
    public short GenerateSample()
    {
        if (!_playing || _deltaN == 0)
            return 0;

        // Delta-N controls playback rate relative to 55.5 kHz
        _playAccumulator += _deltaN;
        if (_playAccumulator < 65536)
            return (short)((_accumulator * _level) >> 8);

        _playAccumulator -= 65536;

        // Read next nibble
        int endByteAddr = (_endAddr << 5) + 31;
        if (_currentAddr > endByteAddr)
        {
            _playing = false;
            return 0;
        }

        if (!_nibbleHigh)
        {
            int memAddr = _currentAddr & (_memory.Length - 1);
            _currentByte = _memory[memAddr];
            DecodeSample((byte)((_currentByte >> 4) & 0x0F));
            _nibbleHigh = true;
        }
        else
        {
            DecodeSample((byte)(_currentByte & 0x0F));
            _nibbleHigh = false;
            _currentAddr++;
        }

        return (short)((_accumulator * _level) >> 8);
    }

    /// <summary>
    /// Write data to ADPCM memory at the current write position.
    /// </summary>
    public void WriteMemory(int addr, byte data)
    {
        if (addr >= 0 && addr < _memory.Length)
            _memory[addr] = data;
    }
}
