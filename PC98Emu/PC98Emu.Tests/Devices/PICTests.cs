using Xunit;
using PC98Emu.Devices;

namespace PC98Emu.Tests.Devices;

public class PICTests
{
    [Fact]
    public void InitICW_SetsBaseVector()
    {
        var pic = new PIC(0x00, 0x02);
        pic.WriteByte(0x00, 0x11);
        pic.WriteByte(0x02, 0x08);
        pic.WriteByte(0x02, 0x04);
        pic.WriteByte(0x02, 0x01);
        Assert.Equal(0x08, pic.VectorBase);
    }

    [Fact]
    public void MaskIRQ()
    {
        var pic = new PIC(0x00, 0x02);
        pic.WriteByte(0x02, 0xFF);
        Assert.Equal(0xFF, pic.ReadByte(0x02));
    }

    [Fact]
    public void RaiseIRQ_Unmasked()
    {
        var pic = new PIC(0x00, 0x02);
        pic.VectorBase = 0x08;
        pic.WriteByte(0x02, 0x00);
        pic.RaiseIRQ(0);
        Assert.True(pic.HasInterrupt());
        Assert.Equal(0x08, pic.AcknowledgeInterrupt());
    }

    [Fact]
    public void RaiseIRQ_Masked()
    {
        var pic = new PIC(0x00, 0x02);
        pic.VectorBase = 0x08;
        pic.WriteByte(0x02, 0x01);
        pic.RaiseIRQ(0);
        Assert.False(pic.HasInterrupt());
    }

    [Fact]
    public void EOI_ClearsISR()
    {
        var pic = new PIC(0x00, 0x02);
        pic.VectorBase = 0x08;
        pic.WriteByte(0x02, 0x00);
        pic.RaiseIRQ(1);
        pic.AcknowledgeInterrupt();
        pic.WriteByte(0x00, 0x20);
        Assert.False(pic.HasInterrupt());
    }

    [Fact]
    public void IRQ_Priority()
    {
        var pic = new PIC(0x00, 0x02);
        pic.VectorBase = 0x08;
        pic.WriteByte(0x02, 0x00);
        pic.RaiseIRQ(3);
        pic.RaiseIRQ(1);
        Assert.Equal(0x09, pic.AcknowledgeInterrupt());
    }
}
