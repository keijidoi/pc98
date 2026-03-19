using static SDL2.SDL;

namespace PC98Emu.Graphics;

/// <summary>
/// SDL2-based display for the PC-98 emulator.
/// Composites text and graphics layers and presents to screen.
/// Resolution: 640x400, default scale: 2x.
/// Frame rate: 56.4 Hz (17.73ms per frame).
/// </summary>
public class Display : IDisposable
{
    private readonly TextRenderer _textRenderer;
    private readonly GraphicsRenderer _graphicsRenderer;
    private readonly int _scale;

    private const int ScreenWidth = 640;
    private const int ScreenHeight = 400;

    private IntPtr _window;
    private IntPtr _renderer;
    private IntPtr _texture;

    private readonly uint[] _textBuffer = new uint[ScreenWidth * ScreenHeight];
    private readonly uint[] _graphicsBuffer = new uint[ScreenWidth * ScreenHeight];
    private readonly uint[] _compositeBuffer = new uint[ScreenWidth * ScreenHeight];

    private bool _disposed;

    public Display(TextRenderer textRenderer, GraphicsRenderer graphicsRenderer, int scale = 2)
    {
        _textRenderer = textRenderer;
        _graphicsRenderer = graphicsRenderer;
        _scale = scale;
    }

    /// <summary>
    /// Initialize SDL2 window, renderer, and texture.
    /// </summary>
    public void Init()
    {
        if (SDL_Init(SDL_INIT_VIDEO) < 0)
            throw new InvalidOperationException($"SDL_Init failed: {SDL_GetError()}");

        _window = SDL_CreateWindow(
            "PC-98 Emulator",
            SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED,
            ScreenWidth * _scale, ScreenHeight * _scale,
            SDL_WindowFlags.SDL_WINDOW_SHOWN);

        if (_window == IntPtr.Zero)
            throw new InvalidOperationException($"SDL_CreateWindow failed: {SDL_GetError()}");

        _renderer = SDL_CreateRenderer(_window, -1,
            SDL_RendererFlags.SDL_RENDERER_ACCELERATED | SDL_RendererFlags.SDL_RENDERER_PRESENTVSYNC);

        if (_renderer == IntPtr.Zero)
            throw new InvalidOperationException($"SDL_CreateRenderer failed: {SDL_GetError()}");

        _texture = SDL_CreateTexture(
            _renderer,
            SDL_PIXELFORMAT_ARGB8888,
            (int)SDL_TextureAccess.SDL_TEXTUREACCESS_STREAMING,
            ScreenWidth, ScreenHeight);

        if (_texture == IntPtr.Zero)
            throw new InvalidOperationException($"SDL_CreateTexture failed: {SDL_GetError()}");
    }

    /// <summary>
    /// Render one frame: text layer on top of graphics layer.
    /// </summary>
    public void RenderFrame()
    {
        // Clear buffers
        Array.Clear(_textBuffer);
        Array.Clear(_graphicsBuffer);

        // Render both layers
        _graphicsRenderer.Render(_graphicsBuffer, ScreenWidth, ScreenHeight);
        _textRenderer.Render(_textBuffer, ScreenWidth, ScreenHeight);

        // Composite: text layer takes priority where non-transparent
        for (int i = 0; i < _compositeBuffer.Length; i++)
        {
            uint textPixel = _textBuffer[i];
            if (textPixel != 0x00000000)
                _compositeBuffer[i] = textPixel;
            else
                _compositeBuffer[i] = _graphicsBuffer[i];
        }

        // Update texture and present
        var handle = System.Runtime.InteropServices.GCHandle.Alloc(_compositeBuffer, System.Runtime.InteropServices.GCHandleType.Pinned);
        try
        {
            SDL_UpdateTexture(_texture, IntPtr.Zero, handle.AddrOfPinnedObject(), ScreenWidth * 4);
        }
        finally
        {
            handle.Free();
        }

        SDL_RenderClear(_renderer);
        SDL_RenderCopy(_renderer, _texture, IntPtr.Zero, IntPtr.Zero);
        SDL_RenderPresent(_renderer);
    }

    /// <summary>
    /// Poll SDL events. Returns true if the application should continue running.
    /// </summary>
    // Queue of key events (ASCII, scancode) from SDL
    private readonly Queue<(byte ascii, byte scancode)> _keyQueue = new();

    public bool PollEvents()
    {
        while (SDL_PollEvent(out SDL_Event e) != 0)
        {
            if (e.type == SDL_EventType.SDL_QUIT)
                return false;
            if (e.type == SDL_EventType.SDL_KEYDOWN)
            {
                byte ascii = SdlKeyToAscii(e.key.keysym);
                byte scancode = (byte)e.key.keysym.scancode;
                if (ascii != 0)
                    _keyQueue.Enqueue((ascii, scancode));
            }
        }
        return true;
    }

    public bool HasKey() => _keyQueue.Count > 0;
    public (byte ascii, byte scancode) DequeueKey() =>
        _keyQueue.Count > 0 ? _keyQueue.Dequeue() : ((byte)0, (byte)0);

    private static byte SdlKeyToAscii(SDL_Keysym keysym)
    {
        var key = keysym.sym;
        var mod = keysym.mod;
        bool shift = (mod & SDL_Keymod.KMOD_SHIFT) != 0;

        if (key == SDL_Keycode.SDLK_RETURN) return 0x0D;
        if (key == SDL_Keycode.SDLK_BACKSPACE) return 0x08;
        if (key == SDL_Keycode.SDLK_ESCAPE) return 0x1B;
        if (key == SDL_Keycode.SDLK_TAB) return 0x09;
        if (key == SDL_Keycode.SDLK_SPACE) return 0x20;

        // Letters
        if (key >= SDL_Keycode.SDLK_a && key <= SDL_Keycode.SDLK_z)
        {
            byte ch = (byte)(key - SDL_Keycode.SDLK_a + 'a');
            if (shift) ch = (byte)(ch - 32); // uppercase
            return ch;
        }
        // Digits
        if (key >= SDL_Keycode.SDLK_0 && key <= SDL_Keycode.SDLK_9)
        {
            if (shift)
            {
                // Shift+digit symbols
                byte[] shiftDigits = { (byte)')', (byte)'!', (byte)'@', (byte)'#', (byte)'$',
                                       (byte)'%', (byte)'^', (byte)'&', (byte)'*', (byte)'(' };
                return shiftDigits[key - SDL_Keycode.SDLK_0];
            }
            return (byte)(key - SDL_Keycode.SDLK_0 + '0');
        }
        // Common punctuation
        if (key == SDL_Keycode.SDLK_PERIOD) return shift ? (byte)'>' : (byte)'.';
        if (key == SDL_Keycode.SDLK_COMMA) return shift ? (byte)'<' : (byte)',';
        if (key == SDL_Keycode.SDLK_SLASH) return shift ? (byte)'?' : (byte)'/';
        if (key == SDL_Keycode.SDLK_SEMICOLON) return shift ? (byte)':' : (byte)';';
        if (key == SDL_Keycode.SDLK_MINUS) return shift ? (byte)'_' : (byte)'-';
        if (key == SDL_Keycode.SDLK_EQUALS) return shift ? (byte)'+' : (byte)'=';
        if (key == SDL_Keycode.SDLK_BACKSLASH) return shift ? (byte)'|' : (byte)'\\';

        return 0; // Unknown key
    }

    /// <summary>
    /// Clean up SDL resources.
    /// </summary>
    public void Shutdown()
    {
        if (_texture != IntPtr.Zero)
        {
            SDL_DestroyTexture(_texture);
            _texture = IntPtr.Zero;
        }
        if (_renderer != IntPtr.Zero)
        {
            SDL_DestroyRenderer(_renderer);
            _renderer = IntPtr.Zero;
        }
        if (_window != IntPtr.Zero)
        {
            SDL_DestroyWindow(_window);
            _window = IntPtr.Zero;
        }
        SDL_Quit();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Shutdown();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
