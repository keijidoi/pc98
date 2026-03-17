using Xunit;
using PC98Emu.Graphics;

namespace PC98Emu.Tests.Graphics;

public class TextRendererTests
{
    [Fact]
    public void RenderANK_Character()
    {
        var memory = new byte[0x100000];
        var renderer = new TextRenderer(memory);

        memory[0xA0000] = 0x41; // char low 'A'
        memory[0xA0001] = 0x00; // char high
        memory[0xA2000] = 0xE1; // attribute: white, visible
        memory[0xA2001] = 0x00;

        var framebuffer = new uint[640 * 400];
        renderer.Render(framebuffer, 640, 400);

        bool hasPixels = false;
        for (int i = 0; i < 640 * 16; i++)
            if (framebuffer[i] != 0) { hasPixels = true; break; }
        Assert.True(hasPixels);
    }
}
