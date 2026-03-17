using Xunit;
using PC98Emu.Devices;

namespace PC98Emu.Tests.Devices;

public class KeyboardTests
{
    [Fact]
    public void EnqueueScancode_ReadFromPort()
    {
        bool irqFired = false;
        var kbd = new Keyboard(() => irqFired = true);
        kbd.EnqueueScancode(0x1E);
        Assert.True(irqFired);
        Assert.Equal(0x1E, kbd.ReadByte(0x41));
    }

    [Fact]
    public void StatusPort_DataReady()
    {
        var kbd = new Keyboard(() => { });
        Assert.Equal(0x00, kbd.ReadByte(0x43) & 0x01);
        kbd.EnqueueScancode(0x1E);
        Assert.Equal(0x01, kbd.ReadByte(0x43) & 0x01);
    }
}
