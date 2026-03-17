using Xunit;
using PC98Emu.Sound;

namespace PC98Emu.Tests.Sound;

public class YM2608Tests
{
    [Fact]
    public void RegisterWriteAndRead()
    {
        var ym = new YM2608(() => { });
        ym.WriteByte(0x188, 0x07); // SSG mixer register
        ym.WriteByte(0x18A, 0x38); // tone enabled for ch A/B/C
        ym.WriteByte(0x188, 0x07);
        Assert.Equal(0x38, ym.ReadByte(0x18A));
    }

    [Fact]
    public void TimerA_Fires()
    {
        int timerCount = 0;
        var ym = new YM2608(() => timerCount++);
        ym.WriteByte(0x188, 0x24);
        ym.WriteByte(0x18A, 0x00); // Timer A high = 0
        ym.WriteByte(0x188, 0x25);
        ym.WriteByte(0x18A, 0x00); // Timer A low = 0
        ym.WriteByte(0x188, 0x27);
        ym.WriteByte(0x18A, 0x15); // load+enable Timer A
        ym.Tick(1024 * 72 + 1);
        Assert.True(timerCount > 0);
    }

    [Fact]
    public void SSG_ToneGeneration()
    {
        var ym = new YM2608(() => { });
        ym.WriteByte(0x188, 0x00);
        ym.WriteByte(0x18A, 0x00); // freq low
        ym.WriteByte(0x188, 0x01);
        ym.WriteByte(0x18A, 0x01); // freq high = period 256
        ym.WriteByte(0x188, 0x08);
        ym.WriteByte(0x18A, 0x0F); // max volume
        // Enable tone for channel A (bit 0 of reg 7 = 0 means enabled)
        ym.WriteByte(0x188, 0x07);
        ym.WriteByte(0x18A, 0x38); // tone A enabled, others disabled

        var buffer = new short[100];
        ym.GenerateSamples(buffer, 0, 50);
        bool hasAudio = false;
        for (int i = 0; i < 100; i++)
            if (buffer[i] != 0) { hasAudio = true; break; }
        Assert.True(hasAudio);
    }

    [Fact]
    public void FM_SetChannel()
    {
        var ym = new YM2608(() => { });
        ym.WriteByte(0x188, 0x40); // TL for op1 ch1
        ym.WriteByte(0x18A, 0x20);
        ym.WriteByte(0x188, 0xA4); // freq high + block
        ym.WriteByte(0x18A, 0x22);
        ym.WriteByte(0x188, 0xA0); // freq low
        ym.WriteByte(0x18A, 0x69);
        ym.WriteByte(0x188, 0x28);
        ym.WriteByte(0x18A, 0xF0); // all ops on, ch 0
        Assert.True(true); // Should not throw
    }
}
