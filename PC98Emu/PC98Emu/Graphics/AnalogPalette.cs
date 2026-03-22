using PC98Emu.Bus;

namespace PC98Emu.Graphics;

/// <summary>
/// PC-98 analog palette (16 colors, 4-bit per channel RGB).
/// Port 0xA8: palette index (write)
/// Port 0xAA: green value (4-bit, read/write)
/// Port 0xAC: red value (4-bit, read/write)
/// Port 0xAE: blue value (4-bit, read/write)
/// </summary>
public class AnalogPalette : IDevice
{
    private byte _index; // Current palette index (0-15)

    // 16 entries, each with 4-bit R, G, B
    private readonly byte[] _red = new byte[16];
    private readonly byte[] _green = new byte[16];
    private readonly byte[] _blue = new byte[16];

    // Pre-computed ARGB palette for renderer
    public readonly uint[] Palette = new uint[16];

    public AnalogPalette()
    {
        // Initialize with default PC-98 digital palette
        SetDefaultPalette();
    }

    private void SetDefaultPalette()
    {
        // Default PC-98 16-color palette (digital)
        byte[] r = { 0x0, 0x0, 0x7, 0x7, 0x0, 0x0, 0x7, 0x7, 0x0, 0x0, 0xF, 0xF, 0x0, 0x0, 0xF, 0xF };
        byte[] g = { 0x0, 0x0, 0x0, 0x0, 0x7, 0x7, 0x7, 0x7, 0x0, 0x0, 0x0, 0x0, 0xF, 0xF, 0xF, 0xF };
        byte[] b = { 0x0, 0x7, 0x0, 0x7, 0x0, 0x7, 0x0, 0x7, 0x0, 0xF, 0x0, 0xF, 0x0, 0xF, 0x0, 0xF };

        for (int i = 0; i < 16; i++)
        {
            _red[i] = r[i];
            _green[i] = g[i];
            _blue[i] = b[i];
            UpdatePaletteEntry(i);
        }
    }

    private void UpdatePaletteEntry(int index)
    {
        // Convert 4-bit (0-15) to 8-bit (0-255): multiply by 17
        uint r8 = (uint)(_red[index] * 17);
        uint g8 = (uint)(_green[index] * 17);
        uint b8 = (uint)(_blue[index] * 17);
        Palette[index] = 0xFF000000 | (r8 << 16) | (g8 << 8) | b8;
    }

    public byte ReadByte(int port)
    {
        return port switch
        {
            0xA8 => _index,
            0xAA => _green[_index & 0x0F],
            0xAC => _red[_index & 0x0F],
            0xAE => _blue[_index & 0x0F],
            _ => 0xFF
        };
    }

    public void WriteByte(int port, byte value)
    {
        switch (port)
        {
            case 0xA8:
                _index = (byte)(value & 0x0F);
                break;
            case 0xAA:
                _green[_index & 0x0F] = (byte)(value & 0x0F);
                UpdatePaletteEntry(_index & 0x0F);
                break;
            case 0xAC:
                _red[_index & 0x0F] = (byte)(value & 0x0F);
                UpdatePaletteEntry(_index & 0x0F);
                break;
            case 0xAE:
                _blue[_index & 0x0F] = (byte)(value & 0x0F);
                UpdatePaletteEntry(_index & 0x0F);
                break;
        }
    }

    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) => WriteByte(port, (byte)value);
    public void Reset() => SetDefaultPalette();
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0xA8, 0xAA, 0xAC, 0xAE };
}
