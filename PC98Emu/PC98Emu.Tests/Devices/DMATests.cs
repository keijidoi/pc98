using Xunit;
using PC98Emu.Devices;

namespace PC98Emu.Tests.Devices;

public class DMATests
{
    [Fact]
    public void SetAddressAndCount()
    {
        var dma = new DMA();
        dma.WriteByte(0x05, 0x00); // ch2 addr low
        dma.WriteByte(0x05, 0x10); // ch2 addr high
        dma.WriteByte(0x07, 0xFF); // ch2+1(ch3 addr) - actually this is wrong
        // Let me fix: count ports are 0x09, 0x0B, 0x0D, 0x0F
        // For channel 2: addr=0x05, count=0x0D
        dma.ResetFlipFlop();
        dma.WriteByte(0x0D, 0xFF); // ch2 count low
        dma.WriteByte(0x0D, 0x01); // ch2 count high
        Assert.Equal(0x1000, dma.GetChannelAddress(2));
        Assert.Equal(0x01FF, dma.GetChannelCount(2));
    }

    [Fact]
    public void TransferData()
    {
        var dma = new DMA();
        var memory = new byte[0x100000];
        dma.WriteByte(0x05, 0x00);
        dma.WriteByte(0x05, 0x10);
        dma.ResetFlipFlop();
        dma.WriteByte(0x0D, 0x03);
        dma.WriteByte(0x0D, 0x00);
        var data = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        dma.TransferToMemory(2, data, memory);
        Assert.Equal(0xAA, memory[0x1000]);
        Assert.Equal(0xDD, memory[0x1003]);
    }
}
