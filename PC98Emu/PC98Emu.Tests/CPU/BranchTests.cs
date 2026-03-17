using PC98Emu.Bus;
using PC98Emu.CPU;

namespace PC98Emu.Tests.CPU;

public class BranchTests
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
    public void JmpShort()
    {
        var (cpu, bus) = CreateCpu();
        bus.WriteMemoryByte(0, 0xEB); // JMP short
        bus.WriteMemoryByte(1, 0x05); // +5
        cpu.Step();
        Assert.Equal(0x0007, cpu.IP); // 2 (instruction length) + 5
    }

    [Fact]
    public void JzTaken()
    {
        var (cpu, bus) = CreateCpu();
        cpu.Flags.ZF = true;
        bus.WriteMemoryByte(0, 0x74); // JZ
        bus.WriteMemoryByte(1, 0x10); // +16
        cpu.Step();
        Assert.Equal(0x0012, cpu.IP); // 2 + 16
    }

    [Fact]
    public void JzNotTaken()
    {
        var (cpu, bus) = CreateCpu();
        cpu.Flags.ZF = false;
        bus.WriteMemoryByte(0, 0x74); // JZ
        bus.WriteMemoryByte(1, 0x10); // +16
        cpu.Step();
        Assert.Equal(0x0002, cpu.IP); // just past the instruction
    }

    [Fact]
    public void JnzTaken()
    {
        var (cpu, bus) = CreateCpu();
        cpu.Flags.ZF = false;
        bus.WriteMemoryByte(0, 0x75); // JNZ
        bus.WriteMemoryByte(1, 0x08);
        cpu.Step();
        Assert.Equal(0x000A, cpu.IP);
    }

    [Fact]
    public void JcTaken()
    {
        var (cpu, bus) = CreateCpu();
        cpu.Flags.CF = true;
        bus.WriteMemoryByte(0, 0x72); // JC/JB
        bus.WriteMemoryByte(1, 0x04);
        cpu.Step();
        Assert.Equal(0x0006, cpu.IP);
    }

    [Fact]
    public void CallNearRet()
    {
        var (cpu, bus) = CreateCpu();
        // At 0x0000: CALL +0x0003 (target = 0x0006)
        bus.WriteMemoryByte(0, 0xE8);
        bus.WriteMemoryByte(1, 0x03);
        bus.WriteMemoryByte(2, 0x00);
        // At 0x0005: padding
        // At 0x0005: RET
        bus.WriteMemoryByte(5, 0xC3);

        cpu.Step(); // CALL
        Assert.Equal(0x0006, cpu.IP); // should jump to 0x0003 + 0x0003 = 0x0006
        // Wait - CALL displacement is relative to IP after instruction
        // IP after fetch = 0x0003, disp = 0x0003, target = 0x0006
        // But we want RET at the target; let me fix the test
    }

    [Fact]
    public void CallNearAndRet()
    {
        var (cpu, bus) = CreateCpu();
        // CALL near with displacement +2 -> target = IP(3) + 2 = 5
        bus.WriteMemoryByte(0, 0xE8); // CALL near
        bus.WriteMemoryByte(1, 0x02); // disp low
        bus.WriteMemoryByte(2, 0x00); // disp high
        // At 0x0003: HLT (return point)
        bus.WriteMemoryByte(3, 0xF4);
        // At 0x0005: RET
        bus.WriteMemoryByte(5, 0xC3);

        cpu.Step(); // CALL -> pushes 0x0003, jumps to 0x0005
        Assert.Equal(0x0005, cpu.IP);

        cpu.Step(); // RET -> pops 0x0003
        Assert.Equal(0x0003, cpu.IP);
    }

    [Fact]
    public void IntAndIret()
    {
        var (cpu, bus) = CreateCpu();
        // Set up IVT for INT 0x21 at vector 0x21*4 = 0x84
        bus.WriteMemoryWord(0x84, 0x0100); // IP
        bus.WriteMemoryWord(0x86, 0x0200); // CS

        // Place IRET at 0x0200:0x0100 = 0x02100
        bus.WriteMemoryByte(0x02100, 0xCF); // IRET

        cpu.CS = 0x0000;
        cpu.IP = 0x0000;
        cpu.Flags.IF = true;
        cpu.Flags.Value |= 0x0002; // reserved bit

        // INT 21h
        bus.WriteMemoryByte(0, 0xCD);
        bus.WriteMemoryByte(1, 0x21);

        ushort oldFlags = cpu.Flags.Value;
        cpu.Step(); // INT 0x21

        Assert.Equal(0x0100, cpu.IP);
        Assert.Equal(0x0200, cpu.CS);
        Assert.False(cpu.Flags.IF);

        cpu.Step(); // IRET
        Assert.Equal(0x0002, cpu.IP);
        Assert.Equal(0x0000, cpu.CS);
    }

    [Fact]
    public void Loop()
    {
        var (cpu, bus) = CreateCpu();
        // MOV CX, 3 then LOOP back
        cpu.CX = 3;
        cpu.IP = 0x0010;
        // At 0x10: LOOP -2 (back to itself)
        bus.WriteMemoryByte(0x10, 0xE2); // LOOP
        bus.WriteMemoryByte(0x11, 0xFE); // -2

        cpu.Step(); // CX=2, loop taken -> IP = 0x12 + (-2) = 0x10
        Assert.Equal(0x0010, cpu.IP);
        Assert.Equal(2, cpu.CX);

        cpu.Step(); // CX=1, loop taken
        Assert.Equal(0x0010, cpu.IP);
        Assert.Equal(1, cpu.CX);

        cpu.Step(); // CX=0, loop not taken
        Assert.Equal(0x0012, cpu.IP);
        Assert.Equal(0, cpu.CX);
    }

    [Fact]
    public void Stosb()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0x42;
        cpu.ES = 0x0000;
        cpu.DI = 0x0100;
        cpu.Flags.DF = false;
        bus.WriteMemoryByte(0, 0xAA); // STOSB
        cpu.Step();
        Assert.Equal(0x42, bus.ReadMemoryByte(0x0100));
        Assert.Equal(0x0101, cpu.DI);
    }

    [Fact]
    public void Movsb()
    {
        var (cpu, bus) = CreateCpu();
        cpu.DS = 0x0000;
        cpu.SI = 0x0200;
        cpu.ES = 0x0000;
        cpu.DI = 0x0300;
        cpu.Flags.DF = false;
        bus.WriteMemoryByte(0x0200, 0xAB);
        bus.WriteMemoryByte(0, 0xA4); // MOVSB
        cpu.Step();
        Assert.Equal(0xAB, bus.ReadMemoryByte(0x0300));
        Assert.Equal(0x0201, cpu.SI);
        Assert.Equal(0x0301, cpu.DI);
    }

    [Fact]
    public void RepStosb()
    {
        var (cpu, bus) = CreateCpu();
        cpu.AL = 0xFF;
        cpu.ES = 0x0000;
        cpu.DI = 0x0100;
        cpu.CX = 4;
        cpu.Flags.DF = false;
        // REP STOSB
        bus.WriteMemoryByte(0, 0xF3); // REP
        bus.WriteMemoryByte(1, 0xAA); // STOSB
        cpu.Step();
        Assert.Equal(0xFF, bus.ReadMemoryByte(0x0100));
        Assert.Equal(0xFF, bus.ReadMemoryByte(0x0101));
        Assert.Equal(0xFF, bus.ReadMemoryByte(0x0102));
        Assert.Equal(0xFF, bus.ReadMemoryByte(0x0103));
        Assert.Equal(0x00, bus.ReadMemoryByte(0x0104));
        Assert.Equal(0, cpu.CX);
        Assert.Equal(0x0104, cpu.DI);
    }

    [Fact]
    public void JmpShortBackward()
    {
        var (cpu, bus) = CreateCpu();
        cpu.IP = 0x0010;
        bus.WriteMemoryByte(0x10, 0xEB); // JMP short
        bus.WriteMemoryByte(0x11, 0xFC); // -4
        cpu.Step();
        Assert.Equal(0x000E, cpu.IP); // 0x12 + (-4) = 0x0E
    }
}
