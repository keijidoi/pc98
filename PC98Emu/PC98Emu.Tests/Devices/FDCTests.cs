using Xunit;
using PC98Emu.Devices;
using PC98Emu.Disk;

namespace PC98Emu.Tests.Devices;

public class FDCTests
{
    [Fact]
    public void ReadData_TransfersViaDMA()
    {
        var memory = new byte[0x100000];
        var dma = new DMA();
        var diskMgr = new DiskManager();
        var fdc = new FDC(dma, diskMgr, () => { }, memory);

        var diskData = CreateBootableDisk();
        var image = new D88Image(diskData);
        diskMgr.MountFloppy(0, image);

        // Setup DMA channel 2: address=0x1000, count=511
        dma.WriteByte(0x05, 0x00); dma.WriteByte(0x05, 0x10);
        dma.WriteByte(0x07, 0xFF); dma.WriteByte(0x07, 0x01);

        // Issue READ DATA command: 0x06, HD|US, C, H, R, N, EOT, GPL, DTL
        fdc.WriteByte(0x92, 0x06);
        fdc.WriteByte(0x92, 0x00);
        fdc.WriteByte(0x92, 0x00);
        fdc.WriteByte(0x92, 0x00);
        fdc.WriteByte(0x92, 0x01);
        fdc.WriteByte(0x92, 0x02);
        fdc.WriteByte(0x92, 0x08);
        fdc.WriteByte(0x92, 0x1B);
        fdc.WriteByte(0x92, 0xFF);

        fdc.Tick(100);

        Assert.Equal(0xEB, memory[0x1000]);
    }

    private byte[] CreateBootableDisk()
    {
        var data = new byte[688 + 16 + 512];
        int diskSize = data.Length;
        data[0x1C] = (byte)diskSize; data[0x1D] = (byte)(diskSize >> 8);
        data[0x1E] = (byte)(diskSize >> 16); data[0x1F] = (byte)(diskSize >> 24);
        int trackOffset = 688;
        data[0x20] = (byte)trackOffset; data[0x21] = (byte)(trackOffset >> 8);
        data[0x22] = (byte)(trackOffset >> 16); data[0x23] = (byte)(trackOffset >> 24);
        data[688] = 0; data[689] = 0; data[690] = 1; data[691] = 2;
        data[692] = 8; data[693] = 0; data[702] = 0x00; data[703] = 0x02;
        data[704] = 0xEB;
        return data;
    }
}
