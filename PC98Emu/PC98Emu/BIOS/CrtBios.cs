using PC98Emu.CPU;
using PC98Emu.Bus;
using PC98Emu.Graphics;

namespace PC98Emu.BIOS;

public class CrtBios
{
    private readonly V30 _cpu;
    private readonly SystemBus _bus;
    private readonly GDC? _textGdc;

    // BDA addresses for cursor position
    private const int BDA_CURSOR_COL = 0x0460;
    private const int BDA_CURSOR_ROW = 0x0462;
    private const int BDA_DISPLAY_PAGE = 0x0464;

    // Text VRAM layout
    private const int TEXT_VRAM_BASE = 0xA0000;
    private const int TEXT_VRAM_END = 0xA1FFF;
    private const int ATTR_VRAM_BASE = 0xA2000;
    private const int ATTR_VRAM_END = 0xA3FFF;
    private const int COLS = 80;
    private const int ROWS = 25;

    public CrtBios(V30 cpu, SystemBus bus, GDC? textGdc = null)
    {
        _cpu = cpu;
        _bus = bus;
        _textGdc = textGdc;
    }

    public void Handle()
    {
        byte func = _cpu.AH;
        Console.Error.WriteLine($"[CRT] AH={func:X2} AL={_cpu.AL:X2} BX={_cpu.BX:X4} CX={_cpu.CX:X4} DX={_cpu.DX:X4}");
        switch (func)
        {
            case 0x00:
                SetCursorPosition();
                break;
            case 0x01:
                GetCursorPosition();
                break;
            case 0x02:
                // Set cursor type/shape - stub
                break;
            case 0x03:
                // Set cursor appearance - stub
                break;
            case 0x06:
                ClearScreen();
                break;
            case 0x0A:
                WriteCharAtCursor();
                break;
            case 0x0C:
                // Start CRT display (enable text plane)
                _textGdc?.Start();
                break;
            case 0x0D:
                // Stop CRT display (disable text plane)
                _textGdc?.Stop();
                break;
            case 0x0E:
                TeletypeOutput();
                break;
            case 0x11:
                // Set character display mode - stub (80x25 always)
                break;
            case 0x12:
                SetDisplayMode();
                break;
            case 0x13:
                SetAttribute();
                break;
            case 0x14:
                // Read character at cursor - return space
                _cpu.AL = 0x20;
                break;
            case 0x16:
                // Set VRAM access mode - stub
                break;
            case 0x1A:
                // Read text VRAM - stub
                break;
            case 0x1C:
                // Set text screen mode - stub (80x25 always)
                break;
            default:
                _cpu.AH = 0;
                break;
        }
    }

    private void SetCursorPosition()
    {
        byte row = _cpu.DH;
        byte col = _cpu.DL;
        _bus.WriteMemoryByte(BDA_CURSOR_ROW, row);
        _bus.WriteMemoryByte(BDA_CURSOR_COL, col);
    }

    private void GetCursorPosition()
    {
        _cpu.DH = _bus.ReadMemoryByte(BDA_CURSOR_ROW);
        _cpu.DL = _bus.ReadMemoryByte(BDA_CURSOR_COL);
    }

    private void ClearScreen()
    {
        for (int addr = TEXT_VRAM_BASE; addr <= TEXT_VRAM_END; addr += 2)
            _bus.WriteMemoryWord(addr, 0x0020);
        for (int addr = ATTR_VRAM_BASE; addr <= ATTR_VRAM_END; addr += 2)
            _bus.WriteMemoryWord(addr, 0x0087);
        _bus.WriteMemoryByte(BDA_CURSOR_ROW, 0);
        _bus.WriteMemoryByte(BDA_CURSOR_COL, 0);
    }

    private void WriteCharAtCursor()
    {
        byte row = _bus.ReadMemoryByte(BDA_CURSOR_ROW);
        byte col = _bus.ReadMemoryByte(BDA_CURSOR_COL);
        int offset = (row * COLS + col) * 2;
        int addr = TEXT_VRAM_BASE + offset;
        _bus.WriteMemoryWord(addr, _cpu.AL);
        // Set visible attribute so the character actually renders
        _bus.WriteMemoryWord(ATTR_VRAM_BASE + offset, 0xE1);
    }

    /// <summary>
    /// INT 18h AH=13: Set character attribute at VRAM offset.
    /// DX = byte offset in attribute VRAM
    /// AL = attribute byte (bit7=visible, bit4=reverse, bit3=blink, bits0-2=color GRB)
    /// </summary>
    private void SetAttribute()
    {
        int offset = _cpu.DX;
        if (offset <= ATTR_VRAM_END - ATTR_VRAM_BASE)
        {
            _bus.WriteMemoryByte(ATTR_VRAM_BASE + offset, _cpu.AL);
        }
    }

    private void TeletypeOutput()
    {
        byte ch = _cpu.AL;
        byte row = _bus.ReadMemoryByte(BDA_CURSOR_ROW);
        byte col = _bus.ReadMemoryByte(BDA_CURSOR_COL);
        Console.Error.WriteLine($"[CRT] Teletype ch={ch:X2}('{(ch >= 0x20 && ch < 0x7F ? (char)ch : '?')}') at ({row},{col})");

        if (ch == 0x0A)
        {
            row++;
            col = 0;
        }
        else if (ch == 0x0D)
        {
            col = 0;
        }
        else
        {
            int offset = (row * COLS + col) * 2;
            int addr = TEXT_VRAM_BASE + offset;
            _bus.WriteMemoryWord(addr, ch);
            // Set visible attribute so the character actually renders
            _bus.WriteMemoryWord(ATTR_VRAM_BASE + offset, 0xE1);
            col++;
            if (col >= COLS)
            {
                col = 0;
                row++;
            }
        }

        if (row >= ROWS)
        {
            ScrollUp();
            row = (byte)(ROWS - 1);
        }

        _bus.WriteMemoryByte(BDA_CURSOR_ROW, row);
        _bus.WriteMemoryByte(BDA_CURSOR_COL, col);
    }

    private void ScrollUp()
    {
        for (int r = 1; r < ROWS; r++)
        {
            int srcOff = r * COLS * 2;
            int dstOff = (r - 1) * COLS * 2;
            for (int c = 0; c < COLS * 2; c++)
            {
                byte val = _bus.ReadMemoryByte(TEXT_VRAM_BASE + srcOff + c);
                _bus.WriteMemoryByte(TEXT_VRAM_BASE + dstOff + c, val);
                byte attr = _bus.ReadMemoryByte(ATTR_VRAM_BASE + srcOff + c);
                _bus.WriteMemoryByte(ATTR_VRAM_BASE + dstOff + c, attr);
            }
        }
        int lastRowOff = (ROWS - 1) * COLS * 2;
        for (int c = 0; c < COLS; c++)
        {
            _bus.WriteMemoryWord(TEXT_VRAM_BASE + lastRowOff + c * 2, 0x0020);
            _bus.WriteMemoryWord(ATTR_VRAM_BASE + lastRowOff + c * 2, 0x0087);
        }
    }

    private void SetDisplayMode()
    {
        _bus.WriteMemoryByte(BDA_DISPLAY_PAGE, _cpu.AL);
    }
}
