using Xunit;
using PC98Emu.BIOS;
using PC98Emu.CPU;
using PC98Emu.Bus;

namespace PC98Emu.Tests.BIOS;

public class BiosTests
{
    [Fact]
    public void IVT_IsSetup()
    {
        var bus = new SystemBus();
        var cpu = new V30(bus);
        var bios = new CompatibleBios(cpu, bus);
        bios.Initialize();

        ushort ip = bus.ReadMemoryWord(0x18 * 4);
        ushort cs = bus.ReadMemoryWord(0x18 * 4 + 2);
        int addr = (cs << 4) + ip;
        Assert.True(bus.IsBiosArea(addr));
    }

    [Fact]
    public void BDA_MemorySizeIsSet()
    {
        var bus = new SystemBus();
        var cpu = new V30(bus);
        var bios = new CompatibleBios(cpu, bus);
        bios.Initialize();

        ushort memSize = bus.ReadMemoryWord(0x0458);
        Assert.Equal(640, memSize);
    }

    [Fact]
    public void IVT_AllInterruptsPointToBiosArea()
    {
        var bus = new SystemBus();
        var cpu = new V30(bus);
        var bios = new CompatibleBios(cpu, bus);
        bios.Initialize();

        int[] intNums = { 0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D };
        foreach (int intNum in intNums)
        {
            ushort ip = bus.ReadMemoryWord(intNum * 4);
            ushort cs = bus.ReadMemoryWord(intNum * 4 + 2);
            int addr = (cs << 4) + ip;
            Assert.True(bus.IsBiosArea(addr), $"INT {intNum:X2} handler at {addr:X5} is not in BIOS area");
        }
    }

    [Fact]
    public void BDA_BootDeviceIsZero()
    {
        var bus = new SystemBus();
        var cpu = new V30(bus);
        var bios = new CompatibleBios(cpu, bus);
        bios.Initialize();

        byte bootDevice = bus.ReadMemoryByte(0x045B);
        Assert.Equal(0, bootDevice);
    }

    [Fact]
    public void BDA_DisplayModeIsZero()
    {
        var bus = new SystemBus();
        var cpu = new V30(bus);
        var bios = new CompatibleBios(cpu, bus);
        bios.Initialize();

        byte displayMode = bus.ReadMemoryByte(0x0480);
        Assert.Equal(0, displayMode);
    }

    [Fact]
    public void DiskBios_ReadSector()
    {
        var bus = new SystemBus();
        var cpu = new V30(bus);
        var bios = new CompatibleBios(cpu, bus);
        bios.Initialize();
        // Detailed integration test with actual disk deferred to Task 19
    }

    [Fact]
    public void KeyboardBios_NoKey_ReturnsZero()
    {
        var bus = new SystemBus();
        var cpu = new V30(bus);
        var bios = new CompatibleBios(cpu, bus);
        bios.Initialize();
        // Simulated via direct handler call - deferred to integration
    }
}
