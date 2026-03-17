namespace PC98Emu.Devices;

using PC98Emu.Bus;

public class PIC : IDevice
{
    private readonly int _commandPort;
    private readonly int _dataPort;
    private byte _irr; // Interrupt Request Register
    private byte _isr; // In-Service Register
    private byte _imr = 0xFF; // Interrupt Mask Register (all masked initially)
    public int VectorBase;
    private int _icwStep;
    private bool _initMode;

    public PIC(int commandPort, int dataPort)
    {
        _commandPort = commandPort;
        _dataPort = dataPort;
    }

    public void WriteByte(int port, byte value)
    {
        if (port == _commandPort)
        {
            if ((value & 0x10) != 0) // ICW1
            {
                _initMode = true;
                _icwStep = 1;
                _imr = 0;
                _isr = 0;
                _irr = 0;
            }
            else if ((value & 0x08) == 0) // OCW2
            {
                if ((value & 0xE0) == 0x20) // non-specific EOI
                {
                    // Clear highest priority ISR bit
                    for (int i = 0; i < 8; i++)
                    {
                        if ((_isr & (1 << i)) != 0)
                        {
                            _isr &= (byte)~(1 << i);
                            break;
                        }
                    }
                }
                else if ((value & 0xE0) == 0x60) // specific EOI
                {
                    int irq = value & 0x07;
                    _isr &= (byte)~(1 << irq);
                }
            }
        }
        else // data port
        {
            if (_initMode)
            {
                switch (_icwStep)
                {
                    case 1: VectorBase = value; _icwStep = 2; break;
                    case 2: _icwStep = 3; break; // ICW3
                    case 3: _initMode = false; break; // ICW4
                }
            }
            else
            {
                _imr = value; // OCW1 - set interrupt mask
            }
        }
    }

    public byte ReadByte(int port)
    {
        if (port == _dataPort) return _imr;
        return _isr; // status register
    }

    public void RaiseIRQ(int irq) => _irr |= (byte)(1 << irq);
    public void LowerIRQ(int irq) => _irr &= (byte)~(1 << irq);

    public bool HasInterrupt()
    {
        byte pending = (byte)(_irr & ~_imr & ~_isr);
        return pending != 0;
    }

    public int AcknowledgeInterrupt()
    {
        byte pending = (byte)(_irr & ~_imr & ~_isr);
        for (int i = 0; i < 8; i++)
        {
            if ((pending & (1 << i)) != 0)
            {
                _irr &= (byte)~(1 << i);
                _isr |= (byte)(1 << i);
                return VectorBase + i;
            }
        }
        return VectorBase + 7; // spurious
    }

    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) => WriteByte(port, (byte)value);
    public void Reset() { _irr = 0; _isr = 0; _imr = 0xFF; }
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { _commandPort, _dataPort };
}
