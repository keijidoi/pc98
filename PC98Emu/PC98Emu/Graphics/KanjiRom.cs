using System.Runtime.InteropServices;

namespace PC98Emu.Graphics;

/// <summary>
/// Generates 16×16 kanji and 8×16 half-width katakana font bitmaps
/// using Windows GDI, rendering from the "ＭＳ ゴシック" system font.
/// </summary>
public class KanjiRom : IDisposable
{
    private readonly Dictionary<ushort, byte[]> _kanjiCache = new();
    private readonly Dictionary<byte, byte[]> _ankExtCache = new();
    private readonly IntPtr _hdc;
    private readonly IntPtr _hBitmap;
    private readonly IntPtr _hFont16;
    private readonly IntPtr _bitmapBits;
    private readonly IntPtr _oldBitmap;
    private IntPtr _currentFont;
    private bool _disposed;
    private bool _available;

    // Bitmap is 16x16 (largest glyph), reused for all rendering
    private const int BmpSize = 16;

    #region GDI P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO { public BITMAPINFOHEADER bmiHeader; }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFontW(int h, int w, int esc, int orient, int weight,
        uint italic, uint underline, uint strikeout, uint charset, uint outPrec, uint clipPrec,
        uint quality, uint pitchFamily, string face);

    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll")] private static extern uint SetTextColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll")] private static extern uint SetBkColor(IntPtr hdc, uint color);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern bool TextOutW(IntPtr hdc, int x, int y, string str, int count);
    [DllImport("gdi32.dll")] private static extern bool PatBlt(IntPtr hdc, int x, int y, int w, int h, uint rop);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("kernel32.dll")]
    private static extern int MultiByteToWideChar(uint cp, uint flags, byte[] mbStr, int cbMb, [Out] char[] wStr, int ccWc);

    #endregion

    public bool Available => _available;

    public KanjiRom()
    {
        try
        {
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            _hdc = CreateCompatibleDC(IntPtr.Zero);
            if (_hdc == IntPtr.Zero) return;

            var bmi = new BITMAPINFO();
            bmi.bmiHeader.biSize = Marshal.SizeOf<BITMAPINFOHEADER>();
            bmi.bmiHeader.biWidth = BmpSize;
            bmi.bmiHeader.biHeight = -BmpSize; // top-down
            bmi.bmiHeader.biPlanes = 1;
            bmi.bmiHeader.biBitCount = 32;

            _hBitmap = CreateDIBSection(_hdc, ref bmi, 0, out _bitmapBits, IntPtr.Zero, 0);
            if (_hBitmap == IntPtr.Zero) { DeleteDC(_hdc); return; }
            _oldBitmap = SelectObject(_hdc, _hBitmap);

            // 16px font for full-width kanji (16×16)
            _hFont16 = CreateFontW(16, 0, 0, 0, 400, 0, 0, 0,
                128 /*SHIFTJIS*/, 0, 0, 3 /*NONANTIALIASED*/, 0x31 /*FIXED|FF_MODERN*/,
                "ＭＳ ゴシック");
            if (_hFont16 == IntPtr.Zero)
            {
                // Fallback to generic fixed-pitch Japanese font
                _hFont16 = CreateFontW(16, 0, 0, 0, 400, 0, 0, 0,
                    128, 0, 0, 3, 0x31, "MS Gothic");
            }
            _currentFont = SelectObject(_hdc, _hFont16);

            SetBkMode(_hdc, 2 /*OPAQUE*/);
            SetBkColor(_hdc, 0x00000000);
            SetTextColor(_hdc, 0x00FFFFFF);

            _available = true;
            Console.Error.WriteLine("[FONT] KanjiRom initialized via GDI (MS Gothic)");
        }
        catch
        {
            _available = false;
            Console.Error.WriteLine("[FONT] KanjiRom GDI init failed, using placeholders");
        }
    }

    /// <summary>
    /// Get 16×16 kanji glyph (32 bytes) for a JIS X 0208 code.
    /// </summary>
    public byte[] GetKanjiGlyph(ushort jisCode)
    {
        if (_kanjiCache.TryGetValue(jisCode, out var cached))
            return cached;

        byte[] glyph;
        if (_available)
        {
            char ch = JisToUnicode(jisCode);
            Console.Error.WriteLine($"[FONT] JIS {jisCode:X4} → U+{(int)ch:X4} '{ch}'");
            glyph = ch != '\0' ? RenderGlyph16(ch) : MakePlaceholder16();
        }
        else
        {
            glyph = MakePlaceholder16();
        }
        _kanjiCache[jisCode] = glyph;
        return glyph;
    }

    /// <summary>
    /// Get 8×16 glyph (16 bytes) for an extended ANK character (0x80-0xFF).
    /// Half-width katakana: 0xA1-0xDF → Unicode U+FF61-U+FF9F.
    /// </summary>
    public byte[] GetAnkExtGlyph(byte code)
    {
        if (_ankExtCache.TryGetValue(code, out var cached))
            return cached;

        byte[] glyph;
        if (_available)
        {
            char ch = AnkExtToUnicode(code);
            glyph = ch != '\0' ? RenderGlyph8(ch) : MakePlaceholder8();
        }
        else
        {
            glyph = MakePlaceholder8();
        }
        _ankExtCache[code] = glyph;
        return glyph;
    }

    private byte[] RenderGlyph16(char ch)
    {
        PatBlt(_hdc, 0, 0, BmpSize, BmpSize, 0x00000042 /*BLACKNESS*/);
        string s = ch.ToString();
        TextOutW(_hdc, 0, 0, s, s.Length);

        var glyph = new byte[32];
        for (int y = 0; y < 16; y++)
        {
            byte left = 0, right = 0;
            for (int x = 0; x < 8; x++)
            {
                int offset = (y * BmpSize + x) * 4;
                int px = Marshal.ReadInt32(_bitmapBits + offset);
                if ((px & 0xFF) > 0x40)
                    left |= (byte)(0x80 >> x);
            }
            for (int x = 0; x < 8; x++)
            {
                int offset = (y * BmpSize + x + 8) * 4;
                int px = Marshal.ReadInt32(_bitmapBits + offset);
                if ((px & 0xFF) > 0x40)
                    right |= (byte)(0x80 >> x);
            }
            glyph[y * 2] = left;
            glyph[y * 2 + 1] = right;
        }
        return glyph;
    }

    private byte[] RenderGlyph8(char ch)
    {
        PatBlt(_hdc, 0, 0, BmpSize, BmpSize, 0x00000042);
        string s = ch.ToString();
        TextOutW(_hdc, 0, 0, s, s.Length);

        var glyph = new byte[16];
        for (int y = 0; y < 16; y++)
        {
            byte row = 0;
            for (int x = 0; x < 8; x++)
            {
                int offset = (y * BmpSize + x) * 4;
                int px = Marshal.ReadInt32(_bitmapBits + offset);
                if ((px & 0xFF) > 0x40)
                    row |= (byte)(0x80 >> x);
            }
            glyph[y] = row;
        }
        return glyph;
    }

    /// <summary>
    /// Convert JIS X 0208 code to Unicode via Shift-JIS intermediate.
    /// </summary>
    private static char JisToUnicode(ushort jisCode)
    {
        int row = (jisCode >> 8) & 0x7F;
        int col = jisCode & 0x7F;
        if (row < 0x21 || row > 0x7E || col < 0x21 || col > 0x7E)
            return '\0';

        int sjisHi, sjisLo;
        if ((row & 1) != 0)
        {
            sjisHi = (row + 1) / 2 + 0x70;
            sjisLo = col + 0x1F;
            if (sjisLo >= 0x7F) sjisLo++;
        }
        else
        {
            sjisHi = row / 2 + 0x70;
            sjisLo = col + 0x7E;
        }
        if (sjisHi >= 0xA0) sjisHi += 0x40;

        byte[] sjis = { (byte)sjisHi, (byte)sjisLo };
        try
        {
            string s = System.Text.Encoding.GetEncoding(932).GetString(sjis);
            return s.Length > 0 ? s[0] : '\0';
        }
        catch { return '\0'; }
    }

    /// <summary>
    /// Convert PC-98 extended ANK code to Unicode.
    /// 0xA1-0xDF: half-width katakana (JIS X 0201) → U+FF61-U+FF9F
    /// 0x80-0xA0, 0xE0-0xFF: other extended characters
    /// </summary>
    private static char AnkExtToUnicode(byte code)
    {
        if (code >= 0xA1 && code <= 0xDF)
            return (char)(0xFF61 + (code - 0xA1));
        // Other extended ranges - try SJIS single-byte mapping
        byte[] sjis = { code };
        try
        {
            string s = System.Text.Encoding.GetEncoding(932).GetString(sjis);
            return s.Length > 0 ? s[0] : '\0';
        }
        catch { return '\0'; }
    }

    private static byte[] MakePlaceholder16()
    {
        var g = new byte[32];
        g[0] = 0xFF; g[1] = 0xFF;
        g[30] = 0xFF; g[31] = 0xFF;
        for (int r = 1; r < 15; r++) { g[r * 2] = 0x80; g[r * 2 + 1] = 0x01; }
        return g;
    }

    private static byte[] MakePlaceholder8()
    {
        return new byte[] {
            0x00, 0x00, 0x7E, 0x42, 0x42, 0x42, 0x42, 0x42,
            0x42, 0x7E, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (!_available) return;
        SelectObject(_hdc, _currentFont);
        SelectObject(_hdc, _oldBitmap);
        if (_hFont16 != IntPtr.Zero) DeleteObject(_hFont16);
        if (_hBitmap != IntPtr.Zero) DeleteObject(_hBitmap);
        if (_hdc != IntPtr.Zero) DeleteDC(_hdc);
    }
}
