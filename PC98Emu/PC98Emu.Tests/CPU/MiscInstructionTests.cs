using PC98Emu.Bus;
using PC98Emu.CPU;

namespace PC98Emu.Tests.CPU;

public class MiscInstructionTests
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
    public void MovRm8R8()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x42;
        cpu.BX = 0x0100;
        // MOV [BX], AL -> opcode 0x88, modrm = 0x07 (mod=0, reg=0(AL), rm=7(BX))
        bus.WriteMemoryByte(0, 0x88);
        bus.WriteMemoryByte(1, 0x07); // mod=00, reg=000(AL), rm=111(BX)
        cpu.Step();
        Assert.Equal(0x42, bus.ReadMemoryByte(0x0100));
    }

    [Fact]
    public void MovR8Rm8()
    {
        var (cpu, bus) = CreateCpu();
        cpu.BX = 0x0100;
        bus.WriteMemoryByte(0x0100, 0x99);
        // MOV AL, [BX] -> opcode 0x8A, modrm = 0x07 (mod=0, reg=0(AL), rm=7(BX))
        bus.WriteMemoryByte(0, 0x8A);
        bus.WriteMemoryByte(1, 0x07);
        cpu.Step();
        Assert.Equal(0x99, cpu.AL);
    }

    [Fact]
    public void MovSreg()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AX = 0x1234;
        // MOV ES, AX -> 0x8E, modrm = 0xC0 (mod=3, reg=0(ES), rm=0(AX))
        bus.WriteMemoryByte(0, 0x8E);
        bus.WriteMemoryByte(1, 0xC0);
        cpu.Step();
        Assert.Equal(0x1234, cpu.ES);
    }

    [Fact]
    public void ShlRm8By1()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x40;
        // SHL AL, 1 -> 0xD0, modrm = 0xE0 (mod=3, reg=4(SHL), rm=0(AL))
        bus.WriteMemoryByte(0, 0xD0);
        bus.WriteMemoryByte(1, 0xE0);
        cpu.Step();
        Assert.Equal(0x80, cpu.AL);
        Assert.False(cpu.Flags.CF);
    }

    [Fact]
    public void ShlWithCarry()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x80;
        bus.WriteMemoryByte(0, 0xD0);
        bus.WriteMemoryByte(1, 0xE0); // SHL AL, 1
        cpu.Step();
        Assert.Equal(0x00, cpu.AL);
        Assert.True(cpu.Flags.CF);
    }

    [Fact]
    public void ShrRm8By1()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x02;
        // SHR AL, 1 -> 0xD0, modrm = 0xE8 (mod=3, reg=5(SHR), rm=0(AL))
        bus.WriteMemoryByte(0, 0xD0);
        bus.WriteMemoryByte(1, 0xE8);
        cpu.Step();
        Assert.Equal(0x01, cpu.AL);
        Assert.False(cpu.Flags.CF);
    }

    [Fact]
    public void SarRm8By1()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x80; // -128
        // SAR AL, 1 -> 0xD0, modrm = 0xF8 (mod=3, reg=7(SAR), rm=0(AL))
        bus.WriteMemoryByte(0, 0xD0);
        bus.WriteMemoryByte(1, 0xF8);
        cpu.Step();
        Assert.Equal(0xC0, cpu.AL); // sign bit preserved
    }

    [Fact]
    public void MulRm8()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x10;
        cpu.CL = 0x08;
        // MUL CL -> 0xF6, modrm = 0xE1 (mod=3, reg=4(MUL), rm=1(CL))
        bus.WriteMemoryByte(0, 0xF6);
        bus.WriteMemoryByte(1, 0xE1);
        cpu.Step();
        Assert.Equal(0x0080, cpu.AX); // 16 * 8 = 128
    }

    [Fact]
    public void DivRm8()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AX = 0x0064; // 100
        cpu.CL = 0x0A;   // 10
        // DIV CL -> 0xF6, modrm = 0xF1 (mod=3, reg=6(DIV), rm=1(CL))
        bus.WriteMemoryByte(0, 0xF6);
        bus.WriteMemoryByte(1, 0xF1);
        cpu.Step();
        Assert.Equal(0x0A, cpu.AL); // quotient = 10
        Assert.Equal(0x00, cpu.AH); // remainder = 0
    }

    [Fact]
    public void DivByZeroTriggersInt0()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AX = 0x0064;
        cpu.CL = 0x00;
        // Set up IVT for INT 0
        bus.WriteMemoryWord(0x0000, 0x0500); // IP
        bus.WriteMemoryWord(0x0002, 0x0000); // CS
        // DIV CL
        bus.WriteMemoryByte(0x0010, 0xF6);
        bus.WriteMemoryByte(0x0011, 0xF1);
        cpu.IP = 0x0010;
        cpu.Step();
        Assert.Equal(0x0500, cpu.IP);
        Assert.Equal(0x0000, cpu.CS);
    }

    [Fact]
    public void NegRm8()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x05;
        // NEG AL -> 0xF6, modrm = 0xD8 (mod=3, reg=3(NEG), rm=0(AL))
        bus.WriteMemoryByte(0, 0xF6);
        bus.WriteMemoryByte(1, 0xD8);
        cpu.Step();
        Assert.Equal(0xFB, cpu.AL); // -5
        Assert.True(cpu.Flags.CF);  // CF set when source != 0
    }

    [Fact]
    public void NotRm8()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0xAA;
        // NOT AL -> 0xF6, modrm = 0xD0 (mod=3, reg=2(NOT), rm=0(AL))
        bus.WriteMemoryByte(0, 0xF6);
        bus.WriteMemoryByte(1, 0xD0);
        cpu.Step();
        Assert.Equal(0x55, cpu.AL);
    }

    [Fact]
    public void InAlImm8()
    {
        var (cpu, bus) = CreateCpu();
        // No device registered, should read 0xFF
        bus.WriteMemoryByte(0, 0xE4); // IN AL, imm8
        bus.WriteMemoryByte(1, 0x60); // port 0x60
        cpu.Step();
        Assert.Equal(0xFF, cpu.AL);
    }

    [Fact]
    public void OutAlImm8()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x42;
        bus.WriteMemoryByte(0, 0xE6); // OUT imm8, AL
        bus.WriteMemoryByte(1, 0x60); // port 0x60
        cpu.Step(); // just shouldn't throw
        Assert.Equal(0x0002, cpu.IP);
    }

    [Fact]
    public void InAlDx()
    {
        var (cpu, bus) = CreateCpu();
        cpu.DX = 0x0060;
        bus.WriteMemoryByte(0, 0xEC); // IN AL, DX
        cpu.Step();
        Assert.Equal(0xFF, cpu.AL); // no device
    }

    [Fact]
    public void OutDxAl()
    {
        var (cpu, bus) = CreateCpu();
        cpu.DX = 0x0060;
        cpu.AL = 0x55;
        bus.WriteMemoryByte(0, 0xEE); // OUT DX, AL
        cpu.Step();
        Assert.Equal(0x0001, cpu.IP);
    }

    [Fact]
    public void Lea()
    {
        var (cpu, bus) = CreateCpu();
        cpu.BX = 0x0100;
        cpu.SI = 0x0020;
        // LEA AX, [BX+SI] -> 0x8D, modrm = 0x00 (mod=0, reg=0(AX), rm=0(BX+SI))
        bus.WriteMemoryByte(0, 0x8D);
        bus.WriteMemoryByte(1, 0x00);
        cpu.Step();
        Assert.Equal(0x0120, cpu.AX);
    }

    [Fact]
    public void XchgAxReg16()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AX = 0x1111;
        cpu.CX = 0x2222;
        bus.WriteMemoryByte(0, 0x91); // XCHG AX, CX
        cpu.Step();
        Assert.Equal(0x2222, cpu.AX);
        Assert.Equal(0x1111, cpu.CX);
    }

    [Fact]
    public void CliSti()
    {
        var (cpu, bus) = CreateCpu();
        cpu.Flags.IF = true;
        bus.WriteMemoryByte(0, 0xFA); // CLI
        cpu.Step();
        Assert.False(cpu.Flags.IF);

        bus.WriteMemoryByte(1, 0xFB); // STI
        cpu.Step();
        Assert.True(cpu.Flags.IF);
    }

    [Fact]
    public void CldStd()
    {
        var (cpu, bus) = CreateCpu();
        cpu.Flags.DF = false;
        bus.WriteMemoryByte(0, 0xFD); // STD
        cpu.Step();
        Assert.True(cpu.Flags.DF);

        bus.WriteMemoryByte(1, 0xFC); // CLD
        cpu.Step();
        Assert.False(cpu.Flags.DF);
    }

    [Fact]
    public void Cbw()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x80; // -128
        bus.WriteMemoryByte(0, 0x98); // CBW
        cpu.Step();
        Assert.Equal(0xFF80, cpu.AX);
    }

    [Fact]
    public void CbwPositive()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x7F;
        bus.WriteMemoryByte(0, 0x98); // CBW
        cpu.Step();
        Assert.Equal(0x007F, cpu.AX);
    }

    [Fact]
    public void Cwd()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AX = 0x8000;
        bus.WriteMemoryByte(0, 0x99); // CWD
        cpu.Step();
        Assert.Equal(0xFFFF, cpu.DX);
    }

    [Fact]
    public void PushfPopf()
    {
        var (cpu, bus) = CreateCpu();
        cpu.Flags.CF = true;
        cpu.Flags.ZF = true;
        cpu.Flags.SF = false;
        ushort flags = cpu.Flags.Value;

        bus.WriteMemoryByte(0, 0x9C); // PUSHF
        bus.WriteMemoryByte(1, 0x9D); // POPF

        cpu.Step(); // PUSHF
        cpu.Flags.CF = false;
        cpu.Flags.ZF = false;

        cpu.Step(); // POPF - restores flags
        Assert.True(cpu.Flags.CF);
        Assert.True(cpu.Flags.ZF);
    }

    [Fact]
    public void Group80h_AddRm8Imm8()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x10;
        // ADD AL, 0x05 via Group 1 (0x80)
        // modrm = 0xC0 (mod=3, reg=0(ADD), rm=0(AL))
        bus.WriteMemoryByte(0, 0x80);
        bus.WriteMemoryByte(1, 0xC0);
        bus.WriteMemoryByte(2, 0x05);
        cpu.Step();
        Assert.Equal(0x15, cpu.AL);
    }

    [Fact]
    public void Group81h_SubRm16Imm16()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AX = 0x1000;
        // SUB AX, 0x0100 via Group 1 (0x81)
        // modrm = 0xE8 (mod=3, reg=5(SUB), rm=0(AX))
        bus.WriteMemoryByte(0, 0x81);
        bus.WriteMemoryByte(1, 0xE8);
        bus.WriteMemoryByte(2, 0x00); // low
        bus.WriteMemoryByte(3, 0x01); // high
        cpu.Step();
        Assert.Equal(0x0F00, cpu.AX);
    }

    [Fact]
    public void Group83h_CmpRm16SignExtImm8()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AX = 0x0005;
        // CMP AX, 5 via 0x83
        // modrm = 0xF8 (mod=3, reg=7(CMP), rm=0(AX))
        bus.WriteMemoryByte(0, 0x83);
        bus.WriteMemoryByte(1, 0xF8);
        bus.WriteMemoryByte(2, 0x05);
        cpu.Step();
        Assert.True(cpu.Flags.ZF);
        Assert.Equal(0x0005, cpu.AX); // CMP doesn't modify
    }

    [Fact]
    public void MovRm8R8_RegisterToRegister()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x00;
        cpu.BL = 0x42;
        // MOV AL, BL -> 0x88, modrm = 0xD8 (mod=3, reg=3(BL), rm=0(AL))
        bus.WriteMemoryByte(0, 0x88);
        bus.WriteMemoryByte(1, 0xD8);
        cpu.Step();
        Assert.Equal(0x42, cpu.AL);
    }

    [Fact]
    public void XchgRm8R8()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x11;
        cpu.BL = 0x22;
        // XCHG AL, BL -> 0x86, modrm = 0xD8 (mod=3, reg=3(BL), rm=0(AL))
        bus.WriteMemoryByte(0, 0x86);
        bus.WriteMemoryByte(1, 0xD8);
        cpu.Step();
        Assert.Equal(0x22, cpu.AL);
        Assert.Equal(0x11, cpu.BL);
    }

    [Fact]
    public void ShlByCl()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x01;
        cpu.CL = 4;
        // SHL AL, CL -> 0xD2, modrm = 0xE0 (mod=3, reg=4(SHL), rm=0(AL))
        bus.WriteMemoryByte(0, 0xD2);
        bus.WriteMemoryByte(1, 0xE0);
        cpu.Step();
        Assert.Equal(0x10, cpu.AL);
    }

    [Fact]
    public void MovSreg_ReadBack()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AX = 0x5678;
        // MOV DS, AX -> 0x8E, modrm = 0xD8 (mod=3, reg=3(DS), rm=0(AX))
        bus.WriteMemoryByte(0, 0x8E);
        bus.WriteMemoryByte(1, 0xD8);
        cpu.Step();
        Assert.Equal(0x5678, cpu.DS);

        // MOV AX, DS -> 0x8C, modrm = 0xD8 (mod=3, reg=3(DS), rm=0(AX))
        cpu.AX = 0;
        bus.WriteMemoryByte(2, 0x8C);
        bus.WriteMemoryByte(3, 0xD8);
        cpu.Step();
        Assert.Equal(0x5678, cpu.AX);
    }

    [Fact]
    public void MulRm16()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AX = 0x0100;
        cpu.CX = 0x0100;
        // MUL CX -> 0xF7, modrm = 0xE1 (mod=3, reg=4(MUL), rm=1(CX))
        bus.WriteMemoryByte(0, 0xF7);
        bus.WriteMemoryByte(1, 0xE1);
        cpu.Step();
        // 0x100 * 0x100 = 0x10000
        Assert.Equal(0x0000, cpu.AX);
        Assert.Equal(0x0001, cpu.DX);
    }
}
