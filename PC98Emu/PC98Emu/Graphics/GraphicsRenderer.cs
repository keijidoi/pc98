using PC98Emu.Bus;

namespace PC98Emu.Graphics;

/// <summary>
/// Renders the PC-98 graphics VRAM (4 bitplanes) to an RGBA framebuffer.
/// Each plane is 32000 bytes (640x400 pixels, 1 bit per pixel).
/// Planes are combined to produce a 4-bit (16-color) digital palette index.
/// </summary>
public class GraphicsRenderer
{
    private readonly byte[] _memory;
    private readonly SystemBus _bus;

    /// <summary>
    /// 4 graphics VRAM planes, each 32000 bytes (640x400 / 8).
    /// </summary>
    public readonly byte[][] Planes = new byte[4][];

    private const int PlaneSize = 32000; // 640 * 400 / 8
    private const int GvramStart = 0xA8000;
    private const int GvramEnd = 0xBFFFF;

    // Default 16-color digital palette (ARGB)
    private static readonly uint[] DefaultPalette = new uint[]
    {
        0xFF000000, // 0: Black
        0xFF0000AA, // 1: Blue
        0xFFAA0000, // 2: Red
        0xFFAA00AA, // 3: Magenta
        0xFF00AA00, // 4: Green
        0xFF00AAAA, // 5: Cyan
        0xFFAAAA00, // 6: Yellow
        0xFFAAAAAA, // 7: White (light gray)
        0xFF555555, // 8: Dark gray
        0xFF5555FF, // 9: Bright blue
        0xFFFF5555, // 10: Bright red
        0xFFFF55FF, // 11: Bright magenta
        0xFF55FF55, // 12: Bright green
        0xFF55FFFF, // 13: Bright cyan
        0xFFFFFF55, // 14: Bright yellow
        0xFFFFFFFF, // 15: Bright white
    };

    public GraphicsRenderer(byte[] memory, SystemBus bus)
    {
        _memory = memory;
        _bus = bus;

        for (int i = 0; i < 4; i++)
            Planes[i] = new byte[PlaneSize];
    }

    /// <summary>
    /// Read a byte from GVRAM at the given address, using the current write plane.
    /// Called by SystemBus for CPU reads from 0xA8000-0xBFFFF.
    /// </summary>
    public byte ReadGvram(int address)
    {
        int offset = address - GvramStart;
        if (offset < 0 || offset >= PlaneSize) return 0xFF;

        int plane = _bus.GvramWritePlane & 0x03;
        return Planes[plane][offset];
    }

    /// <summary>
    /// Write a byte to GVRAM at the given address, using the current write plane.
    /// Called by SystemBus for CPU writes to 0xA8000-0xBFFFF.
    /// </summary>
    public void WriteGvram(int address, byte value)
    {
        int offset = address - GvramStart;
        if (offset < 0 || offset >= PlaneSize) return;

        int plane = _bus.GvramWritePlane & 0x03;
        Planes[plane][offset] = value;
    }

    /// <summary>
    /// Render all 4 graphics planes to the framebuffer.
    /// Combines planes bit-by-bit to get a 4-bit palette index per pixel.
    /// </summary>
    public void Render(uint[] framebuffer, int width, int height)
    {
        int maxBytes = Math.Min(PlaneSize, (width * height + 7) / 8);

        for (int byteIdx = 0; byteIdx < maxBytes; byteIdx++)
        {
            byte p0 = Planes[0][byteIdx];
            byte p1 = Planes[1][byteIdx];
            byte p2 = Planes[2][byteIdx];
            byte p3 = Planes[3][byteIdx];

            for (int bit = 7; bit >= 0; bit--)
            {
                int pixelIndex = byteIdx * 8 + (7 - bit);
                if (pixelIndex >= width * height) break;

                int colorIndex = ((p3 >> bit) & 1) << 3
                               | ((p2 >> bit) & 1) << 2
                               | ((p1 >> bit) & 1) << 1
                               | ((p0 >> bit) & 1);

                framebuffer[pixelIndex] = DefaultPalette[colorIndex];
            }
        }
    }

    /// <summary>
    /// Check if an address is in the GVRAM range.
    /// </summary>
    public static bool IsGvramAddress(int address)
    {
        return address >= GvramStart && address <= GvramEnd;
    }
}
