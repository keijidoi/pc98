namespace PC98Emu.Devices;

using PC98Emu.Bus;

public class PIT : IDevice
{
    private readonly int _port0, _port1, _port2, _portCtrl;
    private readonly Action _irq0Callback;
    private readonly Channel[] _channels = new Channel[3];

    public PIT(int port0, int port1, int port2, int portCtrl, Action irq0Callback)
    {
        _port0 = port0; _port1 = port1; _port2 = port2;
        _portCtrl = portCtrl;
        _irq0Callback = irq0Callback;
        for (int i = 0; i < 3; i++) _channels[i] = new Channel();
    }

    public int GetChannelCount(int ch) => _channels[ch].ReloadValue;

    public void WriteByte(int port, byte value)
    {
        if (port == _portCtrl)
        {
            int ch = (value >> 6) & 3;
            if (ch == 3) return;
            int accessMode = (value >> 4) & 3;
            int mode = (value >> 1) & 7;
            _channels[ch].Mode = mode;
            _channels[ch].AccessMode = accessMode;
            _channels[ch].HighByte = false;
            return;
        }
        int channel = port == _port0 ? 0 : port == _port1 ? 1 : 2;
        var c = _channels[channel];
        if (c.AccessMode == 1 || (c.AccessMode == 3 && !c.HighByte))
        {
            c.ReloadValue = (c.ReloadValue & 0xFF00) | value;
            if (c.AccessMode == 3) c.HighByte = true;
            else { c.Counter = c.ReloadValue; c.Active = true; }
        }
        else if (c.AccessMode == 2 || (c.AccessMode == 3 && c.HighByte))
        {
            c.ReloadValue = (c.ReloadValue & 0x00FF) | (value << 8);
            c.HighByte = false;
            c.Counter = c.ReloadValue == 0 ? 65536 : c.ReloadValue;
            c.Active = true;
        }
    }

    public byte ReadByte(int port)
    {
        int channel = port == _port0 ? 0 : port == _port1 ? 1 : 2;
        return (byte)(_channels[channel].Counter & 0xFF);
    }

    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) => WriteByte(port, (byte)value);

    public void Tick(int cycles)
    {
        for (int i = 0; i < 3; i++)
        {
            var c = _channels[i];
            if (!c.Active) continue;
            c.Counter -= cycles;
            while (c.Counter <= 0)
            {
                if (i == 0) _irq0Callback();
                if (c.Mode == 2 || c.Mode == 3)
                    c.Counter += c.ReloadValue == 0 ? 65536 : c.ReloadValue;
                else { c.Active = false; break; }
            }
        }
    }

    public void Reset() { for (int i = 0; i < 3; i++) _channels[i] = new Channel(); }
    public int[] GetPortRange() => new[] { _port0, _port1, _port2, _portCtrl };

    private class Channel
    {
        public int Counter;
        public int ReloadValue;
        public int Mode;
        public int AccessMode;
        public bool HighByte;
        public bool Active;
    }
}
