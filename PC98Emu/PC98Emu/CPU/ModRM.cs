namespace PC98Emu.CPU;

public class ModRMDecoder
{
    public int Mod;
    public int Reg;
    public int RM;

    public void Decode(byte value)
    {
        Mod = (value >> 6) & 0x03;
        Reg = (value >> 3) & 0x07;
        RM = value & 0x07;
    }
}
