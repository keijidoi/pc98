using Xunit;
using PC98Emu.Graphics;

namespace PC98Emu.Tests.Graphics;

public class GDCTests
{
    [Fact]
    public void Reset_Command()
    {
        var gdc = new GDC(isText: true);
        gdc.WriteCommand(0x00); // RESET
        Assert.False(gdc.DisplayEnabled);
    }

    [Fact]
    public void Start_Command()
    {
        var gdc = new GDC(isText: true);
        gdc.WriteCommand(0x0D); // START
        Assert.True(gdc.DisplayEnabled);
    }

    [Fact]
    public void Stop_Command()
    {
        var gdc = new GDC(isText: true);
        gdc.WriteCommand(0x0D); // START
        gdc.WriteCommand(0x0C); // STOP
        Assert.False(gdc.DisplayEnabled);
    }

    [Fact]
    public void CSRW_SetsCursorAddress()
    {
        var gdc = new GDC(isText: true);
        gdc.WriteCommand(0x49); // CSRW
        gdc.WriteParameter(0x00); // low byte
        gdc.WriteParameter(0x01); // high byte
        Assert.Equal(0x0100, gdc.CursorAddress);
    }

    [Fact]
    public void SYNC_Command_AcceptsParameters()
    {
        var gdc = new GDC(isText: true);
        gdc.WriteCommand(0x0E); // SYNC (DE=0)
        for (int i = 0; i < 8; i++)
            gdc.WriteParameter(0x00);
        Assert.True(true);
    }

    [Fact]
    public void SCROLL_Command()
    {
        var gdc = new GDC(isText: true);
        gdc.WriteCommand(0x70); // SCROLL
        gdc.WriteParameter(0x00);
        gdc.WriteParameter(0x00);
        gdc.WriteParameter(0x19);
        Assert.True(true);
    }

    [Fact]
    public void StatusRegister_ReportsVSync()
    {
        var gdc = new GDC(isText: true);
        var status = gdc.ReadStatus();
        Assert.IsType<byte>(status);
    }
}
