namespace PC98Emu.Sound;

/// <summary>
/// One FM channel with 4 operators, implementing OPN-style FM synthesis.
/// </summary>
public class FMChannel
{
    private const int SINE_TABLE_SIZE = 1024;
    private const int ENV_RATE = 3; // envelope update divider

    private static readonly short[] SineTable = GenerateSineTable();
    private static readonly int[] AttackRateTable = GenerateAttackRates();
    private static readonly int[] DecayRateTable = GenerateDecayRates();

    private readonly Operator[] _operators = new Operator[4];
    private int _algorithm;
    private int _feedback;
    private int _fnum;
    private int _block;
    private int _phaseIncrement;

    // Feedback shift register for operator 0
    private int _feedbackOut1;
    private int _feedbackOut2;

    public FMChannel()
    {
        for (int i = 0; i < 4; i++)
            _operators[i] = new Operator();
    }

    public void SetFrequency(int fnum, int block)
    {
        _fnum = fnum & 0x7FF;
        _block = block & 0x07;
        // Phase increment = (fnum * 2^block) / 2^(20-1) scaled for table size
        // Simplified: inc = fnum << block, then scaled to our sample rate
        _phaseIncrement = (_fnum << _block);
    }

    public void KeyOn(byte opMask)
    {
        for (int i = 0; i < 4; i++)
        {
            if (((opMask >> i) & 1) != 0)
            {
                _operators[i].KeyOn();
            }
        }
    }

    public void KeyOff()
    {
        for (int i = 0; i < 4; i++)
            _operators[i].KeyOff();
    }

    public void WriteOperatorRegister(int op, int reg, byte value)
    {
        if (op < 0 || op > 3) return;
        var o = _operators[op];

        // Register offsets (relative to base):
        // 0x30: DT1/MUL
        // 0x40: TL
        // 0x50: KS/AR
        // 0x60: AM/D1R
        // 0x70: D2R
        // 0x80: SL/RR
        // 0x90: SSG-EG (not fully implemented)

        int regBase = reg & 0xF0;
        switch (regBase)
        {
            case 0x30: // DT1, MUL
                o.Detune = (value >> 4) & 0x07;
                o.Multiple = value & 0x0F;
                break;
            case 0x40: // TL (Total Level)
                o.TotalLevel = value & 0x7F;
                break;
            case 0x50: // KS, AR
                o.KeyScale = (value >> 6) & 0x03;
                o.AttackRate = value & 0x1F;
                break;
            case 0x60: // AM, D1R
                o.AmEnable = (value & 0x80) != 0;
                o.Decay1Rate = value & 0x1F;
                break;
            case 0x70: // D2R
                o.Decay2Rate = value & 0x1F;
                break;
            case 0x80: // SL, RR
                o.SustainLevel = (value >> 4) & 0x0F;
                o.ReleaseRate = value & 0x0F;
                break;
            case 0x90: // SSG-EG
                o.SsgEg = value & 0x0F;
                break;
        }
    }

    public void WriteChannelRegister(int reg, byte value)
    {
        int regBase = reg & 0xFC;
        switch (regBase)
        {
            case 0xB0: // Feedback/Algorithm
                _algorithm = value & 0x07;
                _feedback = (value >> 3) & 0x07;
                break;
            case 0xB4: // Panning / AMS / PMS (simplified)
                break;
        }
    }

    public int GenerateSample()
    {
        // Compute phase increment for each operator based on frequency + detune/multiple
        for (int i = 0; i < 4; i++)
        {
            var o = _operators[i];
            int mul = o.Multiple == 0 ? 1 : o.Multiple * 2;
            o.CurrentPhaseInc = (_phaseIncrement * mul) >> 1;
        }

        // Update envelopes
        for (int i = 0; i < 4; i++)
            _operators[i].UpdateEnvelope();

        // Compute operator outputs based on algorithm
        int fb = _feedback > 0 ? ((_feedbackOut1 + _feedbackOut2) >> (10 - _feedback)) : 0;

        int op0 = ComputeOperator(0, fb);
        _feedbackOut2 = _feedbackOut1;
        _feedbackOut1 = op0;

        int output;
        switch (_algorithm)
        {
            case 0: // op1->op2->op3->op4
                output = ComputeOperator(3, ComputeOperator(2, ComputeOperator(1, op0)));
                break;
            case 1: // (op1+op2)->op3->op4
                output = ComputeOperator(3, ComputeOperator(2, op0 + ComputeOperator(1, 0)));
                break;
            case 2: // (op1+(op2->op3))->op4
                output = ComputeOperator(3, op0 + ComputeOperator(2, ComputeOperator(1, 0)));
                break;
            case 3: // ((op1->op2)+op3)->op4
                output = ComputeOperator(3, ComputeOperator(1, op0) + ComputeOperator(2, 0));
                break;
            case 4: // (op1->op2)+(op3->op4)
                output = ComputeOperator(1, op0) + ComputeOperator(3, ComputeOperator(2, 0));
                break;
            case 5: // op1->(op2+op3+op4)
                output = ComputeOperator(1, op0) + ComputeOperator(2, op0) + ComputeOperator(3, op0);
                break;
            case 6: // (op1->op2)+op3+op4
                output = ComputeOperator(1, op0) + ComputeOperator(2, 0) + ComputeOperator(3, 0);
                break;
            case 7: // op1+op2+op3+op4
                output = op0 + ComputeOperator(1, 0) + ComputeOperator(2, 0) + ComputeOperator(3, 0);
                break;
            default:
                output = 0;
                break;
        }

        return output >> 1; // scale down
    }

    private int ComputeOperator(int opIndex, int modulation)
    {
        var o = _operators[opIndex];

        // Advance phase
        o.Phase += o.CurrentPhaseInc;
        o.Phase &= 0xFFFFF; // 20-bit phase

        // Convert phase + modulation to sine table index
        int phaseIndex = ((o.Phase >> 10) + modulation) & (SINE_TABLE_SIZE - 1);

        // Get sine value and apply envelope attenuation
        int sineVal = SineTable[phaseIndex];
        int envAtten = o.EnvelopeLevel + (o.TotalLevel << 3);

        // Attenuation: reduce amplitude
        if (envAtten > 1023) envAtten = 1023;
        int attenScale = 1023 - envAtten;
        int result = (sineVal * attenScale) >> 10;

        return result;
    }

    private static short[] GenerateSineTable()
    {
        var table = new short[SINE_TABLE_SIZE];
        for (int i = 0; i < SINE_TABLE_SIZE; i++)
        {
            double phase = 2.0 * Math.PI * i / SINE_TABLE_SIZE;
            table[i] = (short)(Math.Sin(phase) * 2047); // 12-bit range
        }
        return table;
    }

    private static int[] GenerateAttackRates()
    {
        var table = new int[64];
        for (int i = 0; i < 64; i++)
            table[i] = i == 0 ? 0 : Math.Max(1, (int)(1023.0 / (1 + i * 4)));
        return table;
    }

    private static int[] GenerateDecayRates()
    {
        var table = new int[64];
        for (int i = 0; i < 64; i++)
            table[i] = i == 0 ? 0 : Math.Max(1, i);
        return table;
    }

    public class Operator
    {
        public int Multiple;
        public int Detune;
        public int TotalLevel;
        public int KeyScale;
        public int AttackRate = 31;
        public int Decay1Rate;
        public int Decay2Rate;
        public int SustainLevel = 15;
        public int ReleaseRate = 15;
        public bool AmEnable;
        public int SsgEg;

        public int Phase;
        public int CurrentPhaseInc;
        public int EnvelopeLevel = 1023; // max attenuation = silent
        public EnvState State = EnvState.Off;

        private int _envCounter;

        public enum EnvState
        {
            Off,
            Attack,
            Decay1,
            Decay2,
            Sustain,
            Release
        }

        public void KeyOn()
        {
            State = EnvState.Attack;
            Phase = 0;
            EnvelopeLevel = 1023;
            _envCounter = 0;
        }

        public void KeyOff()
        {
            if (State != EnvState.Off)
                State = EnvState.Release;
        }

        public void UpdateEnvelope()
        {
            _envCounter++;
            if (_envCounter < ENV_RATE) return;
            _envCounter = 0;

            switch (State)
            {
                case EnvState.Attack:
                    if (AttackRate >= 31)
                    {
                        EnvelopeLevel = 0;
                        State = EnvState.Decay1;
                    }
                    else if (AttackRate > 0)
                    {
                        int rate = Math.Min(63, AttackRate * 2);
                        EnvelopeLevel -= ((EnvelopeLevel + 1) * AttackRateTable[rate]) >> 8;
                        if (EnvelopeLevel <= 0)
                        {
                            EnvelopeLevel = 0;
                            State = EnvState.Decay1;
                        }
                    }
                    break;

                case EnvState.Decay1:
                    if (Decay1Rate > 0)
                    {
                        int rate = Math.Min(63, Decay1Rate * 2);
                        EnvelopeLevel += DecayRateTable[rate];
                        int sl = SustainLevel == 15 ? 1023 : SustainLevel << 6;
                        if (EnvelopeLevel >= sl)
                        {
                            EnvelopeLevel = sl;
                            State = EnvState.Decay2;
                        }
                    }
                    else
                    {
                        State = EnvState.Decay2;
                    }
                    break;

                case EnvState.Decay2:
                    if (Decay2Rate > 0)
                    {
                        int rate = Math.Min(63, Decay2Rate * 2);
                        EnvelopeLevel += DecayRateTable[rate];
                        if (EnvelopeLevel >= 1023)
                        {
                            EnvelopeLevel = 1023;
                            State = EnvState.Off;
                        }
                    }
                    break;

                case EnvState.Release:
                    {
                        int rate = Math.Min(63, ReleaseRate * 4 + 2);
                        EnvelopeLevel += DecayRateTable[rate];
                        if (EnvelopeLevel >= 1023)
                        {
                            EnvelopeLevel = 1023;
                            State = EnvState.Off;
                        }
                    }
                    break;
            }
        }
    }
}
