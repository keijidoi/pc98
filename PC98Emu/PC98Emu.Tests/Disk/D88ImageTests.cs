using Xunit;
using PC98Emu.Disk;

namespace PC98Emu.Tests.Disk;

public class D88ImageTests
{
    [Fact]
    public void ParseD88Header()
    {
        var data = CreateMinimalD88();
        var image = new D88Image(data);
        Assert.Equal(77, image.Cylinders);
        Assert.Equal(2, image.Heads);
        Assert.Equal(8, image.SectorsPerTrack);
        Assert.Equal(512, image.SectorSize);
    }

    [Fact]
    public void ReadFirstSector()
    {
        var data = CreateMinimalD88();
        var image = new D88Image(data);
        var buffer = new byte[512];
        bool ok = image.ReadSector(0, 0, 1, buffer);
        Assert.True(ok);
        Assert.Equal(0xEB, buffer[0]);
    }

    private byte[] CreateMinimalD88()
    {
        var data = new byte[688 + 16 + 512];
        int diskSize = data.Length;
        data[0x1C] = (byte)(diskSize & 0xFF);
        data[0x1D] = (byte)((diskSize >> 8) & 0xFF);
        data[0x1E] = (byte)((diskSize >> 16) & 0xFF);
        data[0x1F] = (byte)((diskSize >> 24) & 0xFF);
        int trackOffset = 688;
        data[0x20] = (byte)(trackOffset & 0xFF);
        data[0x21] = (byte)((trackOffset >> 8) & 0xFF);
        data[0x22] = (byte)((trackOffset >> 16) & 0xFF);
        data[0x23] = (byte)((trackOffset >> 24) & 0xFF);
        int sh = 688;
        data[sh + 0] = 0; // C
        data[sh + 1] = 0; // H
        data[sh + 2] = 1; // R (sector number)
        data[sh + 3] = 2; // N (size code: 2=512)
        data[sh + 4] = 8; data[sh + 5] = 0; // sectors in track
        data[sh + 14] = 0x00; data[sh + 15] = 0x02; // data size = 512
        data[688 + 16] = 0xEB;
        return data;
    }
}
