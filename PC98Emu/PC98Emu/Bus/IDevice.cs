namespace PC98Emu.Bus;

public interface IDevice
{
    byte ReadByte(int port);
    void WriteByte(int port, byte value);
    ushort ReadWord(int port);
    void WriteWord(int port, ushort value);
    void Reset();
    void Tick(int cycles);
    int[] GetPortRange();
}
