using PC98Emu.Bus;

namespace PC98Emu.Graphics;

/// <summary>
/// Renders the PC-98 graphics VRAM (4 bitplanes) to an RGBA framebuffer.
/// PC-98 GVRAM layout (640x400, 16-color mode):
///   Plane 0 (Blue):      0xA8000-0xAFFFF (32KB)
///   Plane 1 (Red):       0xB0000-0xB7FFF (32KB)
///   Plane 2 (Green):     0xB8000-0xBFFFF (32KB)
///   Plane 3 (Intensity): 0xE0000-0xE7FFF (32KB)
/// Each plane stores 1 bit per pixel, combined for 4-bit color index.
/// </summary>
public class GraphicsRenderer
{
    private readonly byte[] _memory;
    private readonly SystemBus _bus;
    private readonly AnalogPalette? _palette;

    private const int PlaneSize = 32000; // 640 * 400 / 8

    // PC-98 GVRAM plane base addresses
    private const int Plane0Base = 0xA8000; // Blue
    private const int Plane1Base = 0xB0000; // Red
    private const int Plane2Base = 0xB8000; // Green
    private const int Plane3Base = 0xE0000; // Intensity

    private const int GvramStart = 0xA8000;
    private const int GvramEnd = 0xBFFFF;

    // Fallback 16-color digital palette (used if no analog palette)
    private static readonly uint[] DefaultPalette = new uint[]
    {
        0xFF000000, 0xFF0000AA, 0xFFAA0000, 0xFFAA00AA,
        0xFF00AA00, 0xFF00AAAA, 0xFFAAAA00, 0xFFAAAAAA,
        0xFF555555, 0xFF5555FF, 0xFFFF5555, 0xFFFF55FF,
        0xFF55FF55, 0xFF55FFFF, 0xFFFFFF55, 0xFFFFFFFF,
    };

    public GraphicsRenderer(byte[] memory, SystemBus bus, AnalogPalette? palette = null)
    {
        _memory = memory;
        _bus = bus;
        _palette = palette;
    }

    /// <summary>
    /// Render all 4 graphics planes to the framebuffer.
    /// Reads directly from the flat memory array at the correct plane addresses.
    /// </summary>
    public void Render(uint[] framebuffer, int width, int height)
    {
        int maxBytes = Math.Min(PlaneSize, (width * height + 7) / 8);

        for (int byteIdx = 0; byteIdx < maxBytes; byteIdx++)
        {
            byte p0 = _memory[Plane0Base + byteIdx]; // Blue
            byte p1 = _memory[Plane1Base + byteIdx]; // Red
            byte p2 = _memory[Plane2Base + byteIdx]; // Green
            byte p3 = _memory[Plane3Base + byteIdx]; // Intensity

            for (int bit = 7; bit >= 0; bit--)
            {
                int pixelIndex = byteIdx * 8 + (7 - bit);
                if (pixelIndex >= width * height) break;

                int colorIndex = ((p3 >> bit) & 1) << 3
                               | ((p2 >> bit) & 1) << 2
                               | ((p1 >> bit) & 1) << 1
                               | ((p0 >> bit) & 1);

                framebuffer[pixelIndex] = _palette != null ? _palette.Palette[colorIndex] : DefaultPalette[colorIndex];
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
