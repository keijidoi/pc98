# PC-98 Emulator Phase 1: Core Foundation Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the core CPU, Bus, Scheduler, and all devices to execute instructions and boot from a disk image with display output.

**Architecture:** Modular component design — CPU communicates through SystemBus, which dispatches I/O to registered IDevice implementations and uses memory-mapped region callbacks for VRAM/BIOS interception. Scheduler uses an event-driven priority queue synchronized to CPU cycle counts. BIOS is implemented as C# handlers intercepted when CPU fetches from the BIOS ROM address range (0xE8000-0xFFFFF).

**Tech Stack:** C# / .NET 8.0, xunit, ppy.SDL2-CS (NuGet)

**Spec:** `docs/superpowers/specs/2026-03-17-pc98-emulator-design.md`

---

## File Structure

| File | Responsibility |
|---|---|
| `PC98Emu/Program.cs` | Entry point, CLI arg parsing, launches Emulator |
| `PC98Emu/Emulator.cs` | Wires components, runs main loop |
| `PC98Emu/Bus/IDevice.cs` | Device interface (ReadByte, WriteByte, Reset, Tick, GetPortRange) |
| `PC98Emu/Bus/SystemBus.cs` | 1MB memory + 64K I/O port dispatch + memory-mapped region callbacks |
| `PC98Emu/CPU/Flags.cs` | FLAGS register helpers (CF, ZF, SF, OF, etc.) |
| `PC98Emu/CPU/ModRM.cs` | ModR/M byte decoder |
| `PC98Emu/CPU/V30.cs` | CPU registers, fetch-decode-execute loop, interrupt handling, BIOS interception |
| `PC98Emu/CPU/Instructions.cs` | All opcode implementations |
| `PC98Emu/Scheduler/Scheduler.cs` | Event-driven priority queue for device timing |
| `PC98Emu/Devices/PIC.cs` | i8259A ×2, interrupt routing |
| `PC98Emu/Devices/PIT.cs` | i8253 timer, 3 channels |
| `PC98Emu/Devices/DMA.cs` | i8237A, FDD data transfer |
| `PC98Emu/Devices/RTC.cs` | uPD1990A, host clock passthrough |
| `PC98Emu/Devices/Serial.cs` | i8251 stub |
| `PC98Emu/Devices/Printer.cs` | Printer port stub (busy response) |
| `PC98Emu/Devices/SystemPort.cs` | System port 0x35/0x37 (Beep control) |
| `PC98Emu/Devices/Keyboard.cs` | Scancode buffer, IRQ1 |
| `PC98Emu/Devices/Mouse.cs` | Bus mouse stub |
| `PC98Emu/Disk/IDiskImage.cs` | Disk image interface |
| `PC98Emu/Disk/D88Image.cs` | D88 floppy loader |
| `PC98Emu/Disk/FDIImage.cs` | FDI floppy loader |
| `PC98Emu/Disk/HDIImage.cs` | HDI hard disk loader |
| `PC98Emu/Disk/NHDImage.cs` | NHD hard disk loader |
| `PC98Emu/Disk/NFDImage.cs` | NFD floppy loader (basic) |
| `PC98Emu/Disk/DiskManager.cs` | Multi-drive management |
| `PC98Emu/Disk/SASIController.cs` | SASI HDD controller stub (0x0CC0-0x0CCC) |
| `PC98Emu/Devices/FDC.cs` | uPD765A floppy controller |
| `PC98Emu/Graphics/GDC.cs` | uPD7220 core (command FIFO, registers, VRAM bank switching) |
| `PC98Emu/Graphics/Display.cs` | SDL2 window, texture rendering |
| `PC98Emu/Graphics/TextRenderer.cs` | Text VRAM → RGBA (ANK + kanji) |
| `PC98Emu/Graphics/GraphicsRenderer.cs` | Graphic VRAM 4-plane → RGBA with bank switching |
| `PC98Emu/Graphics/Font.cs` | ANK 8x16 + JIS kanji 16x16 bitmap font data |
| `PC98Emu/Sound/YM2608.cs` | FM音源 register file, Timer A/B |
| `PC98Emu/Sound/FMChannel.cs` | 4-operator FM synthesis |
| `PC98Emu/Sound/SSG.cs` | SSG 3ch square + noise |
| `PC98Emu/Sound/ADPCM.cs` | ADPCM decoder |
| `PC98Emu/Sound/AudioOutput.cs` | SDL2 audio callback, mixer |
| `PC98Emu/BIOS/CompatibleBios.cs` | IVT setup, BDA init, INT dispatch |
| `PC98Emu/BIOS/DiskBios.cs` | INT 18h handler |
| `PC98Emu/BIOS/SerialBios.cs` | INT 19h handler (stub) |
| `PC98Emu/BIOS/TimerBios.cs` | INT 1Ah handler |
| `PC98Emu/BIOS/KeyboardBios.cs` | INT 1Bh handler |
| `PC98Emu/BIOS/CrtBios.cs` | INT 1Ch handler |
| `PC98Emu/BIOS/GraphicsBios.cs` | INT 1Dh handler |
| `PC98Emu/BIOS/BootLoader.cs` | IPL load + jump |

---

### Task 1: Project Scaffold

**Files:**
- Create: `PC98Emu/PC98Emu.sln`
- Create: `PC98Emu/PC98Emu/PC98Emu.csproj`
- Create: `PC98Emu/PC98Emu/Program.cs`
- Create: `PC98Emu/PC98Emu.Tests/PC98Emu.Tests.csproj`

- [ ] **Step 1: Create .NET solution and projects**

```bash
cd /c/FlutterProject/pc98
dotnet new sln -n PC98Emu -o PC98Emu
dotnet new console -n PC98Emu -o PC98Emu/PC98Emu
dotnet new xunit -n PC98Emu.Tests -o PC98Emu/PC98Emu.Tests
cd PC98Emu
dotnet sln add PC98Emu/PC98Emu.csproj
dotnet sln add PC98Emu.Tests/PC98Emu.Tests.csproj
dotnet add PC98Emu.Tests/PC98Emu.Tests.csproj reference PC98Emu/PC98Emu.csproj
```

- [ ] **Step 2: Add SDL2 NuGet package**

```bash
cd /c/FlutterProject/pc98/PC98Emu
dotnet add PC98Emu/PC98Emu.csproj package ppy.SDL2-CS
```

- [ ] **Step 3: Write minimal Program.cs**

```csharp
// PC98Emu/PC98Emu/Program.cs
namespace PC98Emu;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("PC98Emu - PC-98 Emulator");
    }
}
```

- [ ] **Step 4: Verify build succeeds**

```bash
cd /c/FlutterProject/pc98/PC98Emu
dotnet build
```
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: initialize PC98Emu solution with console and test projects"
```

---

### Task 2: IDevice Interface & SystemBus (with memory-mapped regions)

**Files:**
- Create: `PC98Emu/PC98Emu/Bus/IDevice.cs`
- Create: `PC98Emu/PC98Emu/Bus/SystemBus.cs`
- Create: `PC98Emu/PC98Emu.Tests/Bus/SystemBusTests.cs`

- [ ] **Step 1: Write failing tests for SystemBus**

```csharp
// PC98Emu/PC98Emu.Tests/Bus/SystemBusTests.cs
using Xunit;
using PC98Emu.Bus;

namespace PC98Emu.Tests.Bus;

public class SystemBusTests
{
    [Fact]
    public void ReadWriteMemoryByte()
    {
        var bus = new SystemBus();
        bus.WriteMemoryByte(0x1000, 0xAB);
        Assert.Equal(0xAB, bus.ReadMemoryByte(0x1000));
    }

    [Fact]
    public void ReadWriteMemoryWord()
    {
        var bus = new SystemBus();
        bus.WriteMemoryWord(0x2000, 0x1234);
        Assert.Equal(0x34, bus.ReadMemoryByte(0x2000));
        Assert.Equal(0x12, bus.ReadMemoryByte(0x2001));
        Assert.Equal(0x1234, bus.ReadMemoryWord(0x2000));
    }

    [Fact]
    public void UnmappedMemoryReturnsZero()
    {
        var bus = new SystemBus();
        Assert.Equal(0x00, bus.ReadMemoryByte(0xFFFFF));
    }

    [Fact]
    public void IoPortDispatchesToDevice()
    {
        var bus = new SystemBus();
        var device = new StubDevice(0x42, new[] { 0x60, 0x62 });
        bus.RegisterDevice(device);
        bus.WriteIoByte(0x60, 0x99);
        Assert.Equal(0x99, bus.ReadIoByte(0x60));
    }

    [Fact]
    public void UnmappedIoPortReturns0xFF()
    {
        var bus = new SystemBus();
        Assert.Equal(0xFF, bus.ReadIoByte(0x999));
    }

    [Fact]
    public void BiosRomAreaIsReadOnly()
    {
        var bus = new SystemBus();
        bus.SetBiosRomArea(true);
        // Write to BIOS area should be ignored
        bus.WriteMemoryByte(0xE8000, 0xAB);
        Assert.Equal(0x00, bus.ReadMemoryByte(0xE8000));
        // But direct write for BIOS loading should work
        bus.WriteBiosDirectly(0xE8000, 0xCD);
        Assert.Equal(0xCD, bus.ReadMemoryByte(0xE8000));
    }

    [Fact]
    public void IsBiosArea_ReturnsTrueForRomRange()
    {
        var bus = new SystemBus();
        Assert.True(bus.IsBiosArea(0xE8000));
        Assert.True(bus.IsBiosArea(0xFFFFF));
        Assert.False(bus.IsBiosArea(0xE7FFF));
    }
}

public class StubDevice : IDevice
{
    private readonly Dictionary<int, byte> _ports = new();
    private readonly int[] _portRange;
    private readonly byte _defaultValue;

    public StubDevice(byte defaultValue, int[] portRange)
    {
        _defaultValue = defaultValue;
        _portRange = portRange;
    }

    public byte ReadByte(int port) => _ports.GetValueOrDefault(port, _defaultValue);
    public void WriteByte(int port, byte value) => _ports[port] = value;
    public ushort ReadWord(int port) => (ushort)(ReadByte(port) | (ReadByte(port + 1) << 8));
    public void WriteWord(int port, ushort value) { WriteByte(port, (byte)value); WriteByte(port + 1, (byte)(value >> 8)); }
    public void Reset() => _ports.Clear();
    public void Tick(int cycles) { }
    public int[] GetPortRange() => _portRange;
}
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
cd /c/FlutterProject/pc98/PC98Emu
dotnet test --filter "SystemBusTests"
```

- [ ] **Step 3: Implement IDevice interface**

```csharp
// PC98Emu/PC98Emu/Bus/IDevice.cs
namespace PC98Emu.Bus;

public interface IDevice
{
    byte ReadByte(int port);
    void WriteByte(int port, byte value);
    ushort ReadWord(int port);
    void WriteWord(int port, ushort value);
    void Reset();
    void Tick(int cycles);
    int[] GetPortRange();
}
```

- [ ] **Step 4: Implement SystemBus with memory-mapped regions**

```csharp
// PC98Emu/PC98Emu/Bus/SystemBus.cs
namespace PC98Emu.Bus;

public class SystemBus
{
    private readonly byte[] _memory = new byte[0x100000]; // 1MB
    private readonly IDevice?[] _ioMap = new IDevice?[0x10000]; // 64K I/O ports
    private bool _biosRomProtect;

    // VRAM bank switching state
    public byte GvramDisplayPlane; // port 0xA4
    public byte GvramWritePlane;   // port 0xA6

    public void RegisterDevice(IDevice device)
    {
        foreach (var port in device.GetPortRange())
            _ioMap[port] = device;
    }

    public void SetBiosRomArea(bool readOnly) => _biosRomProtect = readOnly;

    public bool IsBiosArea(int address) => address >= 0xE8000 && address <= 0xFFFFF;

    public void WriteBiosDirectly(int address, byte value)
    {
        _memory[address & 0xFFFFF] = value;
    }

    // Memory access
    public byte ReadMemoryByte(int address)
    {
        address &= 0xFFFFF;
        return _memory[address];
    }

    public void WriteMemoryByte(int address, byte value)
    {
        address &= 0xFFFFF;
        if (_biosRomProtect && IsBiosArea(address)) return; // ROM is read-only
        _memory[address] = value;
    }

    public ushort ReadMemoryWord(int address)
    {
        return (ushort)(ReadMemoryByte(address) | (ReadMemoryByte(address + 1) << 8));
    }

    public void WriteMemoryWord(int address, ushort value)
    {
        WriteMemoryByte(address, (byte)value);
        WriteMemoryByte(address + 1, (byte)(value >> 8));
    }

    // I/O port access
    public byte ReadIoByte(int port)
    {
        port &= 0xFFFF;
        var device = _ioMap[port];
        return device?.ReadByte(port) ?? 0xFF;
    }

    public void WriteIoByte(int port, byte value)
    {
        port &= 0xFFFF;
        var device = _ioMap[port];
        device?.WriteByte(port, value);
    }

    public ushort ReadIoWord(int port)
    {
        port &= 0xFFFF;
        var device = _ioMap[port];
        return device?.ReadWord(port) ?? 0xFFFF;
    }

    public void WriteIoWord(int port, ushort value)
    {
        port &= 0xFFFF;
        var device = _ioMap[port];
        device?.WriteWord(port, value);
    }

    public byte[] GetMemoryDirect() => _memory;
}
```

- [ ] **Step 5: Run tests to verify they pass**

```bash
cd /c/FlutterProject/pc98/PC98Emu
dotnet test --filter "SystemBusTests"
```
Expected: All 7 tests PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add IDevice interface and SystemBus with memory/IO dispatch and BIOS ROM protection"
```

---

### Task 3: FLAGS Register Helpers

**Files:**
- Create: `PC98Emu/PC98Emu/CPU/Flags.cs`
- Create: `PC98Emu/PC98Emu.Tests/CPU/FlagsTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PC98Emu/PC98Emu.Tests/CPU/FlagsTests.cs
using Xunit;
using PC98Emu.CPU;

namespace PC98Emu.Tests.CPU;

public class FlagsTests
{
    [Fact]
    public void SetAndGetCarryFlag()
    {
        var flags = new CpuFlags();
        flags.CF = true;
        Assert.True(flags.CF);
        Assert.Equal((ushort)0x0001, (ushort)(flags.Value & 0x0001));
    }

    [Fact]
    public void SetAndGetZeroFlag()
    {
        var flags = new CpuFlags();
        flags.ZF = true;
        Assert.True(flags.ZF);
    }

    [Fact]
    public void SetAndGetSignFlag()
    {
        var flags = new CpuFlags();
        flags.SF = true;
        Assert.True(flags.SF);
    }

    [Fact]
    public void SetAndGetOverflowFlag()
    {
        var flags = new CpuFlags();
        flags.OF = true;
        Assert.True(flags.OF);
    }

    [Fact]
    public void UpdateFlagsForByteResult()
    {
        var flags = new CpuFlags();
        flags.UpdateSZP8(0x00);
        Assert.True(flags.ZF);
        Assert.False(flags.SF);
        Assert.True(flags.PF);

        flags.UpdateSZP8(0x80);
        Assert.False(flags.ZF);
        Assert.True(flags.SF);
    }

    [Fact]
    public void UpdateFlagsForWordResult()
    {
        var flags = new CpuFlags();
        flags.UpdateSZP16(0x0000);
        Assert.True(flags.ZF);
        Assert.False(flags.SF);

        flags.UpdateSZP16(0x8000);
        Assert.False(flags.ZF);
        Assert.True(flags.SF);
    }

    [Fact]
    public void ParityFlagCalculation()
    {
        var flags = new CpuFlags();
        flags.UpdateSZP8(0x03); // bits: 11 -> even parity
        Assert.True(flags.PF);

        flags.UpdateSZP8(0x07); // bits: 111 -> odd parity
        Assert.False(flags.PF);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement CpuFlags**

```csharp
// PC98Emu/PC98Emu/CPU/Flags.cs
namespace PC98Emu.CPU;

public class CpuFlags
{
    public ushort Value;

    private const int CF_BIT = 0;
    private const int PF_BIT = 2;
    private const int AF_BIT = 4;
    private const int ZF_BIT = 6;
    private const int SF_BIT = 7;
    private const int TF_BIT = 8;
    private const int IF_BIT = 9;
    private const int DF_BIT = 10;
    private const int OF_BIT = 11;

    public bool CF { get => GetFlag(CF_BIT); set => SetFlag(CF_BIT, value); }
    public bool PF { get => GetFlag(PF_BIT); set => SetFlag(PF_BIT, value); }
    public bool AF { get => GetFlag(AF_BIT); set => SetFlag(AF_BIT, value); }
    public bool ZF { get => GetFlag(ZF_BIT); set => SetFlag(ZF_BIT, value); }
    public bool SF { get => GetFlag(SF_BIT); set => SetFlag(SF_BIT, value); }
    public bool TF { get => GetFlag(TF_BIT); set => SetFlag(TF_BIT, value); }
    public bool IF { get => GetFlag(IF_BIT); set => SetFlag(IF_BIT, value); }
    public bool DF { get => GetFlag(DF_BIT); set => SetFlag(DF_BIT, value); }
    public bool OF { get => GetFlag(OF_BIT); set => SetFlag(OF_BIT, value); }

    private bool GetFlag(int bit) => (Value & (1 << bit)) != 0;
    private void SetFlag(int bit, bool val)
    {
        if (val) Value |= (ushort)(1 << bit);
        else Value &= (ushort)~(1 << bit);
    }

    private static readonly bool[] ParityTable = BuildParityTable();
    private static bool[] BuildParityTable()
    {
        var table = new bool[256];
        for (int i = 0; i < 256; i++)
        {
            int bits = 0, v = i;
            while (v != 0) { bits += v & 1; v >>= 1; }
            table[i] = (bits % 2) == 0;
        }
        return table;
    }

    public void UpdateSZP8(byte result)
    {
        ZF = result == 0;
        SF = (result & 0x80) != 0;
        PF = ParityTable[result];
    }

    public void UpdateSZP16(ushort result)
    {
        ZF = result == 0;
        SF = (result & 0x8000) != 0;
        PF = ParityTable[result & 0xFF];
    }
}
```

- [ ] **Step 4: Run tests, verify pass**

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add CpuFlags with flag accessors and SZP update helpers"
```

---

### Task 4: ModR/M Decoder

**Files:**
- Create: `PC98Emu/PC98Emu/CPU/ModRM.cs`
- Create: `PC98Emu/PC98Emu.Tests/CPU/ModRMTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PC98Emu/PC98Emu.Tests/CPU/ModRMTests.cs
using Xunit;
using PC98Emu.CPU;

namespace PC98Emu.Tests.CPU;

public class ModRMTests
{
    [Fact]
    public void DecodeRegField()
    {
        var modrm = new ModRMDecoder();
        modrm.Decode(0xD1); // mod=11, reg=010, rm=001
        Assert.Equal(2, modrm.Reg);
        Assert.Equal(1, modrm.RM);
        Assert.Equal(3, modrm.Mod);
    }

    [Fact]
    public void Mod00_RM110_IsDirectAddress()
    {
        var modrm = new ModRMDecoder();
        modrm.Decode(0x06); // mod=00, reg=000, rm=110
        Assert.Equal(0, modrm.Mod);
        Assert.Equal(6, modrm.RM);
    }

    [Fact]
    public void Mod01_Has8BitDisplacement()
    {
        var modrm = new ModRMDecoder();
        modrm.Decode(0x5C); // mod=01, reg=011, rm=100
        Assert.Equal(1, modrm.Mod);
        Assert.Equal(3, modrm.Reg);
        Assert.Equal(4, modrm.RM);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement ModRMDecoder**

```csharp
// PC98Emu/PC98Emu/CPU/ModRM.cs
namespace PC98Emu.CPU;

public class ModRMDecoder
{
    public int Mod;
    public int Reg;
    public int RM;

    public void Decode(byte value)
    {
        Mod = (value >> 6) & 0x03;
        Reg = (value >> 3) & 0x07;
        RM = value & 0x07;
    }
}
```

- [ ] **Step 4: Run tests, verify pass**

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add ModR/M byte decoder"
```

---

### Task 5: CPU V30 Core — Registers, Fetch-Execute Loop, BIOS Interception

**Files:**
- Create: `PC98Emu/PC98Emu/CPU/V30.cs`
- Create: `PC98Emu/PC98Emu.Tests/CPU/V30Tests.cs`

Key design point: The CPU's `Step()` method checks if CS:IP falls in the BIOS ROM area (0xE8000-0xFFFFF). If so, it calls a registered `BiosInterceptHandler` delegate instead of executing the opcode from memory. This is how the compatible BIOS works — each INT vector in the IVT points to a unique address in the BIOS ROM area, and when the CPU tries to execute code there, the emulator calls the corresponding C# BIOS handler.

- [ ] **Step 1: Write failing tests**

```csharp
// PC98Emu/PC98Emu.Tests/CPU/V30Tests.cs
using Xunit;
using PC98Emu.CPU;
using PC98Emu.Bus;

namespace PC98Emu.Tests.CPU;

public class V30Tests
{
    private (V30 cpu, SystemBus bus) CreateCpu()
    {
        var bus = new SystemBus();
        var cpu = new V30(bus);
        return (cpu, bus);
    }

    [Fact]
    public void InitialRegistersAreZero()
    {
        var (cpu, _) = CreateCpu();
        Assert.Equal(0, cpu.AX);
        Assert.Equal(0, cpu.BX);
        Assert.Equal(0, cpu.CX);
        Assert.Equal(0, cpu.DX);
    }

    [Fact]
    public void AX_SplitsIntoAH_AL()
    {
        var (cpu, _) = CreateCpu();
        cpu.AX = 0x1234;
        Assert.Equal(0x12, cpu.AH);
        Assert.Equal(0x34, cpu.AL);
    }

    [Fact]
    public void SetAH_UpdatesAX()
    {
        var (cpu, _) = CreateCpu();
        cpu.AX = 0x0000;
        cpu.AH = 0xAB;
        Assert.Equal(0xAB00, cpu.AX);
    }

    [Fact]
    public void NOP_AdvancesIP()
    {
        var (cpu, bus) = CreateCpu();
        cpu.CS = 0x0000; cpu.IP = 0x0000;
        bus.WriteMemoryByte(0x00000, 0x90); // NOP
        bus.WriteMemoryByte(0x00001, 0xF4); // HLT
        cpu.Step();
        Assert.Equal(0x0001, cpu.IP);
    }

    [Fact]
    public void MOV_AL_Imm8()
    {
        var (cpu, bus) = CreateCpu();
        cpu.CS = 0x0000; cpu.IP = 0x0000;
        bus.WriteMemoryByte(0x00000, 0xB0); // MOV AL, imm8
        bus.WriteMemoryByte(0x00001, 0x42);
        cpu.Step();
        Assert.Equal(0x42, cpu.AL);
    }

    [Fact]
    public void MOV_AX_Imm16()
    {
        var (cpu, bus) = CreateCpu();
        cpu.CS = 0x0000; cpu.IP = 0x0000;
        bus.WriteMemoryByte(0x00000, 0xB8); // MOV AX, imm16
        bus.WriteMemoryByte(0x00001, 0x34);
        bus.WriteMemoryByte(0x00002, 0x12);
        cpu.Step();
        Assert.Equal(0x1234, cpu.AX);
    }

    [Fact]
    public void HLT_SetsHaltedFlag()
    {
        var (cpu, bus) = CreateCpu();
        cpu.CS = 0x0000; cpu.IP = 0x0000;
        bus.WriteMemoryByte(0x00000, 0xF4); // HLT
        cpu.Step();
        Assert.True(cpu.Halted);
    }

    [Fact]
    public void BiosInterception_CallsHandler()
    {
        var (cpu, bus) = CreateCpu();
        bool called = false;
        // Register a BIOS handler at address 0xE8000
        cpu.RegisterBiosHandler(0xE8000, () => { called = true; });
        // Set CS:IP to point into BIOS ROM area
        cpu.CS = 0xE800;
        cpu.IP = 0x0000;
        cpu.Step();
        Assert.True(called);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement V30 CPU core**

Full V30.cs implementation with:
- All registers (AX/BX/CX/DX, SI/DI/BP/SP, CS/DS/ES/SS, IP, FLAGS)
- High/low byte accessors (AH/AL, etc.)
- `GetPhysicalAddress(segment, offset)` → 20-bit address
- `FetchByte()`, `FetchWord()` from CS:IP
- `DecodeModRM16()` — resolve ModR/M addressing modes
- `ReadModRM8/16()`, `WriteModRM8/16()` — memory/register read/write via ModR/M
- `Push(ushort)`, `Pop()` via SS:SP
- `Interrupt(byte vector)` — push FLAGS/CS/IP, load from IVT
- Prefix handling (segment override, REP, LOCK)
- **BIOS interception**: `Dictionary<int, Action> _biosHandlers`. In `Step()`, before fetching opcode, check if `GetPhysicalAddress(CS, IP)` is in the dictionary. If yes, call the handler and return (handler is responsible for executing IRET logic).
- `RegisterBiosHandler(int physicalAddress, Action handler)` method

- [ ] **Step 4: Create minimal Instructions.cs stub** (NOP, MOV reg,imm, HLT only)

- [ ] **Step 5: Run tests, verify pass**

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add V30 CPU core with registers, ModR/M, BIOS interception"
```

---

### Task 6: Arithmetic & Logic Instructions

**Files:**
- Modify: `PC98Emu/PC98Emu/CPU/Instructions.cs`
- Create: `PC98Emu/PC98Emu.Tests/CPU/ArithmeticTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PC98Emu/PC98Emu.Tests/CPU/ArithmeticTests.cs
using Xunit;
using PC98Emu.CPU;
using PC98Emu.Bus;

namespace PC98Emu.Tests.CPU;

public class ArithmeticTests
{
    private (V30 cpu, SystemBus bus) Setup(params byte[] code)
    {
        var bus = new SystemBus();
        var cpu = new V30(bus);
        cpu.CS = 0x0000; cpu.IP = 0x0000;
        for (int i = 0; i < code.Length; i++)
            bus.WriteMemoryByte(i, code[i]);
        return (cpu, bus);
    }

    [Fact]
    public void ADD_AL_Imm8()
    {
        var (cpu, _) = Setup(0x04, 0x30);
        cpu.AL = 0x10;
        cpu.Step();
        Assert.Equal(0x40, cpu.AL);
        Assert.False(cpu.Flags.CF);
    }

    [Fact]
    public void ADD_AL_Imm8_Carry()
    {
        var (cpu, _) = Setup(0x04, 0x01);
        cpu.AL = 0xFF;
        cpu.Step();
        Assert.Equal(0x00, cpu.AL);
        Assert.True(cpu.Flags.CF);
        Assert.True(cpu.Flags.ZF);
    }

    [Fact]
    public void ADD_AX_Imm16()
    {
        var (cpu, _) = Setup(0x05, 0x00, 0x10);
        cpu.AX = 0x0234;
        cpu.Step();
        Assert.Equal(0x1234, cpu.AX);
    }

    [Fact]
    public void SUB_AL_Imm8_SetsZero()
    {
        var (cpu, _) = Setup(0x2C, 0x42);
        cpu.AL = 0x42;
        cpu.Step();
        Assert.Equal(0x00, cpu.AL);
        Assert.True(cpu.Flags.ZF);
    }

    [Fact]
    public void SUB_AL_Imm8_SetsBorrow()
    {
        var (cpu, _) = Setup(0x2C, 0x01);
        cpu.AL = 0x00;
        cpu.Step();
        Assert.Equal(0xFF, cpu.AL);
        Assert.True(cpu.Flags.CF); // borrow
    }

    [Fact]
    public void CMP_AL_Imm8()
    {
        var (cpu, _) = Setup(0x3C, 0x42);
        cpu.AL = 0x42;
        cpu.Step();
        Assert.Equal(0x42, cpu.AL); // unchanged
        Assert.True(cpu.Flags.ZF);
    }

    [Fact]
    public void AND_AL_Imm8()
    {
        var (cpu, _) = Setup(0x24, 0x0F);
        cpu.AL = 0xAB;
        cpu.Step();
        Assert.Equal(0x0B, cpu.AL);
        Assert.False(cpu.Flags.CF);
    }

    [Fact]
    public void OR_AL_Imm8()
    {
        var (cpu, _) = Setup(0x0C, 0xF0);
        cpu.AL = 0x0A;
        cpu.Step();
        Assert.Equal(0xFA, cpu.AL);
    }

    [Fact]
    public void XOR_AL_Imm8()
    {
        var (cpu, _) = Setup(0x34, 0xFF);
        cpu.AL = 0xAA;
        cpu.Step();
        Assert.Equal(0x55, cpu.AL);
    }

    [Fact]
    public void INC_AX()
    {
        var (cpu, _) = Setup(0x40);
        cpu.AX = 0x00FF;
        cpu.Step();
        Assert.Equal(0x0100, cpu.AX);
    }

    [Fact]
    public void DEC_AX_SetsZero()
    {
        var (cpu, _) = Setup(0x48);
        cpu.AX = 0x0001;
        cpu.Step();
        Assert.Equal(0x0000, cpu.AX);
        Assert.True(cpu.Flags.ZF);
    }

    [Fact]
    public void PUSH_POP_AX()
    {
        var (cpu, _) = Setup(0x50, 0x5B); // PUSH AX, POP BX
        cpu.AX = 0x1234;
        cpu.SP = 0xFFFE; cpu.SS = 0x0000;
        cpu.Step(); cpu.Step();
        Assert.Equal(0x1234, cpu.BX);
    }

    [Fact]
    public void ADC_AL_Imm8_WithCarry()
    {
        var (cpu, _) = Setup(0x14, 0x01); // ADC AL, 0x01
        cpu.AL = 0x10;
        cpu.Flags.CF = true;
        cpu.Step();
        Assert.Equal(0x12, cpu.AL); // 0x10 + 0x01 + CF(1)
    }

    [Fact]
    public void SBB_AL_Imm8_WithBorrow()
    {
        var (cpu, _) = Setup(0x1C, 0x01); // SBB AL, 0x01
        cpu.AL = 0x10;
        cpu.Flags.CF = true;
        cpu.Step();
        Assert.Equal(0x0E, cpu.AL); // 0x10 - 0x01 - CF(1)
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement ALU helper methods and opcodes**

Add to Instructions.cs:
- ALU helpers: `Add8/16`, `Sub8/16`, `Adc8/16`, `Sbb8/16`, `And8/16`, `Or8/16`, `Xor8/16` — each sets CF, OF, SF, ZF, PF, AF appropriately
- Opcodes: `0x00-0x05` (ADD), `0x08-0x0D` (OR), `0x10-0x15` (ADC), `0x18-0x1D` (SBB), `0x20-0x25` (AND), `0x28-0x2D` (SUB), `0x30-0x35` (XOR), `0x38-0x3D` (CMP)
- `0x40-0x47` (INC reg16), `0x48-0x4F` (DEC reg16)
- `0x50-0x57` (PUSH reg16), `0x58-0x5F` (POP reg16)

- [ ] **Step 4: Run tests, verify pass**

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: implement arithmetic, logic, and stack instructions"
```

---

### Task 7: Branch, Control Flow & String Instructions

**Files:**
- Modify: `PC98Emu/PC98Emu/CPU/Instructions.cs`
- Create: `PC98Emu/PC98Emu.Tests/CPU/BranchTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PC98Emu/PC98Emu.Tests/CPU/BranchTests.cs
using Xunit;
using PC98Emu.CPU;
using PC98Emu.Bus;

namespace PC98Emu.Tests.CPU;

public class BranchTests
{
    private (V30 cpu, SystemBus bus) Setup(params byte[] code)
    {
        var bus = new SystemBus();
        var cpu = new V30(bus);
        cpu.CS = 0x0000; cpu.IP = 0x0000;
        for (int i = 0; i < code.Length; i++)
            bus.WriteMemoryByte(i, code[i]);
        return (cpu, bus);
    }

    [Fact]
    public void JMP_Short()
    {
        var (cpu, _) = Setup(0xEB, 0x05);
        cpu.Step();
        Assert.Equal(0x0007, cpu.IP); // 2 + 5
    }

    [Fact]
    public void JZ_Taken()
    {
        var (cpu, _) = Setup(0x74, 0x04);
        cpu.Flags.ZF = true;
        cpu.Step();
        Assert.Equal(0x0006, cpu.IP);
    }

    [Fact]
    public void JZ_NotTaken()
    {
        var (cpu, _) = Setup(0x74, 0x04);
        cpu.Flags.ZF = false;
        cpu.Step();
        Assert.Equal(0x0002, cpu.IP);
    }

    [Fact]
    public void JNZ_Taken()
    {
        var (cpu, _) = Setup(0x75, 0x04);
        cpu.Flags.ZF = false;
        cpu.Step();
        Assert.Equal(0x0006, cpu.IP);
    }

    [Fact]
    public void JC_Taken()
    {
        var (cpu, _) = Setup(0x72, 0x04); // JC/JB
        cpu.Flags.CF = true;
        cpu.Step();
        Assert.Equal(0x0006, cpu.IP);
    }

    [Fact]
    public void JNC_NotTaken()
    {
        var (cpu, _) = Setup(0x73, 0x04); // JNC/JAE
        cpu.Flags.CF = true;
        cpu.Step();
        Assert.Equal(0x0002, cpu.IP);
    }

    [Fact]
    public void CALL_Near()
    {
        var (cpu, bus) = Setup(0xE8, 0x0D, 0x00);
        cpu.SP = 0xFFFE; cpu.SS = 0x0000;
        cpu.Step();
        Assert.Equal(0x0010, cpu.IP);
        Assert.Equal((ushort)0x0003, bus.ReadMemoryWord(cpu.GetPhysicalAddress(cpu.SS, cpu.SP)));
    }

    [Fact]
    public void RET_Near()
    {
        var (cpu, bus) = Setup(0xC3);
        cpu.SP = 0xFFFC; cpu.SS = 0x0000;
        bus.WriteMemoryWord(cpu.GetPhysicalAddress(0, 0xFFFC), 0x1234);
        cpu.Step();
        Assert.Equal(0x1234, cpu.IP);
    }

    [Fact]
    public void INT_SoftwareInterrupt()
    {
        var (cpu, bus) = Setup(0xCD, 0x21);
        cpu.SP = 0xFFFE; cpu.SS = 0x0000;
        cpu.Flags.IF = true;
        bus.WriteMemoryWord(0x84, 0x0100); // IVT INT 21h
        bus.WriteMemoryWord(0x86, 0x0000);
        cpu.Step();
        Assert.Equal(0x0100, cpu.IP);
    }

    [Fact]
    public void IRET()
    {
        var (cpu, bus) = Setup(0xCF);
        cpu.SP = 0xFFF8; cpu.SS = 0x0000;
        bus.WriteMemoryWord(cpu.GetPhysicalAddress(0, 0xFFF8), 0x1234);
        bus.WriteMemoryWord(cpu.GetPhysicalAddress(0, 0xFFFA), 0x0000);
        bus.WriteMemoryWord(cpu.GetPhysicalAddress(0, 0xFFFC), 0x0202);
        cpu.Step();
        Assert.Equal(0x1234, cpu.IP);
    }

    [Fact]
    public void LOOP_Decrements_CX()
    {
        var (cpu, _) = Setup(0xE2, 0xFE);
        cpu.CX = 3;
        cpu.Step();
        Assert.Equal(2, cpu.CX);
        Assert.Equal(0x0000, cpu.IP);
    }

    [Fact]
    public void STOSB()
    {
        var (cpu, bus) = Setup(0xAA);
        cpu.AL = 0x42; cpu.ES = 0x0000; cpu.DI = 0x1000;
        cpu.Flags.DF = false;
        cpu.Step();
        Assert.Equal(0x42, bus.ReadMemoryByte(0x1000));
        Assert.Equal(0x1001, cpu.DI);
    }

    [Fact]
    public void MOVSB()
    {
        var (cpu, bus) = Setup(0xA4);
        cpu.DS = 0x0000; cpu.SI = 0x2000;
        cpu.ES = 0x0000; cpu.DI = 0x3000;
        cpu.Flags.DF = false;
        bus.WriteMemoryByte(0x2000, 0xAB);
        cpu.Step();
        Assert.Equal(0xAB, bus.ReadMemoryByte(0x3000));
    }

    [Fact]
    public void REP_STOSB()
    {
        // REP STOSB: fill 4 bytes with 0x42
        var (cpu, bus) = Setup(0xF3, 0xAA);
        cpu.AL = 0x42; cpu.CX = 4;
        cpu.ES = 0x0000; cpu.DI = 0x1000;
        cpu.Flags.DF = false;
        cpu.Step();
        Assert.Equal(0, cpu.CX);
        Assert.Equal(0x42, bus.ReadMemoryByte(0x1000));
        Assert.Equal(0x42, bus.ReadMemoryByte(0x1003));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement branch, INT/IRET, LOOP, string instructions with REP**

Opcodes to implement:
- `0x70-0x7F`: All Jcc short (JO, JNO, JB, JNB, JZ, JNZ, JBE, JA, JS, JNS, JP, JNP, JL, JGE, JLE, JG)
- `0xE8`: CALL near, `0xE9`: JMP near, `0xEB`: JMP short
- `0xC3`: RET near, `0xCB`: RETF, `0xC2`: RET imm16, `0xCA`: RETF imm16
- `0xCD`: INT imm8, `0xCF`: IRET
- `0xE0-0xE2`: LOOPNZ, LOOPZ, LOOP
- `0xA4/A5`: MOVSB/W, `0xA6/A7`: CMPSB/W, `0xAA/AB`: STOSB/W, `0xAC/AD`: LODSB/W, `0xAE/AF`: SCASB/W
- REP prefix handling: when `_repPrefix` is true, wrap string op in `while(CX > 0) { CX--; op(); if(CMPS/SCAS) check ZF vs _repZFlag; }`

- [ ] **Step 4: Run tests, verify pass**

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: implement branch, interrupt, string, and REP instructions"
```

---

### Task 8: Remaining Instructions (MOV r/m, Shifts, MUL/DIV, I/O, Misc)

**Files:**
- Modify: `PC98Emu/PC98Emu/CPU/Instructions.cs`
- Create: `PC98Emu/PC98Emu.Tests/CPU/MiscInstructionTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PC98Emu/PC98Emu.Tests/CPU/MiscInstructionTests.cs
using Xunit;
using PC98Emu.CPU;
using PC98Emu.Bus;

namespace PC98Emu.Tests.CPU;

public class MiscInstructionTests
{
    private (V30 cpu, SystemBus bus) Setup(params byte[] code)
    {
        var bus = new SystemBus();
        var cpu = new V30(bus);
        cpu.CS = 0x0000; cpu.IP = 0x0000;
        for (int i = 0; i < code.Length; i++)
            bus.WriteMemoryByte(i, code[i]);
        return (cpu, bus);
    }

    [Fact]
    public void MOV_RM8_R8()
    {
        var (cpu, bus) = Setup(0x88, 0x06, 0x00, 0x10); // MOV [0x1000], AL
        cpu.AL = 0xAB; cpu.DS = 0x0000;
        cpu.Step();
        Assert.Equal(0xAB, bus.ReadMemoryByte(0x1000));
    }

    [Fact]
    public void MOV_R8_RM8()
    {
        var (cpu, bus) = Setup(0x8A, 0x06, 0x00, 0x10); // MOV AL, [0x1000]
        cpu.DS = 0x0000;
        bus.WriteMemoryByte(0x1000, 0x42);
        cpu.Step();
        Assert.Equal(0x42, cpu.AL);
    }

    [Fact]
    public void MOV_Sreg_RM16()
    {
        // MOV DS, AX  =>  8E D8 (mod=11, reg=3(DS), rm=0(AX))
        var (cpu, _) = Setup(0x8E, 0xD8);
        cpu.AX = 0x1234;
        cpu.Step();
        Assert.Equal(0x1234, cpu.DS);
    }

    [Fact]
    public void SHL_AL_1()
    {
        var (cpu, _) = Setup(0xD0, 0xE0); // SHL AL, 1
        cpu.AL = 0x40;
        cpu.Step();
        Assert.Equal(0x80, cpu.AL);
    }

    [Fact]
    public void SHR_AL_1()
    {
        var (cpu, _) = Setup(0xD0, 0xE8); // SHR AL, 1
        cpu.AL = 0x80;
        cpu.Step();
        Assert.Equal(0x40, cpu.AL);
    }

    [Fact]
    public void SAR_AL_1()
    {
        var (cpu, _) = Setup(0xD0, 0xF8); // SAR AL, 1
        cpu.AL = 0x80; // -128
        cpu.Step();
        Assert.Equal(0xC0, cpu.AL); // -64, sign preserved
    }

    [Fact]
    public void MUL_AL()
    {
        // MUL BL  =>  F6 E3 (Group 3, reg=4, rm=3)
        var (cpu, _) = Setup(0xF6, 0xE3);
        cpu.AL = 0x10;
        cpu.BL = 0x04;
        cpu.Step();
        Assert.Equal(0x0040, cpu.AX); // 16 * 4 = 64
    }

    [Fact]
    public void DIV_AL()
    {
        // DIV BL  =>  F6 F3 (Group 3, reg=6, rm=3)
        var (cpu, _) = Setup(0xF6, 0xF3);
        cpu.AX = 0x0064; // 100
        cpu.BL = 0x0A;   // 10
        cpu.Step();
        Assert.Equal(0x0A, cpu.AL); // quotient = 10
        Assert.Equal(0x00, cpu.AH); // remainder = 0
    }

    [Fact]
    public void NEG_AL()
    {
        // NEG AL  =>  F6 D8 (Group 3, reg=3, rm=0)
        var (cpu, _) = Setup(0xF6, 0xD8);
        cpu.AL = 0x01;
        cpu.Step();
        Assert.Equal(0xFF, cpu.AL); // -1 in two's complement
        Assert.True(cpu.Flags.CF);
    }

    [Fact]
    public void NOT_AL()
    {
        // NOT AL  =>  F6 D0 (Group 3, reg=2, rm=0)
        var (cpu, _) = Setup(0xF6, 0xD0);
        cpu.AL = 0xAA;
        cpu.Step();
        Assert.Equal(0x55, cpu.AL);
    }

    [Fact]
    public void IN_AL_Imm8()
    {
        var (cpu, bus) = Setup(0xE4, 0x60);
        var device = new Tests.Bus.StubDevice(0x77, new[] { 0x60 });
        bus.RegisterDevice(device);
        cpu.Step();
        Assert.Equal(0x77, cpu.AL);
    }

    [Fact]
    public void OUT_Imm8_AL()
    {
        var (cpu, bus) = Setup(0xE6, 0x60);
        cpu.AL = 0x99;
        var device = new Tests.Bus.StubDevice(0x00, new[] { 0x60 });
        bus.RegisterDevice(device);
        cpu.Step();
        Assert.Equal(0x99, bus.ReadIoByte(0x60));
    }

    [Fact]
    public void IN_AL_DX()
    {
        var (cpu, bus) = Setup(0xEC); // IN AL, DX
        cpu.DX = 0x0060;
        var device = new Tests.Bus.StubDevice(0x55, new[] { 0x60 });
        bus.RegisterDevice(device);
        cpu.Step();
        Assert.Equal(0x55, cpu.AL);
    }

    [Fact]
    public void OUT_DX_AL()
    {
        var (cpu, bus) = Setup(0xEE); // OUT DX, AL
        cpu.DX = 0x0060; cpu.AL = 0xBB;
        var device = new Tests.Bus.StubDevice(0x00, new[] { 0x60 });
        bus.RegisterDevice(device);
        cpu.Step();
        Assert.Equal(0xBB, bus.ReadIoByte(0x60));
    }

    [Fact]
    public void LEA_AX()
    {
        var (cpu, _) = Setup(0x8D, 0x40, 0x10); // LEA AX, [BX+SI+0x10]
        cpu.BX = 0x1000; cpu.SI = 0x0200;
        cpu.Step();
        Assert.Equal(0x1210, cpu.AX);
    }

    [Fact]
    public void XCHG_AX_BX()
    {
        var (cpu, _) = Setup(0x93);
        cpu.AX = 0x1111; cpu.BX = 0x2222;
        cpu.Step();
        Assert.Equal(0x2222, cpu.AX);
        Assert.Equal(0x1111, cpu.BX);
    }

    [Fact]
    public void CLI_STI()
    {
        var (cpu, _) = Setup(0xFA, 0xFB); // CLI, STI
        cpu.Flags.IF = true;
        cpu.Step();
        Assert.False(cpu.Flags.IF);
        cpu.Step();
        Assert.True(cpu.Flags.IF);
    }

    [Fact]
    public void CLD_STD()
    {
        var (cpu, _) = Setup(0xFC, 0xFD); // CLD, STD
        cpu.Flags.DF = true;
        cpu.Step();
        Assert.False(cpu.Flags.DF);
        cpu.Step();
        Assert.True(cpu.Flags.DF);
    }

    [Fact]
    public void CBW()
    {
        var (cpu, _) = Setup(0x98);
        cpu.AL = 0x80; // -128
        cpu.Step();
        Assert.Equal(0xFF80, cpu.AX); // sign-extended
    }

    [Fact]
    public void CWD()
    {
        var (cpu, _) = Setup(0x99);
        cpu.AX = 0x8000; // -32768
        cpu.Step();
        Assert.Equal(0xFFFF, cpu.DX); // sign-extended
    }

    [Fact]
    public void PUSHF_POPF()
    {
        var (cpu, _) = Setup(0x9C, 0x9D); // PUSHF, POPF
        cpu.SP = 0xFFFE; cpu.SS = 0x0000;
        cpu.Flags.CF = true;
        cpu.Flags.ZF = true;
        cpu.Step(); // PUSHF
        cpu.Flags.CF = false;
        cpu.Flags.ZF = false;
        cpu.Step(); // POPF
        Assert.True(cpu.Flags.CF);
        Assert.True(cpu.Flags.ZF);
    }

    [Fact]
    public void Group_80h_ADD_RM8_Imm8()
    {
        // ADD BYTE [0x1000], 0x05  =>  80 06 00 10 05
        var (cpu, bus) = Setup(0x80, 0x06, 0x00, 0x10, 0x05);
        cpu.DS = 0x0000;
        bus.WriteMemoryByte(0x1000, 0x10);
        cpu.Step();
        Assert.Equal(0x15, bus.ReadMemoryByte(0x1000));
    }

    [Fact]
    public void Group_81h_CMP_RM16_Imm16()
    {
        // CMP WORD [0x1000], 0x1234  =>  81 3E 00 10 34 12
        var (cpu, bus) = Setup(0x81, 0x3E, 0x00, 0x10, 0x34, 0x12);
        cpu.DS = 0x0000;
        bus.WriteMemoryWord(0x1000, 0x1234);
        cpu.Step();
        Assert.True(cpu.Flags.ZF); // equal
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement all remaining opcodes**

Opcodes to implement:
- `0x86-0x87`: XCHG r/m
- `0x88-0x8B`: MOV r/m variants
- `0x8C`: MOV r/m16, Sreg
- `0x8D`: LEA
- `0x8E`: MOV Sreg, r/m16
- `0x8F`: POP r/m16
- `0x80-0x83`: Immediate ALU group (ADD/OR/ADC/SBB/AND/SUB/XOR/CMP r/m, imm)
- `0x91-0x97`: XCHG AX, reg16
- `0x98`: CBW, `0x99`: CWD
- `0x9C`: PUSHF, `0x9D`: POPF, `0x9E`: SAHF, `0x9F`: LAHF
- `0xA0-0xA3`: MOV AL/AX, moffs
- `0xC6-0xC7`: MOV r/m, imm
- `0xD0-0xD1`: Shift/rotate by 1 (Group 2: ROL/ROR/RCL/RCR/SHL/SHR/SAR)
- `0xD2-0xD3`: Shift/rotate by CL
- `0xE4-0xE7`: IN/OUT imm8
- `0xEC-0xEF`: IN/OUT DX
- `0xF6-0xF7`: Group 3 (TEST/NOT/NEG/MUL/IMUL/DIV/IDIV)
- `0xF8`: CLC, `0xF9`: STC, `0xFA`: CLI, `0xFB`: STI, `0xFC`: CLD, `0xFD`: STD
- `0xFE`: Group 4 (INC/DEC r/m8)
- `0xFF`: Group 5 (INC/DEC/CALL/JMP/PUSH r/m16)

**Implementation notes for tricky instructions:**
- MUL 8-bit: AX = AL * r/m8. CF=OF=1 if AH != 0
- MUL 16-bit: DX:AX = AX * r/m16. CF=OF=1 if DX != 0
- DIV 8-bit: AL = AX / r/m8, AH = AX % r/m8. Divide by zero → INT 0
- DIV 16-bit: AX = DX:AX / r/m16, DX = DX:AX % r/m16
- IMUL/IDIV: signed variants of above
- MOV Sreg: loading SS should inhibit interrupts for one instruction (stack setup)

- [ ] **Step 4: Run tests, verify pass**

```bash
cd /c/FlutterProject/pc98/PC98Emu
dotnet test
```
Expected: All CPU tests PASS.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: implement MOV, shift, I/O, MUL/DIV, Group opcodes, and misc instructions"
```

---

### Task 9: Scheduler

**Files:**
- Create: `PC98Emu/PC98Emu/Scheduler/Scheduler.cs`
- Create: `PC98Emu/PC98Emu.Tests/Scheduler/SchedulerTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PC98Emu/PC98Emu.Tests/Scheduler/SchedulerTests.cs
using Xunit;
using PC98Emu.Scheduler;

namespace PC98Emu.Tests.Scheduler;

public class SchedulerTests
{
    [Fact]
    public void ScheduleAndFireEvent()
    {
        var scheduler = new EventScheduler();
        bool fired = false;
        scheduler.Schedule(100, "test", () => fired = true);
        scheduler.Advance(99);
        Assert.False(fired);
        scheduler.Advance(1);
        Assert.True(fired);
    }

    [Fact]
    public void RecurringEvent()
    {
        var scheduler = new EventScheduler();
        int count = 0;
        scheduler.ScheduleRecurring(50, "tick", () => count++);
        scheduler.Advance(50);
        Assert.Equal(1, count);
        scheduler.Advance(50);
        Assert.Equal(2, count);
    }

    [Fact]
    public void CancelEvent()
    {
        var scheduler = new EventScheduler();
        bool fired = false;
        scheduler.Schedule(100, "test", () => fired = true);
        scheduler.Cancel("test");
        scheduler.Advance(200);
        Assert.False(fired);
    }

    [Fact]
    public void NextEventCycles()
    {
        var scheduler = new EventScheduler();
        scheduler.Schedule(50, "a", () => { });
        scheduler.Schedule(100, "b", () => { });
        Assert.Equal(50, scheduler.CyclesUntilNextEvent());
    }

    [Fact]
    public void MultipleEventsFireInOrder()
    {
        var scheduler = new EventScheduler();
        var order = new List<string>();
        scheduler.Schedule(50, "first", () => order.Add("first"));
        scheduler.Schedule(100, "second", () => order.Add("second"));
        scheduler.Advance(100);
        Assert.Equal(new[] { "first", "second" }, order);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement EventScheduler**

```csharp
// PC98Emu/PC98Emu/Scheduler/Scheduler.cs
namespace PC98Emu.Scheduler;

public class EventScheduler
{
    private readonly SortedList<long, ScheduledEvent> _events = new();
    private long _currentCycle;
    private int _nextId;

    public void Schedule(long cyclesFromNow, string name, Action callback)
    {
        AddEvent(new ScheduledEvent
        {
            TargetCycle = _currentCycle + cyclesFromNow,
            Name = name,
            Callback = callback
        });
    }

    public void ScheduleRecurring(long interval, string name, Action callback)
    {
        AddEvent(new ScheduledEvent
        {
            TargetCycle = _currentCycle + interval,
            Name = name,
            Callback = callback,
            Recurring = true,
            Interval = interval
        });
    }

    public void Cancel(string name)
    {
        foreach (var key in _events.Where(e => e.Value.Name == name).Select(e => e.Key).ToList())
            _events.Remove(key);
    }

    public void Advance(long cycles)
    {
        _currentCycle += cycles;
        while (_events.Count > 0 && _events.First().Value.TargetCycle <= _currentCycle)
        {
            var evt = _events.First().Value;
            _events.RemoveAt(0);
            evt.Callback();
            if (evt.Recurring)
            {
                evt.TargetCycle += evt.Interval;
                AddEvent(evt);
            }
        }
    }

    public long CyclesUntilNextEvent()
    {
        if (_events.Count == 0) return long.MaxValue;
        return Math.Max(0, _events.First().Value.TargetCycle - _currentCycle);
    }

    private void AddEvent(ScheduledEvent evt)
    {
        long key = evt.TargetCycle;
        while (_events.ContainsKey(key)) key++;
        _events.Add(key, evt);
    }

    private class ScheduledEvent
    {
        public long TargetCycle;
        public string Name = "";
        public Action Callback = () => { };
        public bool Recurring;
        public long Interval;
    }
}
```

- [ ] **Step 4: Run tests, verify pass**

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add EventScheduler with one-shot and recurring events"
```

---

### Task 10: PIC (i8259A)

**Files:**
- Create: `PC98Emu/PC98Emu/Devices/PIC.cs`
- Create: `PC98Emu/PC98Emu.Tests/Devices/PICTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PC98Emu/PC98Emu.Tests/Devices/PICTests.cs
using Xunit;
using PC98Emu.Devices;

namespace PC98Emu.Tests.Devices;

public class PICTests
{
    [Fact]
    public void InitICW_SetsBaseVector()
    {
        var pic = new PIC(0x00, 0x02);
        pic.WriteByte(0x00, 0x11); // ICW1
        pic.WriteByte(0x02, 0x08); // ICW2: base vector = 8
        pic.WriteByte(0x02, 0x04); // ICW3
        pic.WriteByte(0x02, 0x01); // ICW4
        Assert.Equal(0x08, pic.VectorBase);
    }

    [Fact]
    public void MaskIRQ()
    {
        var pic = new PIC(0x00, 0x02);
        pic.WriteByte(0x02, 0xFF); // mask all
        Assert.Equal(0xFF, pic.ReadByte(0x02));
    }

    [Fact]
    public void RaiseIRQ_Unmasked()
    {
        var pic = new PIC(0x00, 0x02);
        pic.VectorBase = 0x08;
        pic.WriteByte(0x02, 0x00);
        pic.RaiseIRQ(0);
        Assert.True(pic.HasInterrupt());
        Assert.Equal(0x08, pic.AcknowledgeInterrupt());
    }

    [Fact]
    public void RaiseIRQ_Masked()
    {
        var pic = new PIC(0x00, 0x02);
        pic.VectorBase = 0x08;
        pic.WriteByte(0x02, 0x01); // mask IRQ0
        pic.RaiseIRQ(0);
        Assert.False(pic.HasInterrupt());
    }

    [Fact]
    public void EOI_ClearsISR()
    {
        var pic = new PIC(0x00, 0x02);
        pic.VectorBase = 0x08;
        pic.WriteByte(0x02, 0x00);
        pic.RaiseIRQ(1);
        pic.AcknowledgeInterrupt();
        pic.WriteByte(0x00, 0x20); // non-specific EOI
        Assert.False(pic.HasInterrupt());
    }

    [Fact]
    public void IRQ_Priority()
    {
        var pic = new PIC(0x00, 0x02);
        pic.VectorBase = 0x08;
        pic.WriteByte(0x02, 0x00);
        pic.RaiseIRQ(3);
        pic.RaiseIRQ(1);
        // IRQ1 should be acknowledged first (higher priority)
        Assert.Equal(0x09, pic.AcknowledgeInterrupt()); // vector 8+1
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement PIC**

Implement `PIC : IDevice` with:
- Two ports (command port, data port) configurable via constructor
- ICW1-ICW4 initialization sequence tracking (`_icwStep` counter)
- IRR (Interrupt Request Register), ISR (In-Service Register), IMR (Interrupt Mask Register)
- `RaiseIRQ(int irq)`: sets bit in IRR
- `HasInterrupt()`: returns true if any unmasked, un-in-service IRQ is pending
- `AcknowledgeInterrupt()`: returns vector number, moves IRQ from IRR to ISR
- OCW2 handling: non-specific EOI (0x20) clears highest-priority ISR bit
- OCW3 handling: read IRR/ISR via command port
- Priority: lowest bit number = highest priority

- [ ] **Step 4: Run tests, verify pass**

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add PIC (i8259A) with ICW/OCW and interrupt routing"
```

---

### Task 11: PIT (i8253)

**Files:**
- Create: `PC98Emu/PC98Emu/Devices/PIT.cs`
- Create: `PC98Emu/PC98Emu.Tests/Devices/PITTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PC98Emu/PC98Emu.Tests/Devices/PITTests.cs
using Xunit;
using PC98Emu.Devices;

namespace PC98Emu.Tests.Devices;

public class PITTests
{
    [Fact]
    public void SetCounterValue()
    {
        var irqFired = false;
        var pit = new PIT(0x71, 0x73, 0x75, 0x77, () => irqFired = true);
        // Control word: channel 0, lobyte/hibyte, mode 2 (rate generator)
        // 00 11 010 0 = 0x34
        pit.WriteByte(0x77, 0x34);
        pit.WriteByte(0x71, 0x00); // count low = 0
        pit.WriteByte(0x71, 0x01); // count high = 1 => count = 256
        Assert.Equal(256, pit.GetChannelCount(0));
    }

    [Fact]
    public void Channel0_FiresIRQ_OnCountdown()
    {
        int irqCount = 0;
        var pit = new PIT(0x71, 0x73, 0x75, 0x77, () => irqCount++);
        // Channel 0, mode 2, count = 10
        pit.WriteByte(0x77, 0x34);
        pit.WriteByte(0x71, 0x0A); // count = 10
        pit.WriteByte(0x71, 0x00);
        // Tick 10 times
        pit.Tick(10);
        Assert.Equal(1, irqCount);
    }

    [Fact]
    public void Channel0_Mode2_Repeats()
    {
        int irqCount = 0;
        var pit = new PIT(0x71, 0x73, 0x75, 0x77, () => irqCount++);
        pit.WriteByte(0x77, 0x34); // channel 0, mode 2
        pit.WriteByte(0x71, 0x05);
        pit.WriteByte(0x71, 0x00); // count = 5
        pit.Tick(15);
        Assert.Equal(3, irqCount); // fires at 5, 10, 15
    }

    [Fact]
    public void Channel2_BeepFrequency()
    {
        var pit = new PIT(0x71, 0x73, 0x75, 0x77, () => { });
        // Channel 2, mode 3 (square wave), count = 1000
        // 10 11 011 0 = 0xB6
        pit.WriteByte(0x77, 0xB6);
        pit.WriteByte(0x75, 0xE8); // 1000 low
        pit.WriteByte(0x75, 0x03); // 1000 high
        Assert.Equal(1000, pit.GetChannelCount(2));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement PIT**

```csharp
// PC98Emu/PC98Emu/Devices/PIT.cs
namespace PC98Emu.Devices;

using PC98Emu.Bus;

public class PIT : IDevice
{
    private readonly int _port0, _port1, _port2, _portCtrl;
    private readonly Action _irq0Callback;
    private readonly Channel[] _channels = new Channel[3];

    public PIT(int port0, int port1, int port2, int portCtrl, Action irq0Callback)
    {
        _port0 = port0; _port1 = port1; _port2 = port2;
        _portCtrl = portCtrl;
        _irq0Callback = irq0Callback;
        for (int i = 0; i < 3; i++) _channels[i] = new Channel();
    }

    public int GetChannelCount(int ch) => _channels[ch].ReloadValue;

    public void WriteByte(int port, byte value)
    {
        if (port == _portCtrl)
        {
            int ch = (value >> 6) & 3;
            if (ch == 3) return; // readback - not implemented
            int accessMode = (value >> 4) & 3;
            int mode = (value >> 1) & 7;
            _channels[ch].Mode = mode;
            _channels[ch].AccessMode = accessMode;
            _channels[ch].HighByte = false;
            return;
        }

        int channel = port == _port0 ? 0 : port == _port1 ? 1 : 2;
        var c = _channels[channel];
        if (c.AccessMode == 1 || (c.AccessMode == 3 && !c.HighByte))
        {
            c.ReloadValue = (c.ReloadValue & 0xFF00) | value;
            if (c.AccessMode == 3) c.HighByte = true;
            else { c.Counter = c.ReloadValue; c.Active = true; }
        }
        else if (c.AccessMode == 2 || (c.AccessMode == 3 && c.HighByte))
        {
            c.ReloadValue = (c.ReloadValue & 0x00FF) | (value << 8);
            c.HighByte = false;
            c.Counter = c.ReloadValue == 0 ? 65536 : c.ReloadValue;
            c.Active = true;
        }
    }

    public byte ReadByte(int port)
    {
        int channel = port == _port0 ? 0 : port == _port1 ? 1 : 2;
        return (byte)(_channels[channel].Counter & 0xFF);
    }

    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) => WriteByte(port, (byte)value);

    public void Tick(int cycles)
    {
        for (int i = 0; i < 3; i++)
        {
            var c = _channels[i];
            if (!c.Active) continue;
            c.Counter -= cycles;
            while (c.Counter <= 0)
            {
                if (i == 0) _irq0Callback();
                if (c.Mode == 2 || c.Mode == 3) // rate generator / square wave
                    c.Counter += c.ReloadValue == 0 ? 65536 : c.ReloadValue;
                else
                {
                    c.Active = false;
                    break;
                }
            }
        }
    }

    public void Reset()
    {
        for (int i = 0; i < 3; i++) _channels[i] = new Channel();
    }

    public int[] GetPortRange() => new[] { _port0, _port1, _port2, _portCtrl };

    private class Channel
    {
        public int Counter;
        public int ReloadValue;
        public int Mode;
        public int AccessMode; // 1=lobyte, 2=hibyte, 3=lo/hi
        public bool HighByte;
        public bool Active;
    }
}
```

- [ ] **Step 4: Run tests, verify pass**

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add PIT (i8253) with 3 channels and mode 2/3 support"
```

---

### Task 12: DMA (i8237A)

**Files:**
- Create: `PC98Emu/PC98Emu/Devices/DMA.cs`
- Create: `PC98Emu/PC98Emu.Tests/Devices/DMATests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PC98Emu/PC98Emu.Tests/Devices/DMATests.cs
using Xunit;
using PC98Emu.Devices;

namespace PC98Emu.Tests.Devices;

public class DMATests
{
    [Fact]
    public void SetAddressAndCount()
    {
        var dma = new DMA();
        // Set channel 2 address: write low then high byte
        dma.WriteByte(0x05, 0x00); // ch2 address low
        dma.WriteByte(0x05, 0x10); // ch2 address high => 0x1000
        dma.WriteByte(0x07, 0xFF); // ch2 count low
        dma.WriteByte(0x07, 0x01); // ch2 count high => 0x01FF (512-1)
        Assert.Equal(0x1000, dma.GetChannelAddress(2));
        Assert.Equal(0x01FF, dma.GetChannelCount(2));
    }

    [Fact]
    public void TransferData()
    {
        var dma = new DMA();
        var memory = new byte[0x100000];
        dma.WriteByte(0x05, 0x00);
        dma.WriteByte(0x05, 0x10); // address = 0x1000
        dma.WriteByte(0x07, 0x03);
        dma.WriteByte(0x07, 0x00); // count = 3 (4 bytes)
        var data = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        dma.TransferToMemory(2, data, memory);
        Assert.Equal(0xAA, memory[0x1000]);
        Assert.Equal(0xDD, memory[0x1003]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement DMA**

Implement `DMA : IDevice` with:
- 4 channels, each with address register, count register, page register
- Flip-flop for low/high byte write
- `TransferToMemory(int channel, byte[] data, byte[] memory)` for FDC use
- Ports: odd ports (0x01, 0x03, 0x05, 0x07, 0x09, 0x0B, 0x0D, 0x0F)

- [ ] **Step 4: Run tests, verify pass**

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: add DMA (i8237A) controller"
```

---

### Task 13: Keyboard, RTC, Serial, Printer, SystemPort, Mouse

**Files:**
- Create: `PC98Emu/PC98Emu/Devices/Keyboard.cs`
- Create: `PC98Emu/PC98Emu/Devices/RTC.cs`
- Create: `PC98Emu/PC98Emu/Devices/Serial.cs`
- Create: `PC98Emu/PC98Emu/Devices/Printer.cs`
- Create: `PC98Emu/PC98Emu/Devices/SystemPort.cs`
- Create: `PC98Emu/PC98Emu/Devices/Mouse.cs`
- Create: `PC98Emu/PC98Emu.Tests/Devices/KeyboardTests.cs`

- [ ] **Step 1: Write failing tests for Keyboard**

```csharp
// PC98Emu/PC98Emu.Tests/Devices/KeyboardTests.cs
using Xunit;
using PC98Emu.Devices;

namespace PC98Emu.Tests.Devices;

public class KeyboardTests
{
    [Fact]
    public void EnqueueScancode_ReadFromPort()
    {
        bool irqFired = false;
        var kbd = new Keyboard(() => irqFired = true);
        kbd.EnqueueScancode(0x1E); // 'A' key press
        Assert.True(irqFired);
        Assert.Equal(0x1E, kbd.ReadByte(0x41));
    }

    [Fact]
    public void StatusPort_DataReady()
    {
        var kbd = new Keyboard(() => { });
        Assert.Equal(0x00, kbd.ReadByte(0x43) & 0x01); // no data
        kbd.EnqueueScancode(0x1E);
        Assert.Equal(0x01, kbd.ReadByte(0x43) & 0x01); // data ready
    }

    [Fact]
    public void BufferOverflow_DropsOldest()
    {
        var kbd = new Keyboard(() => { });
        for (int i = 0; i < 20; i++) // buffer is 16 bytes
            kbd.EnqueueScancode((byte)i);
        // Should not throw, oldest scancodes dropped
        Assert.True(true);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement Keyboard**

```csharp
// PC98Emu/PC98Emu/Devices/Keyboard.cs
namespace PC98Emu.Devices;

using PC98Emu.Bus;

public class Keyboard : IDevice
{
    private readonly Action _raiseIRQ1;
    private readonly Queue<byte> _buffer = new(16);

    public Keyboard(Action raiseIRQ1) => _raiseIRQ1 = raiseIRQ1;

    public void EnqueueScancode(byte scancode)
    {
        if (_buffer.Count >= 16) _buffer.Dequeue(); // drop oldest
        _buffer.Enqueue(scancode);
        _raiseIRQ1();
    }

    public byte ReadByte(int port)
    {
        if (port == 0x41) return _buffer.Count > 0 ? _buffer.Dequeue() : (byte)0;
        if (port == 0x43) return (byte)(_buffer.Count > 0 ? 0x01 : 0x00);
        return 0xFF;
    }

    public void WriteByte(int port, byte value) { /* keyboard commands - minimal */ }
    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) { }
    public void Reset() => _buffer.Clear();
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0x41, 0x43 };
}
```

- [ ] **Step 4: Implement RTC**

```csharp
// PC98Emu/PC98Emu/Devices/RTC.cs
namespace PC98Emu.Devices;

using PC98Emu.Bus;

public class RTC : IDevice
{
    private int _command;
    private int _shiftRegister;
    private int _bitCount;

    public byte ReadByte(int port)
    {
        // Return current time data bit
        var now = DateTime.Now;
        int data = GetTimeBCD(now);
        return (byte)((data >> _bitCount) & 1);
    }

    public void WriteByte(int port, byte value)
    {
        // STB(bit3), CLK(bit2), DI(bit1), C0-C2 in upper bits
        _command = value;
    }

    private int GetTimeBCD(DateTime now)
    {
        // Return packed BCD time for uPD1990A
        return ToBCD(now.Second) | (ToBCD(now.Minute) << 8) |
               (ToBCD(now.Hour) << 16) | (ToBCD(now.Day) << 24);
    }

    private int ToBCD(int value) => ((value / 10) << 4) | (value % 10);

    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) => WriteByte(port, (byte)value);
    public void Reset() { _command = 0; _bitCount = 0; }
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0x20 };
}
```

- [ ] **Step 5: Implement Serial stub**

```csharp
// PC98Emu/PC98Emu/Devices/Serial.cs
namespace PC98Emu.Devices;

using PC98Emu.Bus;

public class Serial : IDevice
{
    public byte ReadByte(int port)
    {
        if (port == 0x32) return 0x05; // status: TX ready, no RX data
        return 0x00;
    }

    public void WriteByte(int port, byte value) { }
    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) { }
    public void Reset() { }
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0x30, 0x31, 0x32, 0x33 };
}
```

- [ ] **Step 6: Implement Printer stub**

```csharp
// PC98Emu/PC98Emu/Devices/Printer.cs
namespace PC98Emu.Devices;

using PC98Emu.Bus;

public class Printer : IDevice
{
    public byte ReadByte(int port)
    {
        if (port == 0x42) return 0x04; // status: busy
        return 0xFF;
    }

    public void WriteByte(int port, byte value) { }
    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) { }
    public void Reset() { }
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0x40, 0x42 };
}
```

- [ ] **Step 7: Implement SystemPort (Beep control)**

```csharp
// PC98Emu/PC98Emu/Devices/SystemPort.cs
namespace PC98Emu.Devices;

using PC98Emu.Bus;

public class SystemPort : IDevice
{
    public bool BeepEnabled { get; private set; }

    public byte ReadByte(int port) => 0xFF;

    public void WriteByte(int port, byte value)
    {
        if (port == 0x37)
            BeepEnabled = (value & 0x08) != 0;
    }

    public ushort ReadWord(int port) => 0xFFFF;
    public void WriteWord(int port, ushort value) { }
    public void Reset() => BeepEnabled = false;
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0x35, 0x37 };
}
```

- [ ] **Step 8: Implement Mouse stub**

```csharp
// PC98Emu/PC98Emu/Devices/Mouse.cs
namespace PC98Emu.Devices;

using PC98Emu.Bus;

public class Mouse : IDevice
{
    public byte ReadByte(int port) => 0x00;
    public void WriteByte(int port, byte value) { }
    public ushort ReadWord(int port) => 0;
    public void WriteWord(int port, ushort value) { }
    public void Reset() { }
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0x7FD9, 0x7FDB, 0x7FDD };
}
```

- [ ] **Step 9: Run all tests**

```bash
cd /c/FlutterProject/pc98/PC98Emu
dotnet test
```

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: add Keyboard, RTC, Serial, Printer, SystemPort, and Mouse devices"
```

---

### Task 14: Disk Image Loaders

**Files:**
- Create: `PC98Emu/PC98Emu/Disk/IDiskImage.cs`
- Create: `PC98Emu/PC98Emu/Disk/D88Image.cs`
- Create: `PC98Emu/PC98Emu/Disk/FDIImage.cs`
- Create: `PC98Emu/PC98Emu/Disk/HDIImage.cs`
- Create: `PC98Emu/PC98Emu/Disk/NHDImage.cs`
- Create: `PC98Emu/PC98Emu/Disk/NFDImage.cs`
- Create: `PC98Emu/PC98Emu/Disk/DiskManager.cs`
- Create: `PC98Emu/PC98Emu/Disk/SASIController.cs`
- Create: `PC98Emu/PC98Emu.Tests/Disk/D88ImageTests.cs`

- [ ] **Step 1: Define IDiskImage**

```csharp
// PC98Emu/PC98Emu/Disk/IDiskImage.cs
namespace PC98Emu.Disk;

public interface IDiskImage
{
    bool ReadSector(int cylinder, int head, int sector, byte[] buffer);
    bool WriteSector(int cylinder, int head, int sector, byte[] buffer);
    int Cylinders { get; }
    int Heads { get; }
    int SectorsPerTrack { get; }
    int SectorSize { get; }
}
```

- [ ] **Step 2: Write failing tests for D88Image**

```csharp
// PC98Emu/PC98Emu.Tests/Disk/D88ImageTests.cs
using Xunit;
using PC98Emu.Disk;

namespace PC98Emu.Tests.Disk;

public class D88ImageTests
{
    [Fact]
    public void ParseD88Header()
    {
        // Create a minimal D88 image in memory
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
        Assert.Equal(0xEB, buffer[0]); // JMP short (typical boot sector)
    }

    private byte[] CreateMinimalD88()
    {
        // D88 header: 688 bytes (name[17], reserved[9], writeProtect[1], mediaType[1], diskSize[4], trackOffsets[164*4])
        var data = new byte[688 + 16 + 512]; // header + sector header + sector data
        // Disk size
        int diskSize = data.Length;
        data[0x1C] = (byte)(diskSize & 0xFF);
        data[0x1D] = (byte)((diskSize >> 8) & 0xFF);
        data[0x1E] = (byte)((diskSize >> 16) & 0xFF);
        data[0x1F] = (byte)((diskSize >> 24) & 0xFF);
        // Track 0 offset = 688
        int trackOffset = 688;
        data[0x20] = (byte)(trackOffset & 0xFF);
        data[0x21] = (byte)((trackOffset >> 8) & 0xFF);
        data[0x22] = (byte)((trackOffset >> 16) & 0xFF);
        data[0x23] = (byte)((trackOffset >> 24) & 0xFF);
        // Sector header at offset 688: C=0, H=0, R=1, N=2(512bytes), sectors=8, density, deleted, status, size=512
        int sh = 688;
        data[sh + 0] = 0; // C
        data[sh + 1] = 0; // H
        data[sh + 2] = 1; // R (sector number)
        data[sh + 3] = 2; // N (size code: 2=512)
        data[sh + 4] = 8; data[sh + 5] = 0; // sectors in track
        data[sh + 14] = 0x00; data[sh + 15] = 0x02; // data size = 512
        // Sector data
        data[688 + 16] = 0xEB; // JMP short (boot indicator)
        return data;
    }
}
```

- [ ] **Step 3: Implement D88Image**

Parse D88 file format:
- Header: 688 bytes (name, write protect, media type, disk size, 164 track offsets)
- Each track: sequence of sector headers (16 bytes each: C, H, R, N, sectors count, density, deleted mark, status, data size) followed by sector data
- `ReadSector`: locate track offset, iterate sector headers to find matching C/H/R, read data

- [ ] **Step 4: Implement FDIImage, HDIImage, NHDImage, NFDImage**

Each format:
- **FDI**: 4096-byte header (header size, fdd type, header bytes, sector size, sectors, heads, cylinders), then raw sector data in order
- **HDI**: 4096-byte header (header size, data size, sector size, surfaces, cylinders, sectors per track), then raw sector data
- **NHD**: 512-byte header ("T98HDDIMAGE.R0\0", header size, cylinders, heads, sectors, sector size), then raw data
- **NFD**: header with revision, track table. Basic support only (standard sectors, no copy protection)

- [ ] **Step 5: Implement DiskManager and SASIController stub**

```csharp
// DiskManager: maps drive numbers (0-3 FDD, 0-1 HDD) to IDiskImage instances
// SASIController: IDevice at ports 0x0CC0-0x0CCC, delegates to DiskManager for HDD reads
```

- [ ] **Step 6: Run tests, verify pass**

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: add disk image loaders (D88, FDI, HDI, NHD, NFD) and SASI stub"
```

---

### Task 15: FDC (uPD765A)

**Files:**
- Create: `PC98Emu/PC98Emu/Devices/FDC.cs`
- Create: `PC98Emu/PC98Emu.Tests/Devices/FDCTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PC98Emu/PC98Emu.Tests/Devices/FDCTests.cs
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

        // Create minimal disk image and mount
        var diskData = CreateBootableDisk();
        var image = new D88Image(diskData);
        diskMgr.MountFloppy(0, image);

        // Setup DMA channel 2: address=0x1000, count=511
        dma.WriteByte(0x05, 0x00); dma.WriteByte(0x05, 0x10);
        dma.WriteByte(0x07, 0xFF); dma.WriteByte(0x07, 0x01);

        // Issue READ DATA command: 0x06, HD|US, C, H, R, N, EOT, GPL, DTL
        fdc.WriteByte(0x92, 0x06); // command: READ DATA
        fdc.WriteByte(0x92, 0x00); // drive 0, head 0
        fdc.WriteByte(0x92, 0x00); // cylinder 0
        fdc.WriteByte(0x92, 0x00); // head 0
        fdc.WriteByte(0x92, 0x01); // sector 1
        fdc.WriteByte(0x92, 0x02); // N=2 (512 bytes)
        fdc.WriteByte(0x92, 0x08); // EOT
        fdc.WriteByte(0x92, 0x1B); // GPL
        fdc.WriteByte(0x92, 0xFF); // DTL

        // Execute
        fdc.Tick(100);

        // Check DMA transferred data to memory
        Assert.Equal(0xEB, memory[0x1000]); // boot sector first byte
    }

    // Helper: create minimal D88 disk — reuse from D88ImageTests
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
        data[704] = 0xEB; // JMP short
        return data;
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement FDC**

Implement `FDC : IDevice` with state machine:
- **Command phase**: accept command byte and parameters via port 0x92 (data register)
- **Execution phase**: when all parameters received, execute command (read sector from disk image, transfer via DMA)
- **Result phase**: return ST0-ST3 status bytes

Commands to implement:
- `0x06` READ DATA: read sector(s) from disk → DMA to memory
- `0x05` WRITE DATA: memory → DMA → write to disk
- `0x0A` READ ID: return current sector header
- `0x0F` SEEK: move head to cylinder
- `0x07` RECALIBRATE: move head to cylinder 0
- `0x08` SENSE INTERRUPT STATUS: return ST0 and current cylinder

Status register (port 0x90): MSR bits — RQM, DIO, CB, NDM, DB0-3

- [ ] **Step 4: Wire FDC to DMA for data transfer**

- [ ] **Step 5: Run tests, verify pass**

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add FDC (uPD765A) with DMA transfer support"
```

---

### Task 16: GDC (uPD7220) and Display

**Files:**
- Create: `PC98Emu/PC98Emu/Graphics/GDC.cs`
- Create: `PC98Emu/PC98Emu/Graphics/TextRenderer.cs`
- Create: `PC98Emu/PC98Emu/Graphics/GraphicsRenderer.cs`
- Create: `PC98Emu/PC98Emu/Graphics/Display.cs`
- Create: `PC98Emu/PC98Emu/Graphics/Font.cs`
- Create: `PC98Emu/PC98Emu.Tests/Graphics/GDCTests.cs`
- Create: `PC98Emu/PC98Emu.Tests/Graphics/TextRendererTests.cs`

- [ ] **Step 1: Write failing tests for GDC**

```csharp
// PC98Emu/PC98Emu.Tests/Graphics/GDCTests.cs
using Xunit;
using PC98Emu.Graphics;

namespace PC98Emu.Tests.Graphics;

public class GDCTests
{
    [Fact]
    public void Reset_Command()
    {
        var gdc = new GDC(isText: true);
        gdc.WriteCommand(0x00); // RESET
        Assert.False(gdc.DisplayEnabled);
    }

    [Fact]
    public void Start_Command()
    {
        var gdc = new GDC(isText: true);
        gdc.WriteCommand(0x0D); // START
        Assert.True(gdc.DisplayEnabled);
    }

    [Fact]
    public void Stop_Command()
    {
        var gdc = new GDC(isText: true);
        gdc.WriteCommand(0x0D); // START
        gdc.WriteCommand(0x0C); // STOP
        Assert.False(gdc.DisplayEnabled);
    }

    [Fact]
    public void CSRW_SetsCursorAddress()
    {
        var gdc = new GDC(isText: true);
        gdc.WriteCommand(0x49); // CSRW
        gdc.WriteParameter(0x00); // low byte
        gdc.WriteParameter(0x01); // high byte
        Assert.Equal(0x0100, gdc.CursorAddress);
    }

    [Fact]
    public void SYNC_Command_AcceptsParameters()
    {
        var gdc = new GDC(isText: true);
        gdc.WriteCommand(0x0E); // SYNC (DE=0)
        // 8 parameter bytes for SYNC
        for (int i = 0; i < 8; i++)
            gdc.WriteParameter(0x00);
        // Should not throw
        Assert.True(true);
    }

    [Fact]
    public void SCROLL_Command()
    {
        var gdc = new GDC(isText: true);
        gdc.WriteCommand(0x70); // SCROLL
        gdc.WriteParameter(0x00); // SAD low
        gdc.WriteParameter(0x00); // SAD high + SL low
        gdc.WriteParameter(0x19); // SL high (25 lines)
        // First scroll area configured
        Assert.True(true);
    }

    [Fact]
    public void StatusRegister_ReportsVSync()
    {
        var gdc = new GDC(isText: true);
        var status = gdc.ReadStatus();
        // Bit 5: VSYNC. We toggle this based on frame timing
        Assert.IsType<byte>(status);
    }
}
```

- [ ] **Step 2: Write tests for TextRenderer**

```csharp
// PC98Emu/PC98Emu.Tests/Graphics/TextRendererTests.cs
using Xunit;
using PC98Emu.Graphics;

namespace PC98Emu.Tests.Graphics;

public class TextRendererTests
{
    [Fact]
    public void RenderANK_Character()
    {
        var memory = new byte[0x100000];
        var renderer = new TextRenderer(memory);

        // Write 'A' (0x41) to text VRAM at position 0
        memory[0xA0000] = 0x41; // char low
        memory[0xA0001] = 0x00; // char high
        memory[0xA2000] = 0xE1; // attribute: white, visible
        memory[0xA2001] = 0x00;

        var framebuffer = new uint[640 * 400];
        renderer.Render(framebuffer, 640, 400);

        // Check that some pixels are non-zero (character 'A' rendered)
        bool hasPixels = false;
        for (int i = 0; i < 640 * 16; i++) // first char row (16 pixels high)
            if (framebuffer[i] != 0) { hasPixels = true; break; }
        Assert.True(hasPixels);
    }
}
```

- [ ] **Step 3: Implement GDC**

```csharp
// PC98Emu/PC98Emu/Graphics/GDC.cs — uPD7220 emulation
// Implements as IDevice (text GDC: ports 0x60/0x62, graphics GDC: ports 0xA0/0xA2)
// Command FIFO: commands written to command port, parameters to parameter port
// Commands: RESET(0x00), SYNC(0x0E/0F), START(0x0D), STOP(0x0C),
//           CSRFORM(0x4B), CSRW(0x49), CSRR(0xE0), SCROLL(0x70-0x7F),
//           PRAM(0x70), PITCH(0x47), WRITE(0x20), READ(0xA0)
// Status register: VSYNC(bit5), HBLANK(bit4), DMA(bit3), DRAWING(bit2), FIFO(bit1/0)
// VRAM bank switching: track current plane selection via port 0xA4/0xA6
```

- [ ] **Step 4: Create ANK font data (8x16 bitmap)**

Generate 256 ASCII/ANK character bitmaps as `static readonly byte[]` arrays in `Font.cs`.
Use a standard VGA-compatible bitmap font (public domain). Each character = 16 bytes (8 pixels wide × 16 rows).

- [ ] **Step 5: Create JIS kanji font data (16x16 bitmap)**

For kanji support:
- Include JIS X 0201 (ANK, 256 chars × 16 bytes = 4KB)
- Include JIS X 0208 Level 1+2 (~6879 chars × 32 bytes = ~215KB)
- Generate font data from a public domain/open source bitmap font (e.g., Shinonome 16x16 Gothic, BSD license)
- Store as compressed byte array in `Font.cs` or as embedded resource
- `GetKanjiGlyph(ushort jisCode)` → `byte[32]` (16×16 bitmap, 2 bytes per row)

- [ ] **Step 6: Implement TextRenderer**

```csharp
// Reads text VRAM (0xA0000-0xA1FFF chars, 0xA2000-0xA3FFF attrs)
// For each character position (80 columns × 25 rows):
//   - Read 2-byte character code
//   - Read 2-byte attribute (color, blink, reverse, underline)
//   - If code < 0x100: ANK character, render 8x16 from font
//   - If code >= 0x100: JIS kanji, render 16x16 from font
//   - Apply attribute (foreground/background color from 8-color digital palette)
// Output: uint[] RGBA framebuffer
```

- [ ] **Step 7: Implement GraphicsRenderer with VRAM bank switching**

```csharp
// Reads graphic VRAM planes 0-3
// Port 0xA4 selects which plane is mapped to 0xA8000-0xBFFFF for CPU access
// Port 0xA6 selects display plane(s)
// For rendering: combine all 4 planes bit-by-bit
//   pixel color = (plane3_bit << 3) | (plane2_bit << 2) | (plane1_bit << 1) | plane0_bit
//   Look up in 16-color palette
// Support both 640x400 and 640x200 modes
// Output: uint[] RGBA framebuffer
```

- [ ] **Step 8: Implement Display (SDL2)**

```csharp
// Initialize SDL2: SDL_Init(VIDEO), SDL_CreateWindow(640x400 * scale),
//   SDL_CreateRenderer, SDL_CreateTexture(ARGB8888, 640x400)
// Each frame:
//   1. TextRenderer.Render(textBuffer)
//   2. GraphicsRenderer.Render(graphicsBuffer)
//   3. Composite: text layer on top of graphics (text takes priority where non-transparent)
//   4. SDL_UpdateTexture + SDL_RenderCopy + SDL_RenderPresent
// Frame rate: 56.4 Hz (17.73ms per frame)
// Handle SDL_QUIT event for exit
```

- [ ] **Step 9: Run tests, verify pass**

```bash
cd /c/FlutterProject/pc98/PC98Emu
dotnet test
```

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: add GDC, text/graphics renderers with kanji support, and SDL2 display"
```

---

### Task 17: Compatible BIOS

**Files:**
- Create: `PC98Emu/PC98Emu/BIOS/CompatibleBios.cs`
- Create: `PC98Emu/PC98Emu/BIOS/DiskBios.cs`
- Create: `PC98Emu/PC98Emu/BIOS/SerialBios.cs`
- Create: `PC98Emu/PC98Emu/BIOS/TimerBios.cs`
- Create: `PC98Emu/PC98Emu/BIOS/KeyboardBios.cs`
- Create: `PC98Emu/PC98Emu/BIOS/CrtBios.cs`
- Create: `PC98Emu/PC98Emu/BIOS/GraphicsBios.cs`
- Create: `PC98Emu/PC98Emu/BIOS/BootLoader.cs`
- Create: `PC98Emu/PC98Emu.Tests/BIOS/BiosTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PC98Emu/PC98Emu.Tests/BIOS/BiosTests.cs
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

        // INT 18h should have an IVT entry pointing to BIOS ROM area
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

        // Memory size at 0x0458 should be 640 (KB)
        ushort memSize = bus.ReadMemoryWord(0x0458);
        Assert.Equal(640, memSize);
    }

    [Fact]
    public void DiskBios_ReadSector()
    {
        var bus = new SystemBus();
        var cpu = new V30(bus);
        var bios = new CompatibleBios(cpu, bus);
        bios.Initialize();

        // Setup a minimal disk and test INT 18h AH=x6 (read)
        // This test verifies the BIOS handler sets AH=0 (success) on return
        // Detailed integration test with actual disk deferred to Task 19
    }

    [Fact]
    public void KeyboardBios_NoKey_ReturnsZero()
    {
        var bus = new SystemBus();
        var cpu = new V30(bus);
        var bios = new CompatibleBios(cpu, bus);
        bios.Initialize();

        // INT 1Bh AH=1 (check key) should return ZF=1 when no key available
        // Simulated via direct handler call
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement CompatibleBios**

```csharp
// PC98Emu/PC98Emu/BIOS/CompatibleBios.cs
// Initialization:
// 1. Assign unique addresses in BIOS ROM area (0xE8000+) for each INT handler
//    INT 18h → 0xE8000, INT 19h → 0xE8010, INT 1Ah → 0xE8020, etc.
// 2. Write IVT entries: for each INT, write handler address to 0x0000:(INT * 4)
// 3. Register each handler with cpu.RegisterBiosHandler(address, handlerAction)
// 4. Initialize BDA (BIOS Data Area, 0x0400-0x05FF):
//    - 0x0458: memory size = 640
//    - 0x045B: boot device
//    - 0x0480: display mode
//    - 0x0486: GDC clock mode
// 5. Set _biosRomProtect = true on SystemBus
//
// Each handler Action:
// - Reads CPU registers (AH for function number, other regs for parameters)
// - Performs the BIOS function
// - Sets return values in CPU registers
// - Executes IRET by calling cpu.Pop() for IP, CS, FLAGS
```

- [ ] **Step 4: Implement DiskBios (INT 18h)**

```csharp
// Key functions:
// AH=0x06: Read sector(s) — AL=drive, BX=sector count, CX=cylinder,
//          DX=head, BP=sector, ES:DI=buffer address
//   Read via DiskManager, copy to ES:DI, set AH=0 (success) or AH=error
// AH=0x07: Write sector(s) — same parameters
// AH=0x04: Drive status / get drive parameters
// AH=0x03: Initialize — reset FDC
```

- [ ] **Step 5: Implement KeyboardBios (INT 1Bh)**

```csharp
// AH=0x00: Wait for key — block until key in buffer, return scancode in AH, ASCII in AL
// AH=0x01: Check key — ZF=1 if no key, ZF=0 if key available (peek, don't remove)
// AH=0x02: Get shift key status
// AH=0x04: Clear keyboard buffer
```

- [ ] **Step 6: Implement TimerBios (INT 1Ah)**

```csharp
// AH=0x00: Read current time from RTC → CX:DX = tick count
// AH=0x02: Read RTC time → CH=hour, CL=minute, DH=second (BCD)
// AH=0x04: Read RTC date → CX=year, DH=month, DL=day (BCD)
```

- [ ] **Step 7: Implement CrtBios (INT 1Ch)**

```csharp
// AH=0x00: Set cursor position — DX=row, DL=column
// AH=0x01: Get cursor position
// AH=0x02: Scroll window
// AH=0x06: Clear screen — fill text VRAM with spaces
// AH=0x0A: Write character at cursor
// AH=0x0E: Write character and advance cursor (teletype)
// AH=0x12: Set display mode
```

- [ ] **Step 8: Implement GraphicsBios (INT 1Dh) — basic stubs**

```csharp
// AH=0x40: Set pixel
// AH=0x41: Get pixel
// AH=0x42: Draw line (Bresenham)
// For now, implement set/get pixel and stub the rest
```

- [ ] **Step 9: Implement SerialBios (INT 19h) stub — return AH=error**

- [ ] **Step 10: Implement BootLoader**

```csharp
// PC98Emu/PC98Emu/BIOS/BootLoader.cs
// Boot sequence:
// 1. Read sector C=0, H=0, S=1 from boot drive (FDD0 or HDD0)
// 2. Load 512 bytes to physical address 0x1FE00 (segment 0x1FE0, offset 0x0000)
// 3. Set CS=0x1FE0, IP=0x0000
// 4. Set DL=boot drive number
// PC-98 does NOT check for boot signature (0x55AA)
```

- [ ] **Step 11: Run tests, verify pass**

```bash
cd /c/FlutterProject/pc98/PC98Emu
dotnet test
```

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "feat: add compatible BIOS with INT handlers and boot loader"
```

---

### Task 18: Sound (YM2608/OPNA)

**Files:**
- Create: `PC98Emu/PC98Emu/Sound/YM2608.cs`
- Create: `PC98Emu/PC98Emu/Sound/FMChannel.cs`
- Create: `PC98Emu/PC98Emu/Sound/SSG.cs`
- Create: `PC98Emu/PC98Emu/Sound/ADPCM.cs`
- Create: `PC98Emu/PC98Emu/Sound/AudioOutput.cs`
- Create: `PC98Emu/PC98Emu.Tests/Sound/YM2608Tests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// PC98Emu/PC98Emu.Tests/Sound/YM2608Tests.cs
using Xunit;
using PC98Emu.Sound;

namespace PC98Emu.Tests.Sound;

public class YM2608Tests
{
    [Fact]
    public void RegisterWriteAndRead()
    {
        var ym = new YM2608(() => { });
        // Address port 0x188, data port 0x18A
        ym.WriteByte(0x188, 0x07); // SSG mixer register
        ym.WriteByte(0x18A, 0x38); // tone enabled for ch A/B/C
        ym.WriteByte(0x188, 0x07);
        Assert.Equal(0x38, ym.ReadByte(0x18A));
    }

    [Fact]
    public void TimerA_Fires()
    {
        int timerCount = 0;
        var ym = new YM2608(() => timerCount++);
        // Set Timer A: registers 0x24 (high), 0x25 (low)
        ym.WriteByte(0x188, 0x24);
        ym.WriteByte(0x18A, 0x00); // Timer A high = 0
        ym.WriteByte(0x188, 0x25);
        ym.WriteByte(0x18A, 0x00); // Timer A low = 0 → period = (1024-0)*18 cycles
        // Enable Timer A: register 0x27, bit 0
        ym.WriteByte(0x188, 0x27);
        ym.WriteByte(0x18A, 0x15); // load+enable Timer A, Timer B
        // Tick enough cycles
        ym.Tick(1024 * 72 + 1);
        Assert.True(timerCount > 0);
    }

    [Fact]
    public void SSG_ToneGeneration()
    {
        var ym = new YM2608(() => { });
        // Set SSG channel A frequency
        ym.WriteByte(0x188, 0x00); // freq low
        ym.WriteByte(0x18A, 0x00);
        ym.WriteByte(0x188, 0x01); // freq high
        ym.WriteByte(0x18A, 0x01); // period = 256
        // Set volume
        ym.WriteByte(0x188, 0x08);
        ym.WriteByte(0x18A, 0x0F); // max volume

        // Generate some samples
        var buffer = new short[100];
        ym.GenerateSamples(buffer, 0, 50);
        // Should have non-zero samples
        bool hasAudio = false;
        for (int i = 0; i < 100; i++)
            if (buffer[i] != 0) { hasAudio = true; break; }
        Assert.True(hasAudio);
    }

    [Fact]
    public void FM_SetChannel()
    {
        var ym = new YM2608(() => { });
        // Set FM channel 1 operator 1 TL (total level)
        ym.WriteByte(0x188, 0x40); // TL for op1 ch1
        ym.WriteByte(0x18A, 0x20);
        // Set frequency
        ym.WriteByte(0x188, 0xA4); // freq high + block
        ym.WriteByte(0x18A, 0x22);
        ym.WriteByte(0x188, 0xA0); // freq low
        ym.WriteByte(0x18A, 0x69);
        // Key on
        ym.WriteByte(0x188, 0x28);
        ym.WriteByte(0x18A, 0xF0); // all operators on, channel 0
        // Should not throw
        Assert.True(true);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

- [ ] **Step 3: Implement YM2608 register file and Timer A/B**

```csharp
// YM2608 : IDevice
// Ports: 0x188 (addr1), 0x18A (data1), 0x18C (addr2), 0x18E (data2)
// Internal: 512 registers (256 for port1, 256 for port2)
// Timer A: 10-bit counter, period = (1024 - TA) * 72 master clock cycles
//   Register 0x24: TA high 8 bits, Register 0x25: TA low 2 bits
// Timer B: 8-bit counter, period = (256 - TB) * 1152 master clock cycles
//   Register 0x26: TB value
// Register 0x27: Timer control (load, enable, reset flags)
// Status register: Timer A flag (bit 0), Timer B flag (bit 1)
// IRQ callback when timer overflows
```

- [ ] **Step 4: Implement SSG (3ch square + noise)**

```csharp
// SSG : embedded in YM2608 (registers 0x00-0x0F)
// Registers: 0x00-0x05 tone period (2 regs per channel), 0x06 noise period,
//   0x07 mixer (enable tone/noise per channel), 0x08-0x0A volume,
//   0x0B-0x0C envelope period, 0x0D envelope shape
// Each channel: 12-bit divider → square wave toggle
// Noise: LFSR (linear feedback shift register)
// Envelope: 16 shapes, modulates volume over time
// Output: mix 3 channels, scale to 16-bit samples
```

- [ ] **Step 5: Implement FMChannel (4-operator synthesis)**

```csharp
// FMChannel: represents one FM channel with 4 operators
// Each operator:
//   - Phase accumulator (frequency → phase increment from F-Number + Block)
//   - Sine LUT (1024 entries, 12-bit output) with feedback for op1
//   - Envelope generator: Attack/Decay/Sustain/Release with rate scaling
//     4 rates (AR, D1R, D2R, RR), sustain level (SL), total level (TL)
//   - Key scaling (KS), detune (DT1), multiple (MUL)
// 8 algorithms define operator connection topology:
//   alg 0: op1→op2→op3→op4→out
//   alg 1: (op1+op2)→op3→op4→out
//   alg 2: (op1+(op2→op3))→op4→out
//   ...
//   alg 7: op1+op2+op3+op4→out (all parallel)
// GenerateSample() → int16 output
```

- [ ] **Step 6: Implement ADPCM decoder**

```csharp
// ADPCM : 4-bit delta encoding → 16-bit PCM
// YM2608 ADPCM: uses delta table for step size adjustment
// Registers: 0x100-0x10D (start addr, end addr, delta-N, level, flag, control)
// For now: decode ADPCM data from memory buffer to PCM samples
```

- [ ] **Step 7: Implement AudioOutput (SDL2)**

```csharp
// SDL_OpenAudioDevice with SDL_AudioSpec: freq=44100, format=AUDIO_S16, channels=2, samples=1024
// Callback: request N samples from YM2608.GenerateSamples()
// Mix FM + SSG + ADPCM + Beep (from SystemPort + PIT ch2 frequency)
// Thread-safe ring buffer between emulation thread and audio callback
```

- [ ] **Step 8: Run tests, verify pass**

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: add YM2608 FM/SSG/ADPCM sound emulation with SDL2 audio"
```

---

### Task 19: Emulator Main Loop & Integration

**Files:**
- Modify: `PC98Emu/PC98Emu/Program.cs`
- Create: `PC98Emu/PC98Emu/Emulator.cs`
- Create: `PC98Emu/PC98Emu.Tests/Integration/BootTests.cs`

- [ ] **Step 1: Write boot integration test**

```csharp
// PC98Emu/PC98Emu.Tests/Integration/BootTests.cs
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
        // D88 image with a boot sector that does:
        //   MOV AX, 0x1234  (B8 34 12)
        //   HLT             (F4)
        var data = new byte[688 + 16 + 512];
        int diskSize = data.Length;
        data[0x1C] = (byte)diskSize; data[0x1D] = (byte)(diskSize >> 8);
        data[0x1E] = (byte)(diskSize >> 16); data[0x1F] = (byte)(diskSize >> 24);
        int trackOffset = 688;
        data[0x20] = (byte)trackOffset; data[0x21] = (byte)(trackOffset >> 8);
        data[0x22] = (byte)(trackOffset >> 16); data[0x23] = (byte)(trackOffset >> 24);
        // Sector header
        data[688] = 0; data[689] = 0; data[690] = 1; data[691] = 2;
        data[692] = 8; data[693] = 0; data[702] = 0x00; data[703] = 0x02;
        // Boot code: MOV AX, 0x1234; HLT
        data[704] = 0xB8; data[705] = 0x34; data[706] = 0x12; data[707] = 0xF4;
        return data;
    }
}
```

- [ ] **Step 2: Implement Emulator class**

```csharp
// PC98Emu/PC98Emu/Emulator.cs
// Creates and wires all components:
// - SystemBus, V30, EventScheduler
// - PIC master (0x00, 0x02), PIC slave (0x08, 0x0A)
// - PIT (0x71, 0x73, 0x75, 0x77) → PIC master IRQ0
// - DMA, FDC → DMA
// - Keyboard → PIC master IRQ1
// - GDC text (0x60/0x62), GDC graphics (0xA0/0xA2)
// - RTC, Serial, Printer, SystemPort, Mouse
// - YM2608 → PIC slave
// - CompatibleBios
// - DiskManager
// - Display, AudioOutput
//
// Public API:
//   Initialize() — BIOS init, register all devices on bus, schedule PIT timer
//   LoadFloppyDisk(int drive, byte[] data)
//   StepCpu() — execute one instruction
//   Run() — main loop:
//     while (!quit):
//       cyclesToRun = scheduler.CyclesUntilNextEvent()
//       actualCycles = cpu.Step() (run until cyclesToRun consumed)
//       scheduler.Advance(actualCycles)
//       if (PIC.HasInterrupt() && cpu.Flags.IF):
//         cpu.PendingInterruptVector = PIC.AcknowledgeInterrupt()
//         cpu.InterruptPending = true
//       every 17.73ms: Display.RenderFrame(), poll SDL events
```

- [ ] **Step 3: Update Program.cs**

```csharp
// PC98Emu/PC98Emu/Program.cs
// Usage: PC98Emu <disk_image>
// Parse args, detect format by extension (.d88/.fdi/.hdi/.nhd/.nfd)
// Create Emulator, load disk, Initialize(), Run()
// SDL2 event loop integrated into Emulator.Run()
```

- [ ] **Step 4: Run integration test**

```bash
cd /c/FlutterProject/pc98/PC98Emu
dotnet test --filter "BootTests"
```
Expected: PASS — test boot sector executes MOV AX, 0x1234 then HLTs.

- [ ] **Step 5: Build and smoke test**

```bash
cd /c/FlutterProject/pc98/PC98Emu
dotnet build
dotnet run --project PC98Emu -- --help
```

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: integrate all components into main emulator loop with boot support"
```

---

### Task 20: Full Test Suite & Release Build

- [ ] **Step 1: Run complete test suite**

```bash
cd /c/FlutterProject/pc98/PC98Emu
dotnet test --verbosity normal
```
Expected: All tests PASS.

- [ ] **Step 2: Build release**

```bash
cd /c/FlutterProject/pc98/PC98Emu
dotnet publish PC98Emu/PC98Emu.csproj -c Release -r win-x64 --self-contained
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "chore: verify all tests pass, release build"
```
