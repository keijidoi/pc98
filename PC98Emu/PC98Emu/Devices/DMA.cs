namespace PC98Emu.Devices;
using PC98Emu.Bus;

public class DMA : IDevice
{
    private readonly Channel[] _channels = new Channel[4];
    private bool _flipFlop;

    public DMA() { for (int i = 0; i < 4; i++) _channels[i] = new Channel(); }

    public int GetChannelAddress(int ch) => _channels[ch].Address;
    public int GetChannelCount(int ch) => _channels[ch].Count;

    public void WriteByte(int port, byte value)
    {
        // Odd ports only: 0x01=ch0 addr, 0x03=ch1 addr, 0x05=ch2 addr, 0x07=ch3 addr
        // 0x09=ch0 count, 0x0B=ch1 count, 0x0D=ch2 count, 0x0F=ch3 count
        int ch, isCount;
        if (port >= 0x01 && port <= 0x07 && (port & 1) == 1) { ch = (port - 1) / 2; isCount = 0; }
        else if (port >= 0x09 && port <= 0x0F && (port & 1) == 1) { ch = (port - 9) / 2; isCount = 1; }
        else return;

        if (!_flipFlop)
        {
            if (isCount == 0) _channels[ch].Address = (_channels[ch].Address & 0xFF00) | value;
            else _channels[ch].Count = (_channels[ch].Count & 0xFF00) | value;
        }
        else
        {
            if (isCount == 0) _channels[ch].Address = (_channels[ch].Address & 0x00FF) | (value << 8);
            else _channels[ch].Count = (_channels[ch].Count & 0x00FF) | (value << 8);
        }
        _flipFlop = !_flipFlop;
    }

    public byte ReadByte(int port) => 0xFF;

    public void TransferToMemory(int channel, byte[] data, byte[] memory)
    {
        var ch = _channels[channel];
        int addr = ch.Address;
        for (int i = 0; i < data.Length && i <= ch.Count; i++)
        {
            if (addr + i < memory.Length) memory[addr + i] = data[i];
        }
        ch.Address += data.Length;
        ch.Count -= data.Length;
    }

    public void TransferFromMemory(int channel, byte[] buffer, byte[] memory)
    {
        var ch = _channels[channel];
        int addr = ch.Address;
        int count = Math.Min(buffer.Length, ch.Count + 1);
        for (int i = 0; i < count && addr + i < memory.Length; i++)
        {
            buffer[i] = memory[addr + i];
        }
        ch.Address += count;
        ch.Count -= count;
    }

    public void ResetFlipFlop() => _flipFlop = false;
    public ushort ReadWord(int port) => 0xFFFF;
    public void WriteWord(int port, ushort value) { }
    public void Reset() { _flipFlop = false; for (int i = 0; i < 4; i++) _channels[i] = new Channel(); }
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0x01, 0x03, 0x05, 0x07, 0x09, 0x0B, 0x0D, 0x0F };

    private class Channel { public int Address; public int Count; }
}
