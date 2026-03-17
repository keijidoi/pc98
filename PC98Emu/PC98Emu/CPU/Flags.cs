namespace PC98Emu.CPU;

public class CpuFlags
{
    public ushort Value;

    private const int CF_BIT = 0;
    private const int PF_BIT = 2;
    private const int AF_BIT = 4;
    private const int ZF_BIT = 6;
    private const int SF_BIT = 7;
    private const int TF_BIT = 8;
    private const int IF_BIT = 9;
    private const int DF_BIT = 10;
    private const int OF_BIT = 11;

    public bool CF { get => GetFlag(CF_BIT); set => SetFlag(CF_BIT, value); }
    public bool PF { get => GetFlag(PF_BIT); set => SetFlag(PF_BIT, value); }
    public bool AF { get => GetFlag(AF_BIT); set => SetFlag(AF_BIT, value); }
    public bool ZF { get => GetFlag(ZF_BIT); set => SetFlag(ZF_BIT, value); }
    public bool SF { get => GetFlag(SF_BIT); set => SetFlag(SF_BIT, value); }
    public bool TF { get => GetFlag(TF_BIT); set => SetFlag(TF_BIT, value); }
    public bool IF { get => GetFlag(IF_BIT); set => SetFlag(IF_BIT, value); }
    public bool DF { get => GetFlag(DF_BIT); set => SetFlag(DF_BIT, value); }
    public bool OF { get => GetFlag(OF_BIT); set => SetFlag(OF_BIT, value); }

    private bool GetFlag(int bit) => (Value & (1 << bit)) != 0;
    private void SetFlag(int bit, bool val)
    {
        if (val) Value |= (ushort)(1 << bit);
        else Value &= (ushort)~(1 << bit);
    }

    private static readonly bool[] ParityTable = BuildParityTable();
    private static bool[] BuildParityTable()
    {
        var table = new bool[256];
        for (int i = 0; i < 256; i++)
        {
            int bits = 0, v = i;
            while (v != 0) { bits += v & 1; v >>= 1; }
            table[i] = (bits % 2) == 0;
        }
        return table;
    }

    public void UpdateSZP8(byte result)
    {
        ZF = result == 0;
        SF = (result & 0x80) != 0;
        PF = ParityTable[result];
    }

    public void UpdateSZP16(ushort result)
    {
        ZF = result == 0;
        SF = (result & 0x8000) != 0;
        PF = ParityTable[result & 0xFF];
    }
}
