using Xunit;
using PC98Emu.CPU;

namespace PC98Emu.Tests.CPU;

public class ModRMTests
{
    [Fact]
    public void DecodeRegField()
    {
        var modrm = new ModRMDecoder();
        modrm.Decode(0xD1);
        Assert.Equal(2, modrm.Reg);
        Assert.Equal(1, modrm.RM);
        Assert.Equal(3, modrm.Mod);
    }

    [Fact]
    public void Mod00_RM110_IsDirectAddress()
    {
        var modrm = new ModRMDecoder();
        modrm.Decode(0x06);
        Assert.Equal(0, modrm.Mod);
        Assert.Equal(6, modrm.RM);
    }

    [Fact]
    public void Mod01_Has8BitDisplacement()
    {
        var modrm = new ModRMDecoder();
        modrm.Decode(0x5C);
        Assert.Equal(1, modrm.Mod);
        Assert.Equal(3, modrm.Reg);
        Assert.Equal(4, modrm.RM);
    }
}
