using Xunit;
using PC98Emu;

namespace PC98Emu.Tests.Integration;

public class BootTests
{
    [Fact]
    public void BootFromD88_ExecutesIPL()
    {
        var emu = new Emulator();
        var diskData = CreateTestBootDisk();
        emu.LoadFloppyDisk(0, diskData);
        emu.Initialize();
        emu.Boot();

        // Run for 1000 instructions
        for (int i = 0; i < 1000; i++)
        {
            if (emu.CPU.Halted) break;
            emu.StepCpu();
        }

        // The test boot sector sets AX=0x1234 then HLTs
        Assert.Equal(0x1234, emu.CPU.AX);
        Assert.True(emu.CPU.Halted);
    }

    private byte[] CreateTestBootDisk()
    {
        // D88 image with boot sector: MOV AX, 0x1234; HLT
        var data = new byte[688 + 16 + 512];
        int diskSize = data.Length;
        data[0x1C] = (byte)diskSize; data[0x1D] = (byte)(diskSize >> 8);
        data[0x1E] = (byte)(diskSize >> 16); data[0x1F] = (byte)(diskSize >> 24);
        int trackOffset = 688;
        data[0x20] = (byte)trackOffset; data[0x21] = (byte)(trackOffset >> 8);
        data[0x22] = (byte)(trackOffset >> 16); data[0x23] = (byte)(trackOffset >> 24);
        // Sector header: C=0, H=0, R=1, N=2, sectors=8
        data[688] = 0; data[689] = 0; data[690] = 1; data[691] = 2;
        data[692] = 8; data[693] = 0; data[702] = 0x00; data[703] = 0x02;
        // Boot code: MOV AX, 0x1234; HLT
        data[704] = 0xB8; data[705] = 0x34; data[706] = 0x12; data[707] = 0xF4;
        return data;
    }
}
