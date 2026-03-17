namespace PC98Emu.Devices;
using PC98Emu.Bus;

public class Keyboard : IDevice
{
    private readonly Action _raiseIRQ1;
    private readonly Queue<byte> _buffer = new(16);

    public Keyboard(Action raiseIRQ1) => _raiseIRQ1 = raiseIRQ1;

    public void EnqueueScancode(byte scancode)
    {
        if (_buffer.Count >= 16) _buffer.Dequeue();
        _buffer.Enqueue(scancode);
        _raiseIRQ1();
    }

    public byte ReadByte(int port)
    {
        if (port == 0x41) return _buffer.Count > 0 ? _buffer.Dequeue() : (byte)0;
        if (port == 0x43) return (byte)(_buffer.Count > 0 ? 0x01 : 0x00);
        return 0xFF;
    }

    public void WriteByte(int port, byte value) { }
    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) { }
    public void Reset() => _buffer.Clear();
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0x41, 0x43 };
}
