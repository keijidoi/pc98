namespace PC98Emu.Sound;

/// <summary>
/// SSG (Software-controlled Sound Generator) — 3 channel square wave + noise.
/// Compatible with AY-3-8910 / YM2149. Registers 0x00-0x0F.
/// </summary>
public class SSG
{
    private readonly byte[] _registers = new byte[16];

    // Per-channel state
    private readonly int[] _periodCounters = new int[3];
    private readonly bool[] _toneOutput = new bool[3]; // current square wave level

    // Noise state
    private int _noiseCounter;
    private int _noiseLfsr = 1; // 17-bit LFSR, must not be 0
    private bool _noiseOutput;

    // Envelope state
    private int _envelopeCounter;
    private int _envelopeStep;
    private int _envelopeVolume;
    private bool _envelopeHolding;

    // Master clock divider: YM2608 SSG runs at master/4, then each period unit = 8 SSG clocks.
    // For sample generation at 44100 Hz from a ~4 MHz SSG clock:
    // SSG clock ~ 3993600 Hz (OPNA master 7987200 / 2), sample rate 44100
    // Cycles per sample = 3993600 / 44100 ~ 90.56
    // We'll use a fixed-point accumulator for accuracy.
    private const int SSG_CLOCK = 3993600;
    private const int SAMPLE_RATE = 44100;
    private int _clockAccumulator;

    public SSG()
    {
        Reset();
    }

    public void Reset()
    {
        Array.Clear(_registers);
        Array.Clear(_periodCounters);
        Array.Clear(_toneOutput);
        _noiseCounter = 0;
        _noiseLfsr = 1;
        _noiseOutput = false;
        _envelopeCounter = 0;
        _envelopeStep = 0;
        _envelopeVolume = 0;
        _envelopeHolding = false;
        _clockAccumulator = 0;
    }

    public void WriteRegister(int reg, byte value)
    {
        if (reg < 0 || reg > 0x0F) return;
        _registers[reg] = value;

        // Writing to envelope shape register resets the envelope
        if (reg == 0x0D)
        {
            _envelopeStep = 0;
            _envelopeHolding = false;
            _envelopeCounter = 0;
            // Initialize envelope volume based on attack direction
            _envelopeVolume = (value & 0x04) != 0 ? 0 : 15;
        }
    }

    public byte ReadRegister(int reg)
    {
        if (reg < 0 || reg > 0x0F) return 0;
        return _registers[reg];
    }

    private int GetTonePeriod(int ch)
    {
        int low = _registers[ch * 2];
        int high = _registers[ch * 2 + 1] & 0x0F;
        int period = (high << 8) | low;
        return period < 1 ? 1 : period;
    }

    private int GetNoisePeriod()
    {
        int p = _registers[6] & 0x1F;
        return p < 1 ? 1 : p;
    }

    private int GetEnvelopePeriod()
    {
        int p = _registers[0x0B] | (_registers[0x0C] << 8);
        return p < 1 ? 1 : p;
    }

    private void ClockOnce()
    {
        // Tone channels
        for (int ch = 0; ch < 3; ch++)
        {
            _periodCounters[ch]--;
            if (_periodCounters[ch] <= 0)
            {
                _periodCounters[ch] = GetTonePeriod(ch);
                _toneOutput[ch] = !_toneOutput[ch];
            }
        }

        // Noise
        _noiseCounter--;
        if (_noiseCounter <= 0)
        {
            _noiseCounter = GetNoisePeriod();
            // 17-bit LFSR: XOR bits 0 and 3
            int bit = ((_noiseLfsr ^ (_noiseLfsr >> 3)) & 1);
            _noiseLfsr = (_noiseLfsr >> 1) | (bit << 16);
            _noiseOutput = (_noiseLfsr & 1) != 0;
        }

        // Envelope
        _envelopeCounter--;
        if (_envelopeCounter <= 0)
        {
            _envelopeCounter = GetEnvelopePeriod();
            if (!_envelopeHolding)
            {
                AdvanceEnvelope();
            }
        }
    }

    private void AdvanceEnvelope()
    {
        int shape = _registers[0x0D] & 0x0F;
        bool attack = (shape & 0x04) != 0;

        _envelopeStep++;

        if (_envelopeStep < 16)
        {
            // First period
            _envelopeVolume = attack ? _envelopeStep : (15 - _envelopeStep);
        }
        else if (_envelopeStep == 16)
        {
            // End of first period — decide what happens next
            if ((shape & 0x08) == 0)
            {
                // Shapes 0x00-0x07: hold at 0 after first period
                _envelopeVolume = 0;
                _envelopeHolding = true;
            }
            else
            {
                bool cont = (shape & 0x08) != 0;
                bool hold = (shape & 0x01) != 0;
                bool alt = (shape & 0x02) != 0;

                if (hold)
                {
                    _envelopeVolume = alt ? (attack ? 0 : 15) : (attack ? 15 : 0);
                    _envelopeHolding = true;
                }
                else if (alt)
                {
                    // Restart with flipped direction
                    _envelopeStep = 0;
                    _envelopeVolume = attack ? 15 : 0;
                    // Flip the effective attack for next cycle by toggling bit 2 interpretation
                    // We handle this by just resetting step and letting it count again
                    // Actually for alternating, we need to track direction. Simplify:
                    _registers[0x0D] ^= 0x04; // flip attack bit for next cycle
                }
                else
                {
                    // Repeat same direction
                    _envelopeStep = 0;
                    _envelopeVolume = attack ? 0 : 15;
                }
            }
        }
    }

    // Volume table: 4-bit volume to linear amplitude (approximate log scale)
    private static readonly int[] VolumeTable = GenerateVolumeTable();

    private static int[] GenerateVolumeTable()
    {
        var table = new int[16];
        for (int i = 0; i < 16; i++)
        {
            // Approximate YM2149 DAC output levels
            if (i == 0)
                table[i] = 0;
            else
                table[i] = (int)(32767.0 * Math.Pow(10.0, (i - 15) * 2.0 / 20.0));
        }
        return table;
    }

    /// <summary>
    /// Generate one mono sample at 44100 Hz.
    /// </summary>
    public short GenerateSample()
    {
        // Run SSG clock ticks proportional to one sample period
        _clockAccumulator += SSG_CLOCK;
        int ticks = _clockAccumulator / SAMPLE_RATE;
        _clockAccumulator %= SAMPLE_RATE;

        for (int t = 0; t < ticks; t++)
        {
            ClockOnce();
        }

        int mixer = _registers[7];
        int output = 0;

        for (int ch = 0; ch < 3; ch++)
        {
            bool toneDisable = ((mixer >> ch) & 1) != 0;
            bool noiseDisable = ((mixer >> (ch + 3)) & 1) != 0;

            bool tone = toneDisable || _toneOutput[ch];
            bool noise = noiseDisable || _noiseOutput;

            if (tone && noise)
            {
                int volReg = _registers[0x08 + ch];
                int volume;
                if ((volReg & 0x10) != 0)
                {
                    // Envelope mode
                    volume = _envelopeVolume;
                }
                else
                {
                    volume = volReg & 0x0F;
                }
                output += VolumeTable[volume];
            }
        }

        // Clamp and scale (3 channels mixed, divide by 3 to avoid clipping)
        output /= 3;
        if (output > 32767) output = 32767;
        if (output < -32767) output = -32767;

        return (short)output;
    }
}
