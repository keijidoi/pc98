namespace PC98Emu.Graphics;

/// <summary>
/// Renders the PC-98 text VRAM layer to an RGBA framebuffer.
/// Text VRAM: 0xA0000-0xA1FFF = character codes, 0xA2000-0xA3FFF = attributes.
/// 80 columns x 25 rows, each character is 2 bytes.
/// </summary>
public class TextRenderer
{
    private readonly byte[] _memory;

    private const int TextVramBase = 0xA0000;
    private const int AttrVramBase = 0xA2000;
    private const int Columns = 80;
    private const int Rows = 25;
    private const int CharWidth = 8;
    private const int CharHeight = 16;

    // PC-98 digital 8-color palette (GRB order in attribute bits 0-2)
    // Bit 0 = Blue, Bit 1 = Red, Bit 2 = Green
    private static readonly uint[] ColorPalette = new uint[]
    {
        0xFF000000, // 0: Black
        0xFF0000FF, // 1: Blue
        0xFFFF0000, // 2: Red
        0xFFFF00FF, // 3: Magenta (Red + Blue)
        0xFF00FF00, // 4: Green
        0xFF00FFFF, // 5: Cyan (Green + Blue)
        0xFFFFFF00, // 6: Yellow (Green + Red)
        0xFFFFFFFF, // 7: White
    };

    public TextRenderer(byte[] memory)
    {
        _memory = memory;
    }

    public void Render(uint[] framebuffer, int width, int height)
    {
        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                int charAddr = TextVramBase + (row * Columns + col) * 2;
                int attrAddr = AttrVramBase + (row * Columns + col) * 2;

                ushort charCode = (ushort)(_memory[charAddr] | (_memory[charAddr + 1] << 8));
                byte attr = _memory[attrAddr];

                // Attribute bits:
                // bits 0-2: color (GRB)
                // bit 3: blink
                // bit 4: reverse
                // bit 5: underline
                // bit 6: vertical line
                // bit 7: visible
                bool visible = (attr & 0x80) != 0;
                bool reverse = (attr & 0x10) != 0;
                bool underline = (attr & 0x20) != 0;
                int colorIndex = attr & 0x07;

                if (!visible)
                    continue;

                uint fgColor = ColorPalette[colorIndex];
                uint bgColor = 0x00000000; // Transparent background

                if (reverse)
                {
                    (fgColor, bgColor) = (bgColor, fgColor);
                }

                if (charCode < 0x100)
                {
                    // ANK character (8x16)
                    RenderAnkChar((byte)charCode, framebuffer, width, col * CharWidth, row * CharHeight, fgColor, bgColor, underline);
                }
                else
                {
                    // JIS Kanji character (16x16, occupies 2 cells)
                    RenderKanjiChar(charCode, framebuffer, width, col * CharWidth, row * CharHeight, fgColor, bgColor, underline);
                    col++; // Skip next column (kanji occupies 2 cells)
                }
            }
        }
    }

    private static void RenderAnkChar(byte charCode, uint[] framebuffer, int width, int x, int y, uint fgColor, uint bgColor, bool underline)
    {
        var glyph = Font.GetAnkGlyph(charCode);

        for (int row = 0; row < CharHeight; row++)
        {
            int py = y + row;
            if (py >= 400) break;

            byte glyphRow = glyph[row];
            // Don't draw underline on space/null characters (prevents blue horizontal lines)
            bool isUnderlineRow = underline && row == CharHeight - 1 && charCode > 0x20;

            for (int bit = 0; bit < CharWidth; bit++)
            {
                int px = x + bit;
                if (px >= width) break;

                bool pixelOn = ((glyphRow >> (7 - bit)) & 1) != 0 || isUnderlineRow;
                uint color = pixelOn ? fgColor : bgColor;

                if (color != 0x00000000) // Only write non-transparent pixels
                {
                    framebuffer[py * width + px] = color;
                }
            }
        }
    }

    private static void RenderKanjiChar(ushort jisCode, uint[] framebuffer, int width, int x, int y, uint fgColor, uint bgColor, bool underline)
    {
        var glyph = Font.GetKanjiGlyph(jisCode);

        for (int row = 0; row < 16; row++)
        {
            int py = y + row;
            if (py >= 400) break;

            byte glyphLeft = glyph[row * 2];
            byte glyphRight = glyph[row * 2 + 1];
            bool isUnderlineRow = underline && row == 15;

            // Left 8 pixels
            for (int bit = 0; bit < 8; bit++)
            {
                int px = x + bit;
                if (px >= width) break;

                bool pixelOn = ((glyphLeft >> (7 - bit)) & 1) != 0 || isUnderlineRow;
                uint color = pixelOn ? fgColor : bgColor;

                if (color != 0x00000000)
                    framebuffer[py * width + px] = color;
            }

            // Right 8 pixels
            for (int bit = 0; bit < 8; bit++)
            {
                int px = x + 8 + bit;
                if (px >= width) break;

                bool pixelOn = ((glyphRight >> (7 - bit)) & 1) != 0 || isUnderlineRow;
                uint color = pixelOn ? fgColor : bgColor;

                if (color != 0x00000000)
                    framebuffer[py * width + px] = color;
            }
        }
    }
}
