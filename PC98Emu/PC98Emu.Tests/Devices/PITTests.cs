using Xunit;
using PC98Emu.Devices;

namespace PC98Emu.Tests.Devices;

public class PITTests
{
    [Fact]
    public void SetCounterValue()
    {
        var pit = new PIT(0x71, 0x73, 0x75, 0x77, () => { });
        pit.WriteByte(0x77, 0x34); // ch0, lo/hi, mode 2
        pit.WriteByte(0x71, 0x00);
        pit.WriteByte(0x71, 0x01); // count = 256
        Assert.Equal(256, pit.GetChannelCount(0));
    }

    [Fact]
    public void Channel0_FiresIRQ()
    {
        int irqCount = 0;
        var pit = new PIT(0x71, 0x73, 0x75, 0x77, () => irqCount++);
        pit.WriteByte(0x77, 0x34);
        pit.WriteByte(0x71, 0x0A);
        pit.WriteByte(0x71, 0x00); // count = 10
        pit.Tick(10);
        Assert.Equal(1, irqCount);
    }

    [Fact]
    public void Channel0_Mode2_Repeats()
    {
        int irqCount = 0;
        var pit = new PIT(0x71, 0x73, 0x75, 0x77, () => irqCount++);
        pit.WriteByte(0x77, 0x34);
        pit.WriteByte(0x71, 0x05);
        pit.WriteByte(0x71, 0x00); // count = 5
        pit.Tick(15);
        Assert.Equal(3, irqCount);
    }

    [Fact]
    public void Channel2_BeepFrequency()
    {
        var pit = new PIT(0x71, 0x73, 0x75, 0x77, () => { });
        pit.WriteByte(0x77, 0xB6); // ch2, lo/hi, mode 3
        pit.WriteByte(0x75, 0xE8);
        pit.WriteByte(0x75, 0x03); // count = 1000
        Assert.Equal(1000, pit.GetChannelCount(2));
    }
}
