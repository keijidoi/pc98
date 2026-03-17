using PC98Emu.Bus;
using PC98Emu.CPU;

namespace PC98Emu.Tests.CPU;

public class ArithmeticTests
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
    public void AddAlImm8_Normal()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x10;
        bus.WriteMemoryByte(0, 0x04); // ADD AL, imm8
        bus.WriteMemoryByte(1, 0x20);
        cpu.Step();
        Assert.Equal(0x30, cpu.AL);
        Assert.False(cpu.Flags.CF);
        Assert.False(cpu.Flags.ZF);
    }

    [Fact]
    public void AddAlImm8_Carry()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0xFF;
        bus.WriteMemoryByte(0, 0x04); // ADD AL, imm8
        bus.WriteMemoryByte(1, 0x01);
        cpu.Step();
        Assert.Equal(0x00, cpu.AL);
        Assert.True(cpu.Flags.CF);
        Assert.True(cpu.Flags.ZF);
    }

    [Fact]
    public void AddAxImm16()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AX = 0x1000;
        bus.WriteMemoryByte(0, 0x05); // ADD AX, imm16
        bus.WriteMemoryByte(1, 0x34); // low
        bus.WriteMemoryByte(2, 0x12); // high
        cpu.Step();
        Assert.Equal(0x2234, cpu.AX);
        Assert.False(cpu.Flags.CF);
    }

    [Fact]
    public void SubAlImm8_Normal()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x30;
        bus.WriteMemoryByte(0, 0x2C); // SUB AL, imm8
        bus.WriteMemoryByte(1, 0x10);
        cpu.Step();
        Assert.Equal(0x20, cpu.AL);
        Assert.False(cpu.Flags.CF);
    }

    [Fact]
    public void SubAlImm8_Borrow()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x10;
        bus.WriteMemoryByte(0, 0x2C); // SUB AL, imm8
        bus.WriteMemoryByte(1, 0x20);
        cpu.Step();
        Assert.Equal(0xF0, cpu.AL);
        Assert.True(cpu.Flags.CF);
    }

    [Fact]
    public void CmpSetsFlags()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x42;
        bus.WriteMemoryByte(0, 0x3C); // CMP AL, imm8
        bus.WriteMemoryByte(1, 0x42);
        cpu.Step();
        Assert.Equal(0x42, cpu.AL); // AL unchanged
        Assert.True(cpu.Flags.ZF);
        Assert.False(cpu.Flags.CF);
    }

    [Fact]
    public void AndAlImm8()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0xFF;
        bus.WriteMemoryByte(0, 0x24); // AND AL, imm8
        bus.WriteMemoryByte(1, 0x0F);
        cpu.Step();
        Assert.Equal(0x0F, cpu.AL);
        Assert.False(cpu.Flags.CF);
        Assert.False(cpu.Flags.OF);
    }

    [Fact]
    public void OrAlImm8()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0xF0;
        bus.WriteMemoryByte(0, 0x0C); // OR AL, imm8
        bus.WriteMemoryByte(1, 0x0F);
        cpu.Step();
        Assert.Equal(0xFF, cpu.AL);
        Assert.False(cpu.Flags.ZF);
    }

    [Fact]
    public void XorAlImm8()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0xFF;
        bus.WriteMemoryByte(0, 0x34); // XOR AL, imm8
        bus.WriteMemoryByte(1, 0xFF);
        cpu.Step();
        Assert.Equal(0x00, cpu.AL);
        Assert.True(cpu.Flags.ZF);
    }

    [Fact]
    public void IncReg16()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AX = 0x00FF;
        bus.WriteMemoryByte(0, 0x40); // INC AX
        cpu.Step();
        Assert.Equal(0x0100, cpu.AX);
        Assert.False(cpu.Flags.ZF);
    }

    [Fact]
    public void DecReg16()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AX = 0x0001;
        bus.WriteMemoryByte(0, 0x48); // DEC AX
        cpu.Step();
        Assert.Equal(0x0000, cpu.AX);
        Assert.True(cpu.Flags.ZF);
    }

    [Fact]
    public void IncDoesNotAffectCF()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AX = 0xFFFF;
        cpu.Flags.CF = true;
        bus.WriteMemoryByte(0, 0x40); // INC AX
        cpu.Step();
        Assert.Equal(0x0000, cpu.AX);
        Assert.True(cpu.Flags.CF); // CF unchanged
        Assert.True(cpu.Flags.ZF);
    }

    [Fact]
    public void PushPop()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AX = 0x1234;
        // PUSH AX
        bus.WriteMemoryByte(0, 0x50);
        // POP BX
        bus.WriteMemoryByte(1, 0x5B);
        cpu.Step();
        cpu.Step();
        Assert.Equal(0x1234, cpu.BX);
        Assert.Equal(0xFFFE, cpu.SP);
    }

    [Fact]
    public void AdcWithCarry()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x10;
        cpu.Flags.CF = true;
        bus.WriteMemoryByte(0, 0x14); // ADC AL, imm8
        bus.WriteMemoryByte(1, 0x20);
        cpu.Step();
        Assert.Equal(0x31, cpu.AL); // 0x10 + 0x20 + 1
    }

    [Fact]
    public void SbbWithBorrow()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x30;
        cpu.Flags.CF = true;
        bus.WriteMemoryByte(0, 0x1C); // SBB AL, imm8
        bus.WriteMemoryByte(1, 0x10);
        cpu.Step();
        Assert.Equal(0x1F, cpu.AL); // 0x30 - 0x10 - 1
    }
}
