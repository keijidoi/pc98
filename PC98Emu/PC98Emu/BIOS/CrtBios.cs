using PC98Emu.CPU;
using PC98Emu.Bus;

namespace PC98Emu.BIOS;

public class CrtBios
{
    private readonly V30 _cpu;
    private readonly SystemBus _bus;

    // BDA addresses for cursor position
    private const int BDA_CURSOR_COL = 0x053C;
    private const int BDA_CURSOR_ROW = 0x053E;
    private const int BDA_DISPLAY_MODE = 0x0480;

    // Text VRAM layout
    private const int TEXT_VRAM_BASE = 0xA0000;
    private const int TEXT_VRAM_END = 0xA1FFF;
    private const int ATTR_VRAM_BASE = 0xA2000;
    private const int ATTR_VRAM_END = 0xA3FFF;
    private const int COLS = 80;
    private const int ROWS = 25;

    public CrtBios(V30 cpu, SystemBus bus)
    {
        _cpu = cpu;
        _bus = bus;
    }

    public void Handle()
    {
        byte func = _cpu.AH;
        switch (func)
        {
            case 0x00:
                SetCursorPosition();
                break;
            case 0x01:
                GetCursorPosition();
                break;
            case 0x06:
                ClearScreen();
                break;
            case 0x0A:
                WriteCharAtCursor();
                break;
            case 0x0E:
                TeletypeOutput();
                break;
            case 0x12:
                SetDisplayMode();
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
        // Fill text VRAM with spaces (0x0020)
        for (int addr = TEXT_VRAM_BASE; addr <= TEXT_VRAM_END; addr += 2)
        {
            _bus.WriteMemoryWord(addr, 0x0020);
        }
        // Fill attribute VRAM with white visible (0x00E1)
        for (int addr = ATTR_VRAM_BASE; addr <= ATTR_VRAM_END; addr += 2)
        {
            _bus.WriteMemoryWord(addr, 0x00E1);
        }
        // Reset cursor
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
    }

    private void TeletypeOutput()
    {
        byte ch = _cpu.AL;
        byte row = _bus.ReadMemoryByte(BDA_CURSOR_ROW);
        byte col = _bus.ReadMemoryByte(BDA_CURSOR_COL);

        if (ch == 0x0A) // newline
        {
            row++;
            col = 0;
        }
        else if (ch == 0x0D) // carriage return
        {
            col = 0;
        }
        else
        {
            int offset = (row * COLS + col) * 2;
            int addr = TEXT_VRAM_BASE + offset;
            _bus.WriteMemoryWord(addr, ch);
            col++;
            if (col >= COLS)
            {
                col = 0;
                row++;
            }
        }

        if (row >= ROWS)
        {
            // Simple scroll: move rows up, clear last row
            ScrollUp();
            row = (byte)(ROWS - 1);
        }

        _bus.WriteMemoryByte(BDA_CURSOR_ROW, row);
        _bus.WriteMemoryByte(BDA_CURSOR_COL, col);
    }

    private void ScrollUp()
    {
        // Move each row up by one
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
        // Clear last row
        int lastRowOff = (ROWS - 1) * COLS * 2;
        for (int c = 0; c < COLS; c++)
        {
            _bus.WriteMemoryWord(TEXT_VRAM_BASE + lastRowOff + c * 2, 0x0020);
            _bus.WriteMemoryWord(ATTR_VRAM_BASE + lastRowOff + c * 2, 0x00E1);
        }
    }

    private void SetDisplayMode()
    {
        _bus.WriteMemoryByte(BDA_DISPLAY_MODE, _cpu.AL);
    }
}
