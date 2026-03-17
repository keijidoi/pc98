using Xunit;
using PC98Emu.CPU;

namespace PC98Emu.Tests.CPU;

public class FlagsTests
{
    [Fact]
    public void SetAndGetCarryFlag()
    {
        var flags = new CpuFlags();
        flags.CF = true;
        Assert.True(flags.CF);
        Assert.Equal((ushort)0x0001, (ushort)(flags.Value & 0x0001));
    }

    [Fact]
    public void SetAndGetZeroFlag()
    {
        var flags = new CpuFlags();
        flags.ZF = true;
        Assert.True(flags.ZF);
    }

    [Fact]
    public void SetAndGetSignFlag()
    {
        var flags = new CpuFlags();
        flags.SF = true;
        Assert.True(flags.SF);
    }

    [Fact]
    public void SetAndGetOverflowFlag()
    {
        var flags = new CpuFlags();
        flags.OF = true;
        Assert.True(flags.OF);
    }

    [Fact]
    public void UpdateFlagsForByteResult()
    {
        var flags = new CpuFlags();
        flags.UpdateSZP8(0x00);
        Assert.True(flags.ZF);
        Assert.False(flags.SF);
        Assert.True(flags.PF);

        flags.UpdateSZP8(0x80);
        Assert.False(flags.ZF);
        Assert.True(flags.SF);
    }

    [Fact]
    public void UpdateFlagsForWordResult()
    {
        var flags = new CpuFlags();
        flags.UpdateSZP16(0x0000);
        Assert.True(flags.ZF);
        Assert.False(flags.SF);

        flags.UpdateSZP16(0x8000);
        Assert.False(flags.ZF);
        Assert.True(flags.SF);
    }

    [Fact]
    public void ParityFlagCalculation()
    {
        var flags = new CpuFlags();
        flags.UpdateSZP8(0x03);
        Assert.True(flags.PF);

        flags.UpdateSZP8(0x07);
        Assert.False(flags.PF);
    }
}
