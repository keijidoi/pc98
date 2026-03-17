using PC98Emu.Bus;

namespace PC98Emu.Sound;

/// <summary>
/// YM2608 (OPNA) sound chip emulation.
/// 6 FM channels, 3 SSG channels, ADPCM, and timers.
/// </summary>
public class YM2608 : IDevice
{
    private const int PORT_ADDR1 = 0x188;
    private const int PORT_DATA1 = 0x18A;
    private const int PORT_ADDR2 = 0x18C;
    private const int PORT_DATA2 = 0x18E;

    private const int MASTER_CLOCK = 7987200; // 7.9872 MHz
    private const int SAMPLE_RATE = 44100;

    // Register banks: 256 registers per port
    private readonly byte[] _regs1 = new byte[256];
    private readonly byte[] _regs2 = new byte[256];

    // Current address latches
    private byte _addrLatch1;
    private byte _addrLatch2;

    // Status register
    private byte _status;

    // Sub-components
    private readonly SSG _ssg = new();
    private readonly FMChannel[] _fmChannels = new FMChannel[6];
    private readonly ADPCM _adpcm = new();

    // Timer A: 10-bit, period = (1024 - TA) * 72 master clocks
    private int _timerAValue; // 10-bit loaded value
    private int _timerACounter;
    private bool _timerAEnabled;
    private bool _timerALoaded;

    // Timer B: 8-bit, period = (256 - TB) * 1152 master clocks
    private int _timerBValue;
    private int _timerBCounter;
    private bool _timerBEnabled;
    private bool _timerBLoaded;

    private readonly Action _raiseIrq;

    public YM2608(Action raiseIrq)
    {
        _raiseIrq = raiseIrq;
        for (int i = 0; i < 6; i++)
            _fmChannels[i] = new FMChannel();
        Reset();
    }

    public void Reset()
    {
        Array.Clear(_regs1);
        Array.Clear(_regs2);
        _addrLatch1 = 0;
        _addrLatch2 = 0;
        _status = 0;
        _ssg.Reset();
        _adpcm.Reset();
        for (int i = 0; i < 6; i++)
            _fmChannels[i] = new FMChannel();
        _timerAValue = 0;
        _timerACounter = 0;
        _timerAEnabled = false;
        _timerALoaded = false;
        _timerBValue = 0;
        _timerBCounter = 0;
        _timerBEnabled = false;
        _timerBLoaded = false;
    }

    public int[] GetPortRange() => new[] { PORT_ADDR1, PORT_DATA1, PORT_ADDR2, PORT_DATA2 };

    public byte ReadByte(int port)
    {
        switch (port)
        {
            case PORT_ADDR1:
                // Status register
                return _status;

            case PORT_DATA1:
                // Read from port 1 register
                if (_addrLatch1 <= 0x0F)
                    return _ssg.ReadRegister(_addrLatch1);
                return _regs1[_addrLatch1];

            case PORT_ADDR2:
                return _status;

            case PORT_DATA2:
                return _regs2[_addrLatch2];

            default:
                return 0xFF;
        }
    }

    public void WriteByte(int port, byte value)
    {
        switch (port)
        {
            case PORT_ADDR1:
                _addrLatch1 = value;
                break;

            case PORT_DATA1:
                WriteRegisterPort1(_addrLatch1, value);
                break;

            case PORT_ADDR2:
                _addrLatch2 = value;
                break;

            case PORT_DATA2:
                WriteRegisterPort2(_addrLatch2, value);
                break;
        }
    }

    public ushort ReadWord(int port) => ReadByte(port);

    public void WriteWord(int port, ushort value) => WriteByte(port, (byte)value);

    private void WriteRegisterPort1(int reg, byte value)
    {
        _regs1[reg] = value;

        if (reg <= 0x0F)
        {
            // SSG registers
            _ssg.WriteRegister(reg, value);
        }
        else if (reg == 0x24)
        {
            // Timer A high 8 bits
            _timerAValue = (_timerAValue & 0x03) | (value << 2);
        }
        else if (reg == 0x25)
        {
            // Timer A low 2 bits
            _timerAValue = (_timerAValue & 0x3FC) | (value & 0x03);
        }
        else if (reg == 0x26)
        {
            // Timer B
            _timerBValue = value;
        }
        else if (reg == 0x27)
        {
            // Timer control
            _timerAEnabled = (value & 0x01) != 0;
            _timerBEnabled = (value & 0x02) != 0;

            if ((value & 0x04) != 0) // Load Timer A
            {
                _timerALoaded = true;
                _timerACounter = (1024 - _timerAValue) * 72;
            }
            if ((value & 0x08) != 0) // Load Timer B
            {
                _timerBLoaded = true;
                _timerBCounter = (256 - _timerBValue) * 1152;
            }
            if ((value & 0x10) != 0) // Reset Timer A flag
            {
                _status &= unchecked((byte)~0x01);
            }
            if ((value & 0x20) != 0) // Reset Timer B flag
            {
                _status &= unchecked((byte)~0x02);
            }
        }
        else if (reg == 0x28)
        {
            // Key On/Off
            int ch = value & 0x07;
            int fmIndex;
            if (ch <= 2)
                fmIndex = ch; // channels 0-2
            else if (ch >= 4 && ch <= 6)
                fmIndex = ch - 1; // channels 3-5 (mapped from 4-6)
            else
                return;

            byte opMask = (byte)((value >> 4) & 0x0F);
            if (opMask != 0)
                _fmChannels[fmIndex].KeyOn(opMask);
            else
                _fmChannels[fmIndex].KeyOff();
        }
        else if (reg >= 0x30 && reg <= 0xBF)
        {
            // FM ch1-3 operator/channel/frequency registers
            WriteFMRegister(reg, value, 0);
        }
    }

    private void WriteRegisterPort2(int reg, byte value)
    {
        _regs2[reg] = value;

        if (reg >= 0x30 && reg <= 0xBF)
        {
            // FM ch4-6 operator/channel/frequency registers
            WriteFMRegister(reg, value, 3);
        }
        else if (reg >= 0x00 && reg <= 0x0D)
        {
            // ADPCM registers (mapped as 0x100-0x10D in documentation)
            _adpcm.WriteRegister(reg, value);
        }
    }

    private void WriteFMRegister(int reg, byte value, int chOffset)
    {
        int ch = (reg & 0x03);
        if (ch == 3) return; // Invalid channel
        ch += chOffset;
        if (ch >= 6) return;

        int regBase = reg & 0xF0;

        if (regBase >= 0xA0 && regBase <= 0xA0)
        {
            // Frequency registers 0xA0-0xAF
            WriteFMFrequency(reg, value, chOffset);
        }
        else if (regBase >= 0x30 && regBase <= 0x90)
        {
            // Operator registers
            // OPN operator order: 0x_0=op1, 0x_4=op3, 0x_8=op2, 0x_C=op4 (YM2608 ordering)
            int opOffset = ((reg & 0x0C) >> 2);
            int op = opOffset switch
            {
                0 => 0, // op1
                1 => 2, // op3
                2 => 1, // op2
                3 => 3, // op4
                _ => 0
            };
            _fmChannels[ch].WriteOperatorRegister(op, reg, value);
        }
        else if (regBase >= 0xB0)
        {
            _fmChannels[ch].WriteChannelRegister(reg, value);
        }
    }

    private void WriteFMFrequency(int reg, byte value, int chOffset)
    {
        int ch = (reg & 0x03);
        if (ch == 3) return;
        ch += chOffset;
        if (ch >= 6) return;

        if ((reg & 0xFC) == 0xA0)
        {
            // F-Number low byte
            int fnum = (_regs1[0xA4 + (reg & 0x03)] << 8 | value);
            if (chOffset > 0)
                fnum = (_regs2[0xA4 + (reg & 0x03)] << 8 | value);

            int block = (fnum >> 11) & 0x07;
            fnum &= 0x7FF;
            _fmChannels[ch].SetFrequency(fnum, block);
        }
        else if ((reg & 0xFC) == 0xA4)
        {
            // F-Number high + block — just store, applied when low byte is written
            // (already stored in _regs1/_regs2)
        }
    }

    public void Tick(int cycles)
    {
        // Timer A
        if (_timerAEnabled && _timerALoaded)
        {
            _timerACounter -= cycles;
            if (_timerACounter <= 0)
            {
                _status |= 0x01; // Timer A overflow
                _timerACounter += (1024 - _timerAValue) * 72; // Reload
                _raiseIrq();
            }
        }

        // Timer B
        if (_timerBEnabled && _timerBLoaded)
        {
            _timerBCounter -= cycles;
            if (_timerBCounter <= 0)
            {
                _status |= 0x02; // Timer B overflow
                _timerBCounter += (256 - _timerBValue) * 1152; // Reload
                _raiseIrq();
            }
        }
    }

    /// <summary>
    /// Generate interleaved stereo PCM samples at 44100 Hz.
    /// Buffer layout: [L0, R0, L1, R1, ...]. count = number of sample pairs.
    /// </summary>
    public void GenerateSamples(short[] buffer, int offset, int count)
    {
        for (int i = 0; i < count; i++)
        {
            // SSG output
            int ssgOut = _ssg.GenerateSample();

            // FM output (mix all 6 channels)
            int fmOut = 0;
            for (int ch = 0; ch < 6; ch++)
            {
                fmOut += _fmChannels[ch].GenerateSample();
            }
            fmOut >>= 2; // scale down

            // ADPCM output
            int adpcmOut = _adpcm.GenerateSample();

            // Mix all sources
            int mixed = ssgOut + fmOut + adpcmOut;
            mixed = Math.Clamp(mixed, -32767, 32767);

            // Stereo: same signal to both channels (panning not yet implemented)
            int bufIdx = offset + i * 2;
            if (bufIdx + 1 < buffer.Length)
            {
                buffer[bufIdx] = (short)mixed;
                buffer[bufIdx + 1] = (short)mixed;
            }
        }
    }
}
