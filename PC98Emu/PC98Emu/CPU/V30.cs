using PC98Emu.Bus;

namespace PC98Emu.CPU;

public class V30
{
    private readonly SystemBus _bus;

    // General purpose registers (stored as 32-bit for 386 compatibility)
    public uint EAX, EBX, ECX, EDX;
    public uint ESI, EDI, EBP, ESP32;

    // 16-bit register accessors
    public ushort AX { get => (ushort)EAX; set => EAX = (EAX & 0xFFFF0000) | value; }
    public ushort BX { get => (ushort)EBX; set => EBX = (EBX & 0xFFFF0000) | value; }
    public ushort CX { get => (ushort)ECX; set => ECX = (ECX & 0xFFFF0000) | value; }
    public ushort DX { get => (ushort)EDX; set => EDX = (EDX & 0xFFFF0000) | value; }
    public ushort SI { get => (ushort)ESI; set => ESI = (ESI & 0xFFFF0000) | value; }
    public ushort DI { get => (ushort)EDI; set => EDI = (EDI & 0xFFFF0000) | value; }
    public ushort BP { get => (ushort)EBP; set => EBP = (EBP & 0xFFFF0000) | value; }
    public ushort SP { get => (ushort)ESP32; set => ESP32 = (ESP32 & 0xFFFF0000) | value; }

    // Segment registers
    public ushort CS, DS, ES, SS;
    public ushort FS, GS; // 386+ extra segment registers

    // Instruction pointer
    public ushort IP;

    // High/low byte accessors
    public byte AL { get => (byte)AX; set => AX = (ushort)((AX & 0xFF00) | value); }
    public byte AH { get => (byte)(AX >> 8); set => AX = (ushort)((AX & 0x00FF) | (value << 8)); }
    public byte BL { get => (byte)BX; set => BX = (ushort)((BX & 0xFF00) | value); }
    public byte BH { get => (byte)(BX >> 8); set => BX = (ushort)((BX & 0x00FF) | (value << 8)); }
    public byte CL { get => (byte)CX; set => CX = (ushort)((CX & 0xFF00) | value); }
    public byte CH { get => (byte)(CX >> 8); set => CX = (ushort)((CX & 0x00FF) | (value << 8)); }
    public byte DL { get => (byte)DX; set => DX = (ushort)((DX & 0xFF00) | value); }
    public byte DH { get => (byte)(DX >> 8); set => DX = (ushort)((DX & 0x00FF) | (value << 8)); }

    // CPU state
    public CpuFlags Flags = new();
    public ModRMDecoder ModRM = new();
    public bool Halted;
    public long Cycles;

    // Hardware interrupt support
    public bool InterruptPending;
    public byte PendingInterruptVector;

    // Prefix state (reset each instruction)
    internal int SegmentOverride = -1; // -1 = none, 0=ES, 1=CS, 2=SS, 3=DS
    internal bool RepPrefix;
    internal bool RepZero; // true = REPZ/REP, false = REPNZ
    internal bool LockPrefix;
    internal bool OperandSize32; // 0x66 prefix: use 32-bit operands

    // Inhibit interrupts after MOV SS
    private bool _inhibitInterrupt;

    // BIOS interception
    private readonly Dictionary<int, Action> _biosHandlers = new();

    // ModRM decoded state for current instruction
    internal int ModRMSegment; // segment register value
    internal int ModRMOffset; // effective address offset
    internal bool ModRMIsRegister; // true if mod=3

    public V30(SystemBus bus)
    {
        _bus = bus;
    }

    public SystemBus Bus => _bus;

    public static int GetPhysicalAddress(ushort segment, ushort offset)
    {
        return ((segment << 4) + offset) & 0xFFFFF;
    }

    public byte FetchByte()
    {
        byte value = _bus.ReadMemoryByte(GetPhysicalAddress(CS, IP));
        IP++;
        return value;
    }

    public ushort FetchWord()
    {
        byte lo = FetchByte();
        byte hi = FetchByte();
        return (ushort)(lo | (hi << 8));
    }

    // 8-bit register index: 0=AL,1=CL,2=DL,3=BL,4=AH,5=CH,6=DH,7=BH
    public byte GetRegister8(int index)
    {
        return index switch
        {
            0 => AL, 1 => CL, 2 => DL, 3 => BL,
            4 => AH, 5 => CH, 6 => DH, 7 => BH,
            _ => 0
        };
    }

    public void SetRegister8(int index, byte value)
    {
        switch (index)
        {
            case 0: AL = value; break;
            case 1: CL = value; break;
            case 2: DL = value; break;
            case 3: BL = value; break;
            case 4: AH = value; break;
            case 5: CH = value; break;
            case 6: DH = value; break;
            case 7: BH = value; break;
        }
    }

    // 16-bit register index: 0=AX,1=CX,2=DX,3=BX,4=SP,5=BP,6=SI,7=DI
    public ushort GetRegister16(int index)
    {
        return index switch
        {
            0 => AX, 1 => CX, 2 => DX, 3 => BX,
            4 => SP, 5 => BP, 6 => SI, 7 => DI,
            _ => 0
        };
    }

    public void SetRegister16(int index, ushort value)
    {
        switch (index)
        {
            case 0: AX = value; break;
            case 1: CX = value; break;
            case 2: DX = value; break;
            case 3: BX = value; break;
            case 4: SP = value; break;
            case 5: BP = value; break;
            case 6: SI = value; break;
            case 7: DI = value; break;
        }
    }

    // 32-bit register access (for 0x66 prefix)
    public uint GetRegister32(int index)
    {
        return index switch
        {
            0 => EAX, 1 => ECX, 2 => EDX, 3 => EBX,
            4 => ESP32, 5 => EBP, 6 => ESI, 7 => EDI,
            _ => 0
        };
    }

    public void SetRegister32(int index, uint value)
    {
        switch (index)
        {
            case 0: EAX = value; break;
            case 1: ECX = value; break;
            case 2: EDX = value; break;
            case 3: EBX = value; break;
            case 4: ESP32 = value; break;
            case 5: EBP = value; break;
            case 6: ESI = value; break;
            case 7: EDI = value; break;
        }
    }

    public uint FetchDWord()
    {
        byte b0 = FetchByte();
        byte b1 = FetchByte();
        byte b2 = FetchByte();
        byte b3 = FetchByte();
        return (uint)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
    }

    public uint ReadModRM32()
    {
        if (ModRMIsRegister)
            return GetRegister32(ModRM.RM);
        int addr = GetPhysicalAddress((ushort)ModRMSegment, (ushort)ModRMOffset);
        return (uint)(_bus.ReadMemoryByte(addr) |
                      (_bus.ReadMemoryByte(addr + 1) << 8) |
                      (_bus.ReadMemoryByte(addr + 2) << 16) |
                      (_bus.ReadMemoryByte(addr + 3) << 24));
    }

    public void WriteModRM32(uint value)
    {
        if (ModRMIsRegister)
        {
            SetRegister32(ModRM.RM, value);
            return;
        }
        int addr = GetPhysicalAddress((ushort)ModRMSegment, (ushort)ModRMOffset);
        _bus.WriteMemoryByte(addr, (byte)value);
        _bus.WriteMemoryByte(addr + 1, (byte)(value >> 8));
        _bus.WriteMemoryByte(addr + 2, (byte)(value >> 16));
        _bus.WriteMemoryByte(addr + 3, (byte)(value >> 24));
    }

    public void Push32(uint value)
    {
        SP -= 4;
        int addr = GetPhysicalAddress(SS, SP);
        _bus.WriteMemoryByte(addr, (byte)value);
        _bus.WriteMemoryByte(addr + 1, (byte)(value >> 8));
        _bus.WriteMemoryByte(addr + 2, (byte)(value >> 16));
        _bus.WriteMemoryByte(addr + 3, (byte)(value >> 24));
    }

    public uint Pop32()
    {
        int addr = GetPhysicalAddress(SS, SP);
        uint val = (uint)(_bus.ReadMemoryByte(addr) |
                          (_bus.ReadMemoryByte(addr + 1) << 8) |
                          (_bus.ReadMemoryByte(addr + 2) << 16) |
                          (_bus.ReadMemoryByte(addr + 3) << 24));
        SP += 4;
        return val;
    }

    // Segment register index: 0=ES,1=CS,2=SS,3=DS,4=FS,5=GS
    public ushort GetSegmentRegister(int index)
    {
        return index switch
        {
            0 => ES, 1 => CS, 2 => SS, 3 => DS, 4 => FS, 5 => GS,
            _ => 0
        };
    }

    public void SetSegmentRegister(int index, ushort value)
    {
        switch (index)
        {
            case 0: ES = value; break;
            case 1: CS = value; break;
            case 2: SS = value; break;
            case 3: DS = value; break;
            case 4: FS = value; break;
            case 5: GS = value; break;
        }
    }

    /// <summary>
    /// Decodes ModR/M byte and computes effective address.
    /// Sets ModRMSegment, ModRMOffset, ModRMIsRegister.
    /// </summary>
    public void DecodeModRM16(byte modrm)
    {
        ModRM.Decode(modrm);
        int mod = ModRM.Mod;
        int rm = ModRM.RM;

        if (mod == 3)
        {
            ModRMIsRegister = true;
            ModRMOffset = rm;
            return;
        }

        ModRMIsRegister = false;
        int defaultSeg = 3; // DS default
        int offset;

        switch (rm)
        {
            case 0: offset = (ushort)(BX + SI); break;
            case 1: offset = (ushort)(BX + DI); break;
            case 2: offset = (ushort)(BP + SI); defaultSeg = 2; break;
            case 3: offset = (ushort)(BP + DI); defaultSeg = 2; break;
            case 4: offset = SI; break;
            case 5: offset = DI; break;
            case 6:
                if (mod == 0)
                {
                    offset = FetchWord();
                    defaultSeg = 3;
                }
                else
                {
                    offset = BP;
                    defaultSeg = 2;
                }
                break;
            case 7: offset = BX; break;
            default: offset = 0; break;
        }

        if (mod == 1)
        {
            sbyte disp8 = (sbyte)FetchByte();
            offset = (ushort)(offset + disp8);
        }
        else if (mod == 2)
        {
            ushort disp16 = FetchWord();
            offset = (ushort)(offset + disp16);
        }

        ModRMOffset = (ushort)offset;
        ModRMSegment = SegmentOverride >= 0 ? SegmentOverride : defaultSeg;
    }

    public byte ReadModRM8()
    {
        if (ModRMIsRegister)
            return GetRegister8(ModRM.RM);
        int addr = GetPhysicalAddress(GetSegmentRegister(ModRMSegment), (ushort)ModRMOffset);
        return _bus.ReadMemoryByte(addr);
    }

    public void WriteModRM8(byte value)
    {
        if (ModRMIsRegister)
            SetRegister8(ModRM.RM, value);
        else
        {
            int addr = GetPhysicalAddress(GetSegmentRegister(ModRMSegment), (ushort)ModRMOffset);
            _bus.WriteMemoryByte(addr, value);
        }
    }

    public ushort ReadModRM16()
    {
        if (ModRMIsRegister)
            return GetRegister16(ModRM.RM);
        int addr = GetPhysicalAddress(GetSegmentRegister(ModRMSegment), (ushort)ModRMOffset);
        return _bus.ReadMemoryWord(addr);
    }

    public void WriteModRM16(ushort value)
    {
        if (ModRMIsRegister)
            SetRegister16(ModRM.RM, value);
        else
        {
            int addr = GetPhysicalAddress(GetSegmentRegister(ModRMSegment), (ushort)ModRMOffset);
            _bus.WriteMemoryWord(addr, value);
        }
    }

    public void Push(ushort value)
    {
        SP -= 2;
        _bus.WriteMemoryWord(GetPhysicalAddress(SS, SP), value);
    }

    public ushort Pop()
    {
        ushort value = _bus.ReadMemoryWord(GetPhysicalAddress(SS, SP));
        SP += 2;
        return value;
    }

    public void Interrupt(byte vector)
    {
        Halted = false; // Hardware interrupt wakes CPU from HLT
        if (vector == 0)
        {
            ushort handler_ip = _bus.ReadMemoryWord(0);
            ushort handler_cs = _bus.ReadMemoryWord(2);
            Console.Error.WriteLine($"[INT0-TRIGGER] INT 0 at {CS:X4}:{IP:X4} → handler {handler_cs:X4}:{handler_ip:X4} AX={AX:X4} BX={BX:X4} CX={CX:X4} DX={DX:X4} DS={DS:X4} ES={ES:X4} SS={SS:X4} SP={SP:X4}");
        }
        Push(Flags.Value);
        Push(CS);
        Push(IP);
        Flags.IF = false;
        Flags.TF = false;
        int ivtAddr = vector * 4;
        IP = _bus.ReadMemoryWord(ivtAddr);
        CS = _bus.ReadMemoryWord(ivtAddr + 2);
    }

    public void RegisterBiosHandler(int physicalAddress, Action handler)
    {
        _biosHandlers[physicalAddress] = handler;
    }

    public void UnregisterBiosHandler(int physicalAddress)
    {
        _biosHandlers.Remove(physicalAddress);
    }

    public int Step()
    {
        // Check for hardware interrupts
        if (InterruptPending && Flags.IF && !_inhibitInterrupt)
        {
            InterruptPending = false;
            Halted = false;
            Interrupt(PendingInterruptVector);
        }

        _inhibitInterrupt = false;

        if (Halted)
            return 1;

        // BIOS interception
        int physAddr = GetPhysicalAddress(CS, IP);
        if (_biosHandlers.TryGetValue(physAddr, out var handler))
        {
            handler();
            return 1;
        }

        // Reset prefix state
        SegmentOverride = -1;
        RepPrefix = false;
        LockPrefix = false;
        OperandSize32 = false;

        // Handle prefixes
        bool parsingPrefixes = true;
        while (parsingPrefixes)
        {
            byte prefix = _bus.ReadMemoryByte(GetPhysicalAddress(CS, IP));
            switch (prefix)
            {
                case 0x26: SegmentOverride = 0; IP++; break; // ES:
                case 0x2E: SegmentOverride = 1; IP++; break; // CS:
                case 0x36: SegmentOverride = 2; IP++; break; // SS:
                case 0x3E: SegmentOverride = 3; IP++; break; // DS:
                case 0x64: SegmentOverride = 4; IP++; break; // FS:
                case 0x65: SegmentOverride = 5; IP++; break; // GS:
                case 0x66: OperandSize32 = true; IP++; break; // Operand size override
                case 0x67: IP++; break; // Address size override (ignored in real mode)
                case 0xF0: LockPrefix = true; IP++; break;   // LOCK
                case 0xF1: IP++; break;   // Undocumented LOCK alias (treat as NOP)
                case 0xF2: RepPrefix = true; RepZero = false; IP++; break; // REPNZ
                case 0xF3: RepPrefix = true; RepZero = true; IP++; break;  // REP/REPZ
                default: parsingPrefixes = false; break;
            }
        }

        byte opcode = FetchByte();
        int cycles = Instructions.Execute(this, _bus, opcode, RepPrefix, RepZero, SegmentOverride);

        // MOV SS inhibits interrupts for one instruction
        if (opcode == 0x8E)
        {
            // Check if the Reg field was SS (index 2)
            if (ModRM.Reg == 2)
                _inhibitInterrupt = true;
        }

        Cycles += cycles;
        return cycles;
    }
}
