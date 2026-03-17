using PC98Emu.Bus;
using PC98Emu.CPU;

namespace PC98Emu.Tests.CPU;

public class V30Tests
{
    private static (V30 cpu, SystemBus bus) CreateCpu()
    {
        var bus = new SystemBus();
        var cpu = new V30(bus);
        cpu.CS = 0x0000;
        cpu.IP = 0x0000;
        cpu.SS = 0x0000;
        cpu.SP = 0xFFFE;
        cpu.DS = 0x0000;
        cpu.ES = 0x0000;
        return (cpu, bus);
    }

    [Fact]
    public void RegisterHighLowByteAccess()
    {
        var (cpu, _) = CreateCpu();
        cpu.AX = 0x1234;
        Assert.Equal(0x34, cpu.AL);
        Assert.Equal(0x12, cpu.AH);

        cpu.AL = 0xAB;
        Assert.Equal(0x12AB, cpu.AX);

        cpu.AH = 0xCD;
        Assert.Equal(0xCDAB, cpu.AX);

        cpu.BX = 0x5678;
        Assert.Equal(0x78, cpu.BL);
        Assert.Equal(0x56, cpu.BH);

        cpu.CX = 0x9ABC;
        Assert.Equal(0xBC, cpu.CL);
        Assert.Equal(0x9A, cpu.CH);

        cpu.DX = 0xDEF0;
        Assert.Equal(0xF0, cpu.DL);
        Assert.Equal(0xDE, cpu.DH);
    }

    [Fact]
    public void NopAdvancesIP()
    {
        var (cpu, bus) = CreateCpu();
        bus.WriteMemoryByte(0, 0x90); // NOP
        cpu.Step();
        Assert.Equal(0x0001, cpu.IP);
    }

    [Fact]
    public void MovAlImm8()
    {
        var (cpu, bus) = CreateCpu();
        bus.WriteMemoryByte(0, 0xB0); // MOV AL, imm8
        bus.WriteMemoryByte(1, 0x42);
        cpu.Step();
        Assert.Equal(0x42, cpu.AL);
        Assert.Equal(0x0002, cpu.IP);
    }

    [Fact]
    public void MovAxImm16()
    {
        var (cpu, bus) = CreateCpu();
        bus.WriteMemoryByte(0, 0xB8); // MOV AX, imm16
        bus.WriteMemoryByte(1, 0x34); // low
        bus.WriteMemoryByte(2, 0x12); // high
        cpu.Step();
        Assert.Equal(0x1234, cpu.AX);
        Assert.Equal(0x0003, cpu.IP);
    }

    [Fact]
    public void HltSetsHalted()
    {
        var (cpu, bus) = CreateCpu();
        bus.WriteMemoryByte(0, 0xF4); // HLT
        cpu.Step();
        Assert.True(cpu.Halted);
    }

    [Fact]
    public void BiosInterception()
    {
        var (cpu, bus) = CreateCpu();
        bool called = false;
        cpu.RegisterBiosHandler(0x00000, () =>
        {
            called = true;
            cpu.IP += 1; // advance past the instruction
        });
        bus.WriteMemoryByte(0, 0x90); // NOP (won't actually execute)
        cpu.Step();
        Assert.True(called);
    }

    [Fact]
    public void GetPhysicalAddressCalculation()
    {
        Assert.Equal(0x12340, V30.GetPhysicalAddress(0x1234, 0x0000));
        Assert.Equal(0x12345, V30.GetPhysicalAddress(0x1234, 0x0005));
        Assert.Equal(0xFFFFF, V30.GetPhysicalAddress(0xFFFF, 0x000F));
    }

    [Fact]
    public void GetSetRegister8()
    {
        var (cpu, _) = CreateCpu();
        for (int i = 0; i < 8; i++)
        {
            cpu.SetRegister8(i, (byte)(0x10 + i));
            Assert.Equal((byte)(0x10 + i), cpu.GetRegister8(i));
        }
    }

    [Fact]
    public void GetSetRegister16()
    {
        var (cpu, _) = CreateCpu();
        for (int i = 0; i < 8; i++)
        {
            cpu.SetRegister16(i, (ushort)(0x1000 + i));
            Assert.Equal((ushort)(0x1000 + i), cpu.GetRegister16(i));
        }
    }

    [Fact]
    public void SegmentRegisterAccess()
    {
        var (cpu, _) = CreateCpu();
        cpu.SetSegmentRegister(0, 0x1000); // ES
        cpu.SetSegmentRegister(1, 0x2000); // CS
        cpu.SetSegmentRegister(2, 0x3000); // SS
        cpu.SetSegmentRegister(3, 0x4000); // DS
        Assert.Equal(0x1000, cpu.ES);
        Assert.Equal(0x2000, cpu.CS);
        Assert.Equal(0x3000, cpu.SS);
        Assert.Equal(0x4000, cpu.DS);
    }
}
