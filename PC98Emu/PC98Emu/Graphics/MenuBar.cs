using System.Runtime.InteropServices;

namespace PC98Emu.Graphics;

public class MenuBar
{
    public const int MenuBarHeight = 24; // pixels (not scaled)

    private bool _dropdownOpen;
    private int _hoverItem = -1;
    private readonly List<MenuItem> _menuItems = new();

    public Action<string>? OnHdiSelected { get; set; }
    public Action? OnExitRequested { get; set; }

    public MenuBar()
    {
        _menuItems.Add(new MenuItem("File", new[]
        {
            new SubMenuItem("Open HDI...", OnOpenHdi),
            new SubMenuItem("Exit", OnExit),
        }));
    }

    private void OnOpenHdi()
    {
        string? path = FileDialog.OpenHdiFile();
        if (path != null)
            OnHdiSelected?.Invoke(path);
        _dropdownOpen = false;
    }

    private void OnExit()
    {
        OnExitRequested?.Invoke();
    }

    public bool HandleMouseClick(int x, int y)
    {
        if (y < MenuBarHeight)
        {
            int itemX = 8;
            for (int i = 0; i < _menuItems.Count; i++)
            {
                int itemWidth = _menuItems[i].Name.Length * 8 + 16;
                if (x >= itemX && x < itemX + itemWidth)
                {
                    _dropdownOpen = !_dropdownOpen || _hoverItem != i;
                    _hoverItem = i;
                    return true;
                }
                itemX += itemWidth;
            }
            _dropdownOpen = false;
            return true;
        }

        if (_dropdownOpen && _hoverItem >= 0 && _hoverItem < _menuItems.Count)
        {
            var item = _menuItems[_hoverItem];
            int dropX = 8;
            for (int i = 0; i < _hoverItem; i++)
                dropX += _menuItems[i].Name.Length * 8 + 16;

            int dropWidth = 160;
            int subItemHeight = 20;

            if (x >= dropX && x < dropX + dropWidth)
            {
                int idx = (y - MenuBarHeight) / subItemHeight;
                if (idx >= 0 && idx < item.SubItems.Length)
                {
                    item.SubItems[idx].Action?.Invoke();
                    _dropdownOpen = false;
                    return true;
                }
            }
            _dropdownOpen = false;
        }

        return false;
    }

    public void Render(uint[] buffer, int screenWidth)
    {
        uint bgColor = 0xFF333333;
        uint textColor = 0xFFFFFFFF;
        uint hoverBg = 0xFF555555;

        for (int i = 0; i < screenWidth * MenuBarHeight; i++)
            buffer[i] = bgColor;

        for (int x = 0; x < screenWidth; x++)
            buffer[(MenuBarHeight - 1) * screenWidth + x] = 0xFF666666;

        int itemX = 8;
        for (int i = 0; i < _menuItems.Count; i++)
        {
            int itemWidth = _menuItems[i].Name.Length * 8 + 16;
            if (_dropdownOpen && _hoverItem == i)
            {
                for (int py = 1; py < MenuBarHeight - 1; py++)
                    for (int px = itemX; px < itemX + itemWidth && px < screenWidth; px++)
                        buffer[py * screenWidth + px] = hoverBg;
            }
            DrawString(buffer, screenWidth, _menuItems[i].Name, itemX + 8, 4, textColor);
            itemX += itemWidth;
        }
    }

    public void RenderDropdown(uint[] buffer, int screenWidth, int screenHeight)
    {
        if (!_dropdownOpen || _hoverItem < 0 || _hoverItem >= _menuItems.Count)
            return;

        var item = _menuItems[_hoverItem];
        int dropX = 8;
        for (int i = 0; i < _hoverItem; i++)
            dropX += _menuItems[i].Name.Length * 8 + 16;

        int dropWidth = 160;
        int subItemHeight = 20;
        int dropHeight = item.SubItems.Length * subItemHeight + 2;

        uint dropBg = 0xFF444444;
        uint dropBorder = 0xFF888888;
        uint textColor = 0xFFFFFFFF;

        for (int py = 0; py < dropHeight && py < screenHeight; py++)
        {
            for (int px = dropX; px < dropX + dropWidth && px < screenWidth; px++)
            {
                int idx = py * screenWidth + px;
                if (idx < buffer.Length)
                {
                    if (py == 0 || py == dropHeight - 1 || px == dropX || px == dropX + dropWidth - 1)
                        buffer[idx] = dropBorder;
                    else
                        buffer[idx] = dropBg;
                }
            }
        }

        for (int si = 0; si < item.SubItems.Length; si++)
        {
            int textY = si * subItemHeight + 3;
            DrawString(buffer, screenWidth, item.SubItems[si].Name, dropX + 12, textY, textColor);
        }
    }

    public bool IsDropdownOpen => _dropdownOpen;

    private static void DrawString(uint[] buffer, int stride, string text, int x, int y, uint color)
    {
        for (int ci = 0; ci < text.Length; ci++)
        {
            byte ch = (byte)text[ci];
            var glyph = Font.GetAnkGlyph(ch);
            for (int row = 0; row < 16; row++)
            {
                int py = y + row;
                if (py < 0 || py * stride >= buffer.Length) continue;
                byte bits = glyph[row];
                for (int col = 0; col < 8; col++)
                {
                    int px = x + ci * 8 + col;
                    if (px < 0 || px >= stride) continue;
                    int idx = py * stride + px;
                    if (idx >= buffer.Length) continue;
                    if ((bits & (0x80 >> col)) != 0)
                        buffer[idx] = color;
                }
            }
        }
    }

    private class MenuItem
    {
        public string Name { get; }
        public SubMenuItem[] SubItems { get; }
        public MenuItem(string name, SubMenuItem[] subItems) { Name = name; SubItems = subItems; }
    }

    private class SubMenuItem
    {
        public string Name { get; }
        public Action? Action { get; }
        public SubMenuItem(string name, Action? action) { Name = name; Action = action; }
    }
}

public static class FileDialog
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OPENFILENAME
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public IntPtr lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public IntPtr lpstrFileTitle;
        public int nMaxFileTitle;
        public string? lpstrInitialDir;
        public string? lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string? lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public IntPtr lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetOpenFileNameW(ref OPENFILENAME lpofn);

    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_NOCHANGEDIR = 0x00000008;
    private const int MAX_PATH = 260;

    public static string? OpenHdiFile()
    {
        var fileBuffer = new char[MAX_PATH];
        var fileHandle = GCHandle.Alloc(fileBuffer, GCHandleType.Pinned);

        try
        {
            var ofn = new OPENFILENAME();
            ofn.lStructSize = Marshal.SizeOf<OPENFILENAME>();
            ofn.lpstrFilter = "HDI Files (*.hdi)\0*.hdi\0All Files (*.*)\0*.*\0\0";
            ofn.lpstrFile = fileHandle.AddrOfPinnedObject();
            ofn.nMaxFile = MAX_PATH;
            ofn.lpstrTitle = "Open HDI Disk Image";
            ofn.Flags = OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST | OFN_NOCHANGEDIR;
            ofn.lpstrDefExt = "hdi";

            if (GetOpenFileNameW(ref ofn))
            {
                int len = Array.IndexOf(fileBuffer, '\0');
                if (len < 0) len = fileBuffer.Length;
                return new string(fileBuffer, 0, len);
            }
            return null;
        }
        finally
        {
            fileHandle.Free();
        }
    }
}
