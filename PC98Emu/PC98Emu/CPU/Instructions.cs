using PC98Emu.Bus;

namespace PC98Emu.CPU;

public static class Instructions
{
    private static readonly HashSet<int> _warnedOpcodes = new();

    #region ALU Helpers

    public static byte Add8(V30 cpu, byte a, byte b, bool withCarry = false)
    {
        int carry = withCarry && cpu.Flags.CF ? 1 : 0;
        int result = a + b + carry;
        cpu.Flags.CF = result > 0xFF;
        cpu.Flags.OF = ((a ^ b ^ 0x80) & (a ^ result) & 0x80) != 0;
        cpu.Flags.AF = ((a ^ b ^ result) & 0x10) != 0;
        cpu.Flags.UpdateSZP8((byte)result);
        return (byte)result;
    }

    public static ushort Add16(V30 cpu, ushort a, ushort b, bool withCarry = false)
    {
        int carry = withCarry && cpu.Flags.CF ? 1 : 0;
        int result = a + b + carry;
        cpu.Flags.CF = result > 0xFFFF;
        cpu.Flags.OF = ((a ^ b ^ 0x8000) & (a ^ result) & 0x8000) != 0;
        cpu.Flags.AF = ((a ^ b ^ result) & 0x10) != 0;
        cpu.Flags.UpdateSZP16((ushort)result);
        return (ushort)result;
    }

    public static byte Sub8(V30 cpu, byte a, byte b, bool withBorrow = false)
    {
        int borrow = withBorrow && cpu.Flags.CF ? 1 : 0;
        int result = a - b - borrow;
        cpu.Flags.CF = result < 0;
        cpu.Flags.OF = ((a ^ b) & (a ^ result) & 0x80) != 0;
        cpu.Flags.AF = ((a ^ b ^ result) & 0x10) != 0;
        cpu.Flags.UpdateSZP8((byte)result);
        return (byte)result;
    }

    public static ushort Sub16(V30 cpu, ushort a, ushort b, bool withBorrow = false)
    {
        int borrow = withBorrow && cpu.Flags.CF ? 1 : 0;
        int result = a - b - borrow;
        cpu.Flags.CF = result < 0;
        cpu.Flags.OF = ((a ^ b) & (a ^ result) & 0x8000) != 0;
        cpu.Flags.AF = ((a ^ b ^ result) & 0x10) != 0;
        cpu.Flags.UpdateSZP16((ushort)result);
        return (ushort)result;
    }

    public static byte And8(V30 cpu, byte a, byte b)
    {
        byte result = (byte)(a & b);
        cpu.Flags.CF = false;
        cpu.Flags.OF = false;
        cpu.Flags.AF = false;
        cpu.Flags.UpdateSZP8(result);
        return result;
    }

    public static ushort And16(V30 cpu, ushort a, ushort b)
    {
        ushort result = (ushort)(a & b);
        cpu.Flags.CF = false;
        cpu.Flags.OF = false;
        cpu.Flags.AF = false;
        cpu.Flags.UpdateSZP16(result);
        return result;
    }

    public static byte Or8(V30 cpu, byte a, byte b)
    {
        byte result = (byte)(a | b);
        cpu.Flags.CF = false;
        cpu.Flags.OF = false;
        cpu.Flags.AF = false;
        cpu.Flags.UpdateSZP8(result);
        return result;
    }

    public static ushort Or16(V30 cpu, ushort a, ushort b)
    {
        ushort result = (ushort)(a | b);
        cpu.Flags.CF = false;
        cpu.Flags.OF = false;
        cpu.Flags.AF = false;
        cpu.Flags.UpdateSZP16(result);
        return result;
    }

    public static byte Xor8(V30 cpu, byte a, byte b)
    {
        byte result = (byte)(a ^ b);
        cpu.Flags.CF = false;
        cpu.Flags.OF = false;
        cpu.Flags.AF = false;
        cpu.Flags.UpdateSZP8(result);
        return result;
    }

    public static ushort Xor16(V30 cpu, ushort a, ushort b)
    {
        ushort result = (ushort)(a ^ b);
        cpu.Flags.CF = false;
        cpu.Flags.OF = false;
        cpu.Flags.AF = false;
        cpu.Flags.UpdateSZP16(result);
        return result;
    }

    #endregion

    #region Shift/Rotate Helpers

    private static byte ShiftRotate8(V30 cpu, int op, byte val, int count)
    {
        if (count == 0) return val;
        count &= 0x1F; // mask to 5 bits
        if (count == 0) return val;

        int result = val;
        for (int i = 0; i < count; i++)
        {
            switch (op)
            {
                case 0: // ROL
                {
                    int bit7 = (result >> 7) & 1;
                    result = ((result << 1) | bit7) & 0xFF;
                    cpu.Flags.CF = bit7 != 0;
                    break;
                }
                case 1: // ROR
                {
                    int bit0 = result & 1;
                    result = ((result >> 1) | (bit0 << 7)) & 0xFF;
                    cpu.Flags.CF = bit0 != 0;
                    break;
                }
                case 2: // RCL
                {
                    int oldCF = cpu.Flags.CF ? 1 : 0;
                    cpu.Flags.CF = (result & 0x80) != 0;
                    result = ((result << 1) | oldCF) & 0xFF;
                    break;
                }
                case 3: // RCR
                {
                    int oldCF = cpu.Flags.CF ? 1 : 0;
                    cpu.Flags.CF = (result & 1) != 0;
                    result = ((result >> 1) | (oldCF << 7)) & 0xFF;
                    break;
                }
                case 4: // SHL
                    cpu.Flags.CF = (result & 0x80) != 0;
                    result = (result << 1) & 0xFF;
                    break;
                case 5: // SHR
                    cpu.Flags.CF = (result & 1) != 0;
                    result = (result >> 1) & 0xFF;
                    break;
                case 7: // SAR
                    cpu.Flags.CF = (result & 1) != 0;
                    result = (byte)((sbyte)((byte)result) >> 1);
                    break;
            }
        }

        if (op >= 4) // SHL, SHR, SAR update SZP
            cpu.Flags.UpdateSZP8((byte)result);

        if (count == 1)
        {
            switch (op)
            {
                case 0: // ROL
                    cpu.Flags.OF = ((result >> 7) & 1) != (cpu.Flags.CF ? 1 : 0);
                    break;
                case 1: // ROR
                    cpu.Flags.OF = ((result >> 7) & 1) != ((result >> 6) & 1);
                    break;
                case 4: // SHL
                    cpu.Flags.OF = ((result >> 7) & 1) != (cpu.Flags.CF ? 1 : 0);
                    break;
                case 5: // SHR
                    cpu.Flags.OF = (val & 0x80) != 0;
                    break;
                case 7: // SAR
                    cpu.Flags.OF = false;
                    break;
            }
        }

        return (byte)result;
    }

    private static ushort ShiftRotate16(V30 cpu, int op, ushort val, int count)
    {
        if (count == 0) return val;
        count &= 0x1F;
        if (count == 0) return val;

        int result = val;
        for (int i = 0; i < count; i++)
        {
            switch (op)
            {
                case 0: // ROL
                {
                    int bit15 = (result >> 15) & 1;
                    result = ((result << 1) | bit15) & 0xFFFF;
                    cpu.Flags.CF = bit15 != 0;
                    break;
                }
                case 1: // ROR
                {
                    int bit0 = result & 1;
                    result = ((result >> 1) | (bit0 << 15)) & 0xFFFF;
                    cpu.Flags.CF = bit0 != 0;
                    break;
                }
                case 2: // RCL
                {
                    int oldCF = cpu.Flags.CF ? 1 : 0;
                    cpu.Flags.CF = (result & 0x8000) != 0;
                    result = ((result << 1) | oldCF) & 0xFFFF;
                    break;
                }
                case 3: // RCR
                {
                    int oldCF = cpu.Flags.CF ? 1 : 0;
                    cpu.Flags.CF = (result & 1) != 0;
                    result = ((result >> 1) | (oldCF << 15)) & 0xFFFF;
                    break;
                }
                case 4: // SHL
                    cpu.Flags.CF = (result & 0x8000) != 0;
                    result = (result << 1) & 0xFFFF;
                    break;
                case 5: // SHR
                    cpu.Flags.CF = (result & 1) != 0;
                    result = (result >> 1) & 0xFFFF;
                    break;
                case 7: // SAR
                    cpu.Flags.CF = (result & 1) != 0;
                    result = (ushort)((short)((ushort)result) >> 1);
                    break;
            }
        }

        if (op >= 4)
            cpu.Flags.UpdateSZP16((ushort)result);

        if (count == 1)
        {
            switch (op)
            {
                case 0:
                    cpu.Flags.OF = ((result >> 15) & 1) != (cpu.Flags.CF ? 1 : 0);
                    break;
                case 1:
                    cpu.Flags.OF = ((result >> 15) & 1) != ((result >> 14) & 1);
                    break;
                case 4:
                    cpu.Flags.OF = ((result >> 15) & 1) != (cpu.Flags.CF ? 1 : 0);
                    break;
                case 5:
                    cpu.Flags.OF = (val & 0x8000) != 0;
                    break;
                case 7:
                    cpu.Flags.OF = false;
                    break;
            }
        }

        return (ushort)result;
    }

    #endregion

    #region Group helpers

    private static void ExecuteGroup1_8(V30 cpu, int op, byte modrm)
    {
        byte val = cpu.ReadModRM8();
        byte imm = cpu.FetchByte();
        byte result = op switch
        {
            0 => Add8(cpu, val, imm),
            1 => Or8(cpu, val, imm),
            2 => Add8(cpu, val, imm, true),
            3 => Sub8(cpu, val, imm, true),
            4 => And8(cpu, val, imm),
            5 => Sub8(cpu, val, imm),
            6 => Xor8(cpu, val, imm),
            7 => Sub8(cpu, val, imm), // CMP
            _ => val
        };
        if (op != 7) // CMP doesn't store
            cpu.WriteModRM8(result);
    }

    private static void ExecuteGroup1_16(V30 cpu, int op, ushort imm)
    {
        ushort val = cpu.ReadModRM16();
        ushort result = op switch
        {
            0 => Add16(cpu, val, imm),
            1 => Or16(cpu, val, imm),
            2 => Add16(cpu, val, imm, true),
            3 => Sub16(cpu, val, imm, true),
            4 => And16(cpu, val, imm),
            5 => Sub16(cpu, val, imm),
            6 => Xor16(cpu, val, imm),
            7 => Sub16(cpu, val, imm), // CMP
            _ => val
        };
        if (op != 7)
            cpu.WriteModRM16(result);
    }

    #endregion

    public static int Execute(V30 cpu, SystemBus bus, byte opcode, bool rep, bool repZ, int segOverride)
    {
        // Handle 32-bit operand size prefix
        if (cpu.OperandSize32)
            return Execute32(cpu, bus, opcode, segOverride);

        switch (opcode)
        {
            #region ADD 0x00-0x05
            case 0x00: // ADD r/m8, r8
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte val = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                cpu.WriteModRM8(Add8(cpu, val, reg));
                return 3;
            }
            case 0x01: // ADD r/m16, r16
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort val = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                cpu.WriteModRM16(Add16(cpu, val, reg));
                return 3;
            }
            case 0x02: // ADD r8, r/m8
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte rm = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                cpu.SetRegister8(cpu.ModRM.Reg, Add8(cpu, reg, rm));
                return 3;
            }
            case 0x03: // ADD r16, r/m16
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort rm = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                cpu.SetRegister16(cpu.ModRM.Reg, Add16(cpu, reg, rm));
                return 3;
            }
            case 0x04: // ADD AL, imm8
            {
                byte imm = cpu.FetchByte();
                cpu.AL = Add8(cpu, cpu.AL, imm);
                return 4;
            }
            case 0x05: // ADD AX, imm16
            {
                ushort imm = cpu.FetchWord();
                cpu.AX = Add16(cpu, cpu.AX, imm);
                return 4;
            }
            #endregion

            #region OR 0x08-0x0D
            case 0x08:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte val = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                cpu.WriteModRM8(Or8(cpu, val, reg));
                return 3;
            }
            case 0x09:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort val = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                cpu.WriteModRM16(Or16(cpu, val, reg));
                return 3;
            }
            case 0x0A:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte rm = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                cpu.SetRegister8(cpu.ModRM.Reg, Or8(cpu, reg, rm));
                return 3;
            }
            case 0x0B:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort rm = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                cpu.SetRegister16(cpu.ModRM.Reg, Or16(cpu, reg, rm));
                return 3;
            }
            case 0x0C:
            {
                byte imm = cpu.FetchByte();
                cpu.AL = Or8(cpu, cpu.AL, imm);
                return 4;
            }
            case 0x0D:
            {
                ushort imm = cpu.FetchWord();
                cpu.AX = Or16(cpu, cpu.AX, imm);
                return 4;
            }
            #endregion

            #region ADC 0x10-0x15
            case 0x10:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte val = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                cpu.WriteModRM8(Add8(cpu, val, reg, true));
                return 3;
            }
            case 0x11:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort val = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                cpu.WriteModRM16(Add16(cpu, val, reg, true));
                return 3;
            }
            case 0x12:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte rm = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                cpu.SetRegister8(cpu.ModRM.Reg, Add8(cpu, reg, rm, true));
                return 3;
            }
            case 0x13:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort rm = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                cpu.SetRegister16(cpu.ModRM.Reg, Add16(cpu, reg, rm, true));
                return 3;
            }
            case 0x14:
            {
                byte imm = cpu.FetchByte();
                cpu.AL = Add8(cpu, cpu.AL, imm, true);
                return 4;
            }
            case 0x15:
            {
                ushort imm = cpu.FetchWord();
                cpu.AX = Add16(cpu, cpu.AX, imm, true);
                return 4;
            }
            #endregion

            #region SBB 0x18-0x1D
            case 0x18:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte val = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                cpu.WriteModRM8(Sub8(cpu, val, reg, true));
                return 3;
            }
            case 0x19:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort val = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                cpu.WriteModRM16(Sub16(cpu, val, reg, true));
                return 3;
            }
            case 0x1A:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte rm = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                cpu.SetRegister8(cpu.ModRM.Reg, Sub8(cpu, reg, rm, true));
                return 3;
            }
            case 0x1B:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort rm = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                cpu.SetRegister16(cpu.ModRM.Reg, Sub16(cpu, reg, rm, true));
                return 3;
            }
            case 0x1C:
            {
                byte imm = cpu.FetchByte();
                cpu.AL = Sub8(cpu, cpu.AL, imm, true);
                return 4;
            }
            case 0x1D:
            {
                ushort imm = cpu.FetchWord();
                cpu.AX = Sub16(cpu, cpu.AX, imm, true);
                return 4;
            }
            #endregion

            #region AND 0x20-0x25
            case 0x20:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte val = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                cpu.WriteModRM8(And8(cpu, val, reg));
                return 3;
            }
            case 0x21:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort val = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                cpu.WriteModRM16(And16(cpu, val, reg));
                return 3;
            }
            case 0x22:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte rm = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                cpu.SetRegister8(cpu.ModRM.Reg, And8(cpu, reg, rm));
                return 3;
            }
            case 0x23:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort rm = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                cpu.SetRegister16(cpu.ModRM.Reg, And16(cpu, reg, rm));
                return 3;
            }
            case 0x24:
            {
                byte imm = cpu.FetchByte();
                cpu.AL = And8(cpu, cpu.AL, imm);
                return 4;
            }
            case 0x25:
            {
                ushort imm = cpu.FetchWord();
                cpu.AX = And16(cpu, cpu.AX, imm);
                return 4;
            }
            #endregion

            #region DAA/DAS/AAA/AAS
            case 0x27: // DAA
            {
                byte oldAL = cpu.AL;
                bool oldCF = cpu.Flags.CF;
                cpu.Flags.CF = false;
                if ((cpu.AL & 0x0F) > 9 || cpu.Flags.AF)
                {
                    cpu.AL = (byte)(cpu.AL + 6);
                    cpu.Flags.CF = oldCF || (cpu.AL < oldAL);
                    cpu.Flags.AF = true;
                }
                else
                    cpu.Flags.AF = false;
                if (oldAL > 0x99 || oldCF)
                {
                    cpu.AL = (byte)(cpu.AL + 0x60);
                    cpu.Flags.CF = true;
                }
                cpu.Flags.UpdateSZP8(cpu.AL);
                return 4;
            }
            case 0x2F: // DAS
            {
                byte oldAL = cpu.AL;
                bool oldCF = cpu.Flags.CF;
                cpu.Flags.CF = false;
                if ((cpu.AL & 0x0F) > 9 || cpu.Flags.AF)
                {
                    cpu.AL = (byte)(cpu.AL - 6);
                    cpu.Flags.CF = oldCF || (oldAL < 6);
                    cpu.Flags.AF = true;
                }
                else
                    cpu.Flags.AF = false;
                if (oldAL > 0x99 || oldCF)
                {
                    cpu.AL = (byte)(cpu.AL - 0x60);
                    cpu.Flags.CF = true;
                }
                cpu.Flags.UpdateSZP8(cpu.AL);
                return 4;
            }
            case 0x37: // AAA
            {
                if ((cpu.AL & 0x0F) > 9 || cpu.Flags.AF)
                {
                    cpu.AL = (byte)(cpu.AL + 6);
                    cpu.AH = (byte)(cpu.AH + 1);
                    cpu.Flags.AF = true;
                    cpu.Flags.CF = true;
                }
                else
                {
                    cpu.Flags.AF = false;
                    cpu.Flags.CF = false;
                }
                cpu.AL = (byte)(cpu.AL & 0x0F);
                return 8;
            }
            case 0x3F: // AAS
            {
                if ((cpu.AL & 0x0F) > 9 || cpu.Flags.AF)
                {
                    cpu.AL = (byte)(cpu.AL - 6);
                    cpu.AH = (byte)(cpu.AH - 1);
                    cpu.Flags.AF = true;
                    cpu.Flags.CF = true;
                }
                else
                {
                    cpu.Flags.AF = false;
                    cpu.Flags.CF = false;
                }
                cpu.AL = (byte)(cpu.AL & 0x0F);
                return 8;
            }
            #endregion

            #region SUB 0x28-0x2D
            case 0x28:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte val = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                cpu.WriteModRM8(Sub8(cpu, val, reg));
                return 3;
            }
            case 0x29:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort val = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                cpu.WriteModRM16(Sub16(cpu, val, reg));
                return 3;
            }
            case 0x2A:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte rm = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                cpu.SetRegister8(cpu.ModRM.Reg, Sub8(cpu, reg, rm));
                return 3;
            }
            case 0x2B:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort rm = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                cpu.SetRegister16(cpu.ModRM.Reg, Sub16(cpu, reg, rm));
                return 3;
            }
            case 0x2C:
            {
                byte imm = cpu.FetchByte();
                cpu.AL = Sub8(cpu, cpu.AL, imm);
                return 4;
            }
            case 0x2D:
            {
                ushort imm = cpu.FetchWord();
                cpu.AX = Sub16(cpu, cpu.AX, imm);
                return 4;
            }
            #endregion

            #region XOR 0x30-0x35
            case 0x30:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte val = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                cpu.WriteModRM8(Xor8(cpu, val, reg));
                return 3;
            }
            case 0x31:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort val = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                cpu.WriteModRM16(Xor16(cpu, val, reg));
                return 3;
            }
            case 0x32:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte rm = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                cpu.SetRegister8(cpu.ModRM.Reg, Xor8(cpu, reg, rm));
                return 3;
            }
            case 0x33:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort rm = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                cpu.SetRegister16(cpu.ModRM.Reg, Xor16(cpu, reg, rm));
                return 3;
            }
            case 0x34:
            {
                byte imm = cpu.FetchByte();
                cpu.AL = Xor8(cpu, cpu.AL, imm);
                return 4;
            }
            case 0x35:
            {
                ushort imm = cpu.FetchWord();
                cpu.AX = Xor16(cpu, cpu.AX, imm);
                return 4;
            }
            #endregion

            #region CMP 0x38-0x3D
            case 0x38:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte val = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                Sub8(cpu, val, reg);
                return 3;
            }
            case 0x39:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort val = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                Sub16(cpu, val, reg);
                return 3;
            }
            case 0x3A:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte rm = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                Sub8(cpu, reg, rm);
                return 3;
            }
            case 0x3B:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort rm = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                Sub16(cpu, reg, rm);
                return 3;
            }
            case 0x3C:
            {
                byte imm = cpu.FetchByte();
                Sub8(cpu, cpu.AL, imm);
                return 4;
            }
            case 0x3D:
            {
                ushort imm = cpu.FetchWord();
                Sub16(cpu, cpu.AX, imm);
                return 4;
            }
            #endregion

            #region INC/DEC reg16 0x40-0x4F
            case 0x40: case 0x41: case 0x42: case 0x43:
            case 0x44: case 0x45: case 0x46: case 0x47:
            {
                int reg = opcode - 0x40;
                ushort val = cpu.GetRegister16(reg);
                bool oldCF = cpu.Flags.CF;
                cpu.SetRegister16(reg, Add16(cpu, val, 1));
                cpu.Flags.CF = oldCF; // INC doesn't affect CF
                return 2;
            }
            case 0x48: case 0x49: case 0x4A: case 0x4B:
            case 0x4C: case 0x4D: case 0x4E: case 0x4F:
            {
                int reg = opcode - 0x48;
                ushort val = cpu.GetRegister16(reg);
                bool oldCF = cpu.Flags.CF;
                cpu.SetRegister16(reg, Sub16(cpu, val, 1));
                cpu.Flags.CF = oldCF; // DEC doesn't affect CF
                return 2;
            }
            #endregion

            #region PUSH/POP reg16 0x50-0x5F
            case 0x50: case 0x51: case 0x52: case 0x53:
            case 0x54: case 0x55: case 0x56: case 0x57:
            {
                int reg = opcode - 0x50;
                cpu.Push(cpu.GetRegister16(reg));
                return 11;
            }
            case 0x58: case 0x59: case 0x5A: case 0x5B:
            case 0x5C: case 0x5D: case 0x5E: case 0x5F:
            {
                int reg = opcode - 0x58;
                cpu.SetRegister16(reg, cpu.Pop());
                return 8;
            }
            #endregion

            #region PUSH/POP segment registers
            case 0x06: cpu.Push(cpu.ES); return 10; // PUSH ES
            case 0x0E: cpu.Push(cpu.CS); return 10; // PUSH CS
            case 0x16: cpu.Push(cpu.SS); return 10; // PUSH SS
            case 0x1E: cpu.Push(cpu.DS); return 10; // PUSH DS
            case 0x07: cpu.ES = cpu.Pop(); return 8; // POP ES
            case 0x17: cpu.SS = cpu.Pop(); return 8; // POP SS
            case 0x1F: cpu.DS = cpu.Pop(); return 8; // POP DS
            #endregion

            #region Jcc short 0x70-0x7F
            case 0x70: // JO
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (cpu.Flags.OF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            case 0x71: // JNO
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (!cpu.Flags.OF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            case 0x72: // JB/JC
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (cpu.Flags.CF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            case 0x73: // JNB/JNC
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (!cpu.Flags.CF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            case 0x74: // JZ/JE
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (cpu.Flags.ZF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            case 0x75: // JNZ/JNE
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (!cpu.Flags.ZF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            case 0x76: // JBE
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (cpu.Flags.CF || cpu.Flags.ZF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            case 0x77: // JA
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (!cpu.Flags.CF && !cpu.Flags.ZF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            case 0x78: // JS
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (cpu.Flags.SF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            case 0x79: // JNS
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (!cpu.Flags.SF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            case 0x7A: // JP
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (cpu.Flags.PF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            case 0x7B: // JNP
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (!cpu.Flags.PF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            case 0x7C: // JL
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (cpu.Flags.SF != cpu.Flags.OF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            case 0x7D: // JGE
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (cpu.Flags.SF == cpu.Flags.OF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            case 0x7E: // JLE
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (cpu.Flags.ZF || cpu.Flags.SF != cpu.Flags.OF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            case 0x7F: // JG
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (!cpu.Flags.ZF && cpu.Flags.SF == cpu.Flags.OF) cpu.IP = (ushort)(cpu.IP + disp);
                return 4;
            }
            #endregion

            #region Group 1: 0x80-0x83
            case 0x80: // Group 1 r/m8, imm8
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ExecuteGroup1_8(cpu, cpu.ModRM.Reg, modrm);
                return 4;
            }
            case 0x81: // Group 1 r/m16, imm16
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort imm = cpu.FetchWord();
                ExecuteGroup1_16(cpu, cpu.ModRM.Reg, imm);
                return 4;
            }
            case 0x82: // Group 1 r/m8, imm8 (same as 0x80)
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ExecuteGroup1_8(cpu, cpu.ModRM.Reg, modrm);
                return 4;
            }
            case 0x83: // Group 1 r/m16, sign-extended imm8
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort imm = (ushort)(short)(sbyte)cpu.FetchByte();
                ExecuteGroup1_16(cpu, cpu.ModRM.Reg, imm);
                return 4;
            }
            #endregion

            #region XCHG, TEST r/m 0x84-0x87
            case 0x84: // TEST r/m8, r8
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte rm = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                And8(cpu, rm, reg);
                return 3;
            }
            case 0x85: // TEST r/m16, r16
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort rm = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                And16(cpu, rm, reg);
                return 3;
            }
            case 0x86: // XCHG r/m8, r8
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte rm = cpu.ReadModRM8();
                byte reg = cpu.GetRegister8(cpu.ModRM.Reg);
                cpu.WriteModRM8(reg);
                cpu.SetRegister8(cpu.ModRM.Reg, rm);
                return 4;
            }
            case 0x87: // XCHG r/m16, r16
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort rm = cpu.ReadModRM16();
                ushort reg = cpu.GetRegister16(cpu.ModRM.Reg);
                cpu.WriteModRM16(reg);
                cpu.SetRegister16(cpu.ModRM.Reg, rm);
                return 4;
            }
            #endregion

            #region MOV 0x88-0x8E
            case 0x88: // MOV r/m8, r8
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                cpu.WriteModRM8(cpu.GetRegister8(cpu.ModRM.Reg));
                return 2;
            }
            case 0x89: // MOV r/m16, r16
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                cpu.WriteModRM16(cpu.GetRegister16(cpu.ModRM.Reg));
                return 2;
            }
            case 0x8A: // MOV r8, r/m8
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                cpu.SetRegister8(cpu.ModRM.Reg, cpu.ReadModRM8());
                return 2;
            }
            case 0x8B: // MOV r16, r/m16
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                cpu.SetRegister16(cpu.ModRM.Reg, cpu.ReadModRM16());
                return 2;
            }
            case 0x8C: // MOV r/m16, Sreg
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                cpu.WriteModRM16(cpu.GetSegmentRegister(cpu.ModRM.Reg));
                return 2;
            }
            case 0x8D: // LEA r16, m
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                cpu.SetRegister16(cpu.ModRM.Reg, (ushort)cpu.ModRMOffset);
                return 2;
            }
            case 0x8E: // MOV Sreg, r/m16
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                cpu.SetSegmentRegister(cpu.ModRM.Reg, cpu.ReadModRM16());
                return 2;
            }
            #endregion

            #region POP r/m16 0x8F
            case 0x8F:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                cpu.WriteModRM16(cpu.Pop());
                return 8;
            }
            #endregion

            #region NOP, XCHG AX 0x90-0x97
            case 0x90: return 3; // NOP
            case 0x91: case 0x92: case 0x93:
            case 0x94: case 0x95: case 0x96: case 0x97:
            {
                int reg = opcode - 0x90;
                ushort temp = cpu.AX;
                cpu.AX = cpu.GetRegister16(reg);
                cpu.SetRegister16(reg, temp);
                return 3;
            }
            #endregion

            #region CBW, CWD
            case 0x98: // CBW
                cpu.AX = (ushort)(short)(sbyte)cpu.AL;
                return 2;
            case 0x99: // CWD
                cpu.DX = (ushort)((cpu.AX & 0x8000) != 0 ? 0xFFFF : 0);
                return 5;
            #endregion

            #region PUSHF, POPF
            case 0x9C: // PUSHF
                cpu.Push((ushort)(cpu.Flags.Value | 0xF002)); // reserved bits set
                return 10;
            case 0x9D: // POPF
                cpu.Flags.Value = (ushort)(cpu.Pop() | 0x0002); // bit 1 always set
                return 8;
            #endregion

            #region SAHF, LAHF
            case 0x9E: // SAHF
                cpu.Flags.Value = (ushort)((cpu.Flags.Value & 0xFF00) | cpu.AH);
                return 4;
            case 0x9F: // LAHF
                cpu.AH = (byte)(cpu.Flags.Value & 0xFF);
                return 4;
            #endregion

            #region MOV AL/AX, moffs 0xA0-0xA3
            case 0xA0: // MOV AL, [moffs8]
            {
                ushort offset = cpu.FetchWord();
                int seg = segOverride >= 0 ? segOverride : 3;
                cpu.AL = bus.ReadMemoryByte(V30.GetPhysicalAddress(cpu.GetSegmentRegister(seg), offset));
                return 10;
            }
            case 0xA1: // MOV AX, [moffs16]
            {
                ushort offset = cpu.FetchWord();
                int seg = segOverride >= 0 ? segOverride : 3;
                cpu.AX = bus.ReadMemoryWord(V30.GetPhysicalAddress(cpu.GetSegmentRegister(seg), offset));
                return 10;
            }
            case 0xA2: // MOV [moffs8], AL
            {
                ushort offset = cpu.FetchWord();
                int seg = segOverride >= 0 ? segOverride : 3;
                bus.WriteMemoryByte(V30.GetPhysicalAddress(cpu.GetSegmentRegister(seg), offset), cpu.AL);
                return 10;
            }
            case 0xA3: // MOV [moffs16], AX
            {
                ushort offset = cpu.FetchWord();
                int seg = segOverride >= 0 ? segOverride : 3;
                bus.WriteMemoryWord(V30.GetPhysicalAddress(cpu.GetSegmentRegister(seg), offset), cpu.AX);
                return 10;
            }
            #endregion

            #region String operations 0xA4-0xAF
            case 0xA4: // MOVSB
            {
                if (rep)
                {
                    while (cpu.CX != 0)
                    {
                        ExecuteMovsb(cpu, bus, segOverride);
                        cpu.CX--;
                    }
                }
                else
                    ExecuteMovsb(cpu, bus, segOverride);
                return 18;
            }
            case 0xA5: // MOVSW
            {
                if (rep)
                {
                    while (cpu.CX != 0)
                    {
                        ExecuteMovsw(cpu, bus, segOverride);
                        cpu.CX--;
                    }
                }
                else
                    ExecuteMovsw(cpu, bus, segOverride);
                return 18;
            }
            case 0xA6: // CMPSB
            {
                if (rep)
                {
                    while (cpu.CX != 0)
                    {
                        ExecuteCmpsb(cpu, bus, segOverride);
                        cpu.CX--;
                        if (repZ && !cpu.Flags.ZF) break;
                        if (!repZ && cpu.Flags.ZF) break;
                    }
                }
                else
                    ExecuteCmpsb(cpu, bus, segOverride);
                return 22;
            }
            case 0xA7: // CMPSW
            {
                if (rep)
                {
                    while (cpu.CX != 0)
                    {
                        ExecuteCmpsw(cpu, bus, segOverride);
                        cpu.CX--;
                        if (repZ && !cpu.Flags.ZF) break;
                        if (!repZ && cpu.Flags.ZF) break;
                    }
                }
                else
                    ExecuteCmpsw(cpu, bus, segOverride);
                return 22;
            }
            case 0xA8: // TEST AL, imm8
            {
                byte imm = cpu.FetchByte();
                And8(cpu, cpu.AL, imm);
                return 4;
            }
            case 0xA9: // TEST AX, imm16
            {
                ushort imm = cpu.FetchWord();
                And16(cpu, cpu.AX, imm);
                return 4;
            }
            case 0xAA: // STOSB
            {
                if (rep)
                {
                    while (cpu.CX != 0)
                    {
                        ExecuteStosb(cpu, bus);
                        cpu.CX--;
                    }
                }
                else
                    ExecuteStosb(cpu, bus);
                return 11;
            }
            case 0xAB: // STOSW
            {
                if (rep)
                {
                    while (cpu.CX != 0)
                    {
                        ExecuteStosw(cpu, bus);
                        cpu.CX--;
                    }
                }
                else
                    ExecuteStosw(cpu, bus);
                return 11;
            }
            case 0xAC: // LODSB
            {
                if (rep)
                {
                    while (cpu.CX != 0)
                    {
                        ExecuteLodsb(cpu, bus, segOverride);
                        cpu.CX--;
                    }
                }
                else
                    ExecuteLodsb(cpu, bus, segOverride);
                return 12;
            }
            case 0xAD: // LODSW
            {
                if (rep)
                {
                    while (cpu.CX != 0)
                    {
                        ExecuteLodsw(cpu, bus, segOverride);
                        cpu.CX--;
                    }
                }
                else
                    ExecuteLodsw(cpu, bus, segOverride);
                return 12;
            }
            case 0xAE: // SCASB
            {
                if (rep)
                {
                    while (cpu.CX != 0)
                    {
                        ExecuteScasb(cpu, bus);
                        cpu.CX--;
                        if (repZ && !cpu.Flags.ZF) break;
                        if (!repZ && cpu.Flags.ZF) break;
                    }
                }
                else
                    ExecuteScasb(cpu, bus);
                return 15;
            }
            case 0xAF: // SCASW
            {
                if (rep)
                {
                    while (cpu.CX != 0)
                    {
                        ExecuteScasw(cpu, bus);
                        cpu.CX--;
                        if (repZ && !cpu.Flags.ZF) break;
                        if (!repZ && cpu.Flags.ZF) break;
                    }
                }
                else
                    ExecuteScasw(cpu, bus);
                return 15;
            }
            #endregion

            #region MOV reg, imm 0xB0-0xBF
            case 0xB0: case 0xB1: case 0xB2: case 0xB3:
            case 0xB4: case 0xB5: case 0xB6: case 0xB7:
            {
                int reg = opcode - 0xB0;
                cpu.SetRegister8(reg, cpu.FetchByte());
                return 4;
            }
            case 0xB8: case 0xB9: case 0xBA: case 0xBB:
            case 0xBC: case 0xBD: case 0xBE: case 0xBF:
            {
                int reg = opcode - 0xB8;
                cpu.SetRegister16(reg, cpu.FetchWord());
                return 4;
            }
            #endregion

            #region RET/RETF 0xC2-0xC3, 0xCA-0xCB
            case 0xC2: // RET imm16
            {
                ushort imm = cpu.FetchWord();
                cpu.IP = cpu.Pop();
                cpu.SP += imm;
                return 20;
            }
            case 0xC3: // RET near
                cpu.IP = cpu.Pop();
                return 16;
            case 0xCA: // RETF imm16
            {
                ushort imm = cpu.FetchWord();
                cpu.IP = cpu.Pop();
                cpu.CS = cpu.Pop();
                cpu.SP += imm;
                return 25;
            }
            case 0xCB: // RETF
                cpu.IP = cpu.Pop();
                cpu.CS = cpu.Pop();
                return 26;
            #endregion

            #region LES, LDS 0xC4-0xC5
            case 0xC4: // LES r16, m
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort off = cpu.ReadModRM16();
                // Read segment from next word
                int addr = V30.GetPhysicalAddress(cpu.GetSegmentRegister(cpu.ModRMSegment), (ushort)(cpu.ModRMOffset + 2));
                ushort seg = bus.ReadMemoryWord(addr);
                cpu.SetRegister16(cpu.ModRM.Reg, off);
                cpu.ES = seg;
                return 16;
            }
            case 0xC5: // LDS r16, m
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort off = cpu.ReadModRM16();
                int addr = V30.GetPhysicalAddress(cpu.GetSegmentRegister(cpu.ModRMSegment), (ushort)(cpu.ModRMOffset + 2));
                ushort seg = bus.ReadMemoryWord(addr);
                cpu.SetRegister16(cpu.ModRM.Reg, off);
                cpu.DS = seg;
                return 16;
            }
            #endregion

            #region MOV r/m, imm 0xC6-0xC7
            case 0xC6: // MOV r/m8, imm8
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte imm = cpu.FetchByte();
                cpu.WriteModRM8(imm);
                return 10;
            }
            case 0xC7: // MOV r/m16, imm16
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort imm = cpu.FetchWord();
                cpu.WriteModRM16(imm);
                return 10;
            }
            #endregion

            #region INT, IRET 0xCD, 0xCF
            case 0xCC: // INT 3
                cpu.Interrupt(3);
                return 52;
            case 0xCD: // INT imm8
            {
                byte vector = cpu.FetchByte();
                if (vector == 0) Console.Error.WriteLine($"[INT0] Software INT 0 at {cpu.CS:X4}:{(ushort)(cpu.IP - 2):X4} AX={cpu.AX:X4} BX={cpu.BX:X4} CX={cpu.CX:X4} DX={cpu.DX:X4} DS={cpu.DS:X4} ES={cpu.ES:X4} SS={cpu.SS:X4} SP={cpu.SP:X4}");
                cpu.Interrupt(vector);
                return 51;
            }
            case 0xCE: // INTO - Interrupt on Overflow
                if (cpu.Flags.OF)
                    cpu.Interrupt(4);
                return 4;
            case 0xCF: // IRET
            {
                cpu.IP = cpu.Pop();
                cpu.CS = cpu.Pop();
                cpu.Flags.Value = cpu.Pop();
                return 32;
            }
            #endregion

            #region Shift/Rotate Group 2: 0xD0-0xD3
            #region 0xC0/0xC1 Shift/Rotate with imm8 count (80186)
            case 0xC0: // Group 2 r/m8, imm8
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte val = cpu.ReadModRM8();
                byte count = cpu.FetchByte();
                cpu.WriteModRM8(ShiftRotate8(cpu, cpu.ModRM.Reg, val, count));
                return 5;
            }
            case 0xC1: // Group 2 r/m16, imm8
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort val = cpu.ReadModRM16();
                byte count = cpu.FetchByte();
                cpu.WriteModRM16(ShiftRotate16(cpu, cpu.ModRM.Reg, val, count));
                return 5;
            }
            #endregion

            case 0xD0: // Group 2 r/m8, 1
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte val = cpu.ReadModRM8();
                cpu.WriteModRM8(ShiftRotate8(cpu, cpu.ModRM.Reg, val, 1));
                return 2;
            }
            case 0xD1: // Group 2 r/m16, 1
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort val = cpu.ReadModRM16();
                cpu.WriteModRM16(ShiftRotate16(cpu, cpu.ModRM.Reg, val, 1));
                return 2;
            }
            case 0xD2: // Group 2 r/m8, CL
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte val = cpu.ReadModRM8();
                cpu.WriteModRM8(ShiftRotate8(cpu, cpu.ModRM.Reg, val, cpu.CL));
                return 8;
            }
            case 0xD3: // Group 2 r/m16, CL
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort val = cpu.ReadModRM16();
                cpu.WriteModRM16(ShiftRotate16(cpu, cpu.ModRM.Reg, val, cpu.CL));
                return 8;
            }
            #endregion

            #region AAM, AAD
            case 0xD4: // AAM
            {
                byte imm = cpu.FetchByte();
                if (imm == 0)
                {
                    Console.Error.WriteLine($"[DIV0] AAM by zero at {cpu.CS:X4}:{(ushort)(cpu.IP - 2):X4} AX={cpu.AX:X4}");
                    cpu.Interrupt(0);
                    return 83;
                }
                cpu.AH = (byte)(cpu.AL / imm);
                cpu.AL = (byte)(cpu.AL % imm);
                cpu.Flags.UpdateSZP16(cpu.AX);
                return 83;
            }
            case 0xD5: // AAD
            {
                byte imm = cpu.FetchByte();
                cpu.AL = (byte)(cpu.AH * imm + cpu.AL);
                cpu.AH = 0;
                cpu.Flags.UpdateSZP16(cpu.AX);
                return 60;
            }
            #endregion

            #region 0x9B FWAIT
            case 0x9B: // FWAIT - wait for FPU (NOP without FPU)
                return 1;
            #endregion

            #region 0x63 ARPL (NOP in real mode)
            case 0x63:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                // ARPL is undefined in real mode, skip it
                return 3;
            }
            #endregion

            #region 0xD8-0xDF FPU instructions (skip)
            case 0xD8: case 0xD9: case 0xDA: case 0xDB:
            case 0xDC: case 0xDD: case 0xDE: case 0xDF:
            {
                // Skip FPU instructions by reading ModRM and any memory operand
                byte modrm = cpu.FetchByte();
                int mod = modrm >> 6;
                if (mod != 3) // memory operand
                {
                    cpu.DecodeModRM16(modrm);
                    // For opcodes like FILD/FISTP that reference memory, we just decoded the address
                }
                // mod=3 means register-to-register FPU op (ST(i)), no additional bytes
                return 2;
            }
            #endregion

            #region 0xD6 SALC (undocumented)
            case 0xD6: // SALC - Set AL to CF
            {
                cpu.AL = cpu.Flags.CF ? (byte)0xFF : (byte)0x00;
                return 3;
            }
            #endregion

            #region XLAT 0xD7
            case 0xD7: // XLAT
            {
                int seg = segOverride >= 0 ? segOverride : 3;
                int addr = V30.GetPhysicalAddress(cpu.GetSegmentRegister(seg), (ushort)(cpu.BX + cpu.AL));
                cpu.AL = bus.ReadMemoryByte(addr);
                return 11;
            }
            #endregion

            #region LOOP, JCXZ 0xE0-0xE3
            case 0xE0: // LOOPNZ
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                cpu.CX--;
                if (cpu.CX != 0 && !cpu.Flags.ZF)
                    cpu.IP = (ushort)(cpu.IP + disp);
                return 5;
            }
            case 0xE1: // LOOPZ
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                cpu.CX--;
                if (cpu.CX != 0 && cpu.Flags.ZF)
                    cpu.IP = (ushort)(cpu.IP + disp);
                return 5;
            }
            case 0xE2: // LOOP
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                cpu.CX--;
                if (cpu.CX != 0)
                    cpu.IP = (ushort)(cpu.IP + disp);
                return 5;
            }
            case 0xE3: // JCXZ
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                if (cpu.CX == 0)
                    cpu.IP = (ushort)(cpu.IP + disp);
                return 6;
            }
            #endregion

            #region IN/OUT imm8 0xE4-0xE7
            case 0xE4: // IN AL, imm8
            {
                byte port = cpu.FetchByte();
                cpu.AL = bus.ReadIoByte(port);
                return 10;
            }
            case 0xE5: // IN AX, imm8
            {
                byte port = cpu.FetchByte();
                cpu.AX = bus.ReadIoWord(port);
                return 10;
            }
            case 0xE6: // OUT imm8, AL
            {
                byte port = cpu.FetchByte();
                bus.WriteIoByte(port, cpu.AL);
                return 10;
            }
            case 0xE7: // OUT imm8, AX
            {
                byte port = cpu.FetchByte();
                bus.WriteIoWord(port, cpu.AX);
                return 10;
            }
            #endregion

            #region CALL/JMP 0xE8-0xEB
            case 0xE8: // CALL near
            {
                short disp = (short)cpu.FetchWord();
                cpu.Push(cpu.IP);
                cpu.IP = (ushort)(cpu.IP + disp);
                return 19;
            }
            case 0xE9: // JMP near
            {
                short disp = (short)cpu.FetchWord();
                cpu.IP = (ushort)(cpu.IP + disp);
                return 15;
            }
            case 0xEA: // JMP far
            {
                ushort newIP = cpu.FetchWord();
                ushort newCS = cpu.FetchWord();
                cpu.IP = newIP;
                cpu.CS = newCS;
                return 15;
            }
            case 0xEB: // JMP short
            {
                sbyte disp = (sbyte)cpu.FetchByte();
                cpu.IP = (ushort)(cpu.IP + disp);
                return 15;
            }
            #endregion

            #region IN/OUT DX 0xEC-0xEF
            case 0xEC: // IN AL, DX
                cpu.AL = bus.ReadIoByte(cpu.DX);
                return 8;
            case 0xED: // IN AX, DX
                cpu.AX = bus.ReadIoWord(cpu.DX);
                return 8;
            case 0xEE: // OUT DX, AL
                bus.WriteIoByte(cpu.DX, cpu.AL);
                return 8;
            case 0xEF: // OUT DX, AX
                bus.WriteIoWord(cpu.DX, cpu.AX);
                return 8;
            #endregion

            #region Misc flags 0xF4-0xFD
            case 0xF4: // HLT
                cpu.Halted = true;
                return 2;
            case 0xF5: // CMC
                cpu.Flags.CF = !cpu.Flags.CF;
                return 2;
            #endregion

            #region Group 3: 0xF6-0xF7
            case 0xF6: // Group 3 r/m8
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                switch (cpu.ModRM.Reg)
                {
                    case 0: // TEST r/m8, imm8
                    case 1:
                    {
                        byte val = cpu.ReadModRM8();
                        byte imm = cpu.FetchByte();
                        And8(cpu, val, imm);
                        break;
                    }
                    case 2: // NOT r/m8
                    {
                        byte val = cpu.ReadModRM8();
                        cpu.WriteModRM8((byte)~val);
                        break;
                    }
                    case 3: // NEG r/m8
                    {
                        byte val = cpu.ReadModRM8();
                        cpu.Flags.CF = val != 0;
                        byte result = (byte)(-(sbyte)val);
                        cpu.Flags.OF = val == 0x80;
                        cpu.Flags.AF = (val & 0x0F) != 0;
                        cpu.Flags.UpdateSZP8(result);
                        cpu.WriteModRM8(result);
                        break;
                    }
                    case 4: // MUL r/m8
                    {
                        byte val = cpu.ReadModRM8();
                        ushort result = (ushort)(cpu.AL * val);
                        cpu.AX = result;
                        cpu.Flags.CF = cpu.Flags.OF = cpu.AH != 0;
                        break;
                    }
                    case 5: // IMUL r/m8
                    {
                        byte val = cpu.ReadModRM8();
                        short result = (short)((sbyte)cpu.AL * (sbyte)val);
                        cpu.AX = (ushort)result;
                        cpu.Flags.CF = cpu.Flags.OF = (cpu.AH != 0x00 && cpu.AH != 0xFF);
                        break;
                    }
                    case 6: // DIV r/m8
                    {
                        byte val = cpu.ReadModRM8();
                        if (val == 0) { Console.Error.WriteLine($"[DIV0] DIV8 by zero at {cpu.CS:X4}:{(ushort)(cpu.IP - 2):X4} AX={cpu.AX:X4} BX={cpu.BX:X4} CX={cpu.CX:X4} DX={cpu.DX:X4} DS={cpu.DS:X4} ES={cpu.ES:X4} SP={cpu.SP:X4}"); cpu.Interrupt(0); return 1; }
                        ushort num = cpu.AX;
                        int quot = num / val;
                        int rem = num % val;
                        if (quot > 0xFF) { Console.Error.WriteLine($"[DIV0] DIV8 overflow at {cpu.CS:X4}:{(ushort)(cpu.IP - 2):X4} AX={cpu.AX:X4}/{val:X2} quot={quot:X4}"); cpu.Interrupt(0); return 1; }
                        cpu.AL = (byte)quot;
                        cpu.AH = (byte)rem;
                        break;
                    }
                    case 7: // IDIV r/m8
                    {
                        byte val = cpu.ReadModRM8();
                        if (val == 0) { Console.Error.WriteLine($"[DIV0] IDIV8 by zero at {cpu.CS:X4}:{(ushort)(cpu.IP - 2):X4} AX={cpu.AX:X4} DS={cpu.DS:X4} SP={cpu.SP:X4}"); cpu.Interrupt(0); return 1; }
                        short num = (short)cpu.AX;
                        int quot = num / (sbyte)val;
                        int rem = num % (sbyte)val;
                        if (quot > 127 || quot < -128) { Console.Error.WriteLine($"[DIV0] IDIV8 overflow at {cpu.CS:X4}:{(ushort)(cpu.IP - 2):X4} AX={cpu.AX:X4}/{val:X2} quot={quot}"); cpu.Interrupt(0); return 1; }
                        cpu.AL = (byte)quot;
                        cpu.AH = (byte)rem;
                        break;
                    }
                }
                return 4;
            }
            case 0xF7: // Group 3 r/m16
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                switch (cpu.ModRM.Reg)
                {
                    case 0: // TEST r/m16, imm16
                    case 1:
                    {
                        ushort val = cpu.ReadModRM16();
                        ushort imm = cpu.FetchWord();
                        And16(cpu, val, imm);
                        break;
                    }
                    case 2: // NOT r/m16
                    {
                        ushort val = cpu.ReadModRM16();
                        cpu.WriteModRM16((ushort)~val);
                        break;
                    }
                    case 3: // NEG r/m16
                    {
                        ushort val = cpu.ReadModRM16();
                        cpu.Flags.CF = val != 0;
                        ushort result = (ushort)(-(short)val);
                        cpu.Flags.OF = val == 0x8000;
                        cpu.Flags.AF = (val & 0x0F) != 0;
                        cpu.Flags.UpdateSZP16(result);
                        cpu.WriteModRM16(result);
                        break;
                    }
                    case 4: // MUL r/m16
                    {
                        ushort val = cpu.ReadModRM16();
                        uint result = (uint)cpu.AX * val;
                        cpu.AX = (ushort)result;
                        cpu.DX = (ushort)(result >> 16);
                        cpu.Flags.CF = cpu.Flags.OF = cpu.DX != 0;
                        break;
                    }
                    case 5: // IMUL r/m16
                    {
                        ushort val = cpu.ReadModRM16();
                        int result = (short)cpu.AX * (short)val;
                        cpu.AX = (ushort)result;
                        cpu.DX = (ushort)((uint)result >> 16);
                        cpu.Flags.CF = cpu.Flags.OF = (cpu.DX != 0x0000 && cpu.DX != 0xFFFF);
                        break;
                    }
                    case 6: // DIV r/m16
                    {
                        ushort val = cpu.ReadModRM16();
                        if (val == 0) { Console.Error.WriteLine($"[DIV0] DIV16 by zero at {cpu.CS:X4}:{(ushort)(cpu.IP - 2):X4} DX:AX={cpu.DX:X4}:{cpu.AX:X4} BX={cpu.BX:X4} CX={cpu.CX:X4} DS={cpu.DS:X4} ES={cpu.ES:X4} SP={cpu.SP:X4}"); cpu.Interrupt(0); return 1; }
                        uint num = (uint)((cpu.DX << 16) | cpu.AX);
                        uint quot = num / val;
                        uint rem = num % val;
                        if (quot > 0xFFFF) { Console.Error.WriteLine($"[DIV0] DIV16 overflow at {cpu.CS:X4}:{(ushort)(cpu.IP - 2):X4} DX:AX={cpu.DX:X4}:{cpu.AX:X4}/{val:X4} quot={quot:X8}"); cpu.Interrupt(0); return 1; }
                        cpu.AX = (ushort)quot;
                        cpu.DX = (ushort)rem;
                        break;
                    }
                    case 7: // IDIV r/m16
                    {
                        ushort val = cpu.ReadModRM16();
                        if (val == 0) { Console.Error.WriteLine($"[DIV0] IDIV16 by zero at {cpu.CS:X4}:{(ushort)(cpu.IP - 2):X4} DX:AX={cpu.DX:X4}:{cpu.AX:X4} DS={cpu.DS:X4} SP={cpu.SP:X4}"); cpu.Interrupt(0); return 1; }
                        int num = (int)((cpu.DX << 16) | cpu.AX);
                        int quot = num / (short)val;
                        int rem = num % (short)val;
                        if (quot > 32767 || quot < -32768) { Console.Error.WriteLine($"[DIV0] IDIV16 overflow at {cpu.CS:X4}:{(ushort)(cpu.IP - 2):X4} DX:AX={cpu.DX:X4}:{cpu.AX:X4}/{val:X4} quot={quot}"); cpu.Interrupt(0); return 1; }
                        cpu.AX = (ushort)quot;
                        cpu.DX = (ushort)rem;
                        break;
                    }
                }
                return 4;
            }
            #endregion

            #region Flag operations 0xF8-0xFD
            case 0xF8: cpu.Flags.CF = false; return 2; // CLC
            case 0xF9: cpu.Flags.CF = true; return 2;  // STC
            case 0xFA: cpu.Flags.IF = false; return 2; // CLI
            case 0xFB: cpu.Flags.IF = true; return 2;  // STI
            case 0xFC: cpu.Flags.DF = false; return 2; // CLD
            case 0xFD: cpu.Flags.DF = true; return 2;  // STD
            #endregion

            #region Group 4/5: 0xFE-0xFF
            case 0xFE: // Group 4 r/m8
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte val = cpu.ReadModRM8();
                bool oldCF = cpu.Flags.CF;
                switch (cpu.ModRM.Reg)
                {
                    case 0: // INC r/m8
                        cpu.WriteModRM8(Add8(cpu, val, 1));
                        cpu.Flags.CF = oldCF;
                        break;
                    case 1: // DEC r/m8
                        cpu.WriteModRM8(Sub8(cpu, val, 1));
                        cpu.Flags.CF = oldCF;
                        break;
                }
                return 3;
            }
            case 0xFF: // Group 5
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                switch (cpu.ModRM.Reg)
                {
                    case 0: // INC r/m16
                    {
                        ushort val = cpu.ReadModRM16();
                        bool oldCF = cpu.Flags.CF;
                        cpu.WriteModRM16(Add16(cpu, val, 1));
                        cpu.Flags.CF = oldCF;
                        break;
                    }
                    case 1: // DEC r/m16
                    {
                        ushort val = cpu.ReadModRM16();
                        bool oldCF = cpu.Flags.CF;
                        cpu.WriteModRM16(Sub16(cpu, val, 1));
                        cpu.Flags.CF = oldCF;
                        break;
                    }
                    case 2: // CALL r/m16
                    {
                        ushort target = cpu.ReadModRM16();
                        cpu.Push(cpu.IP);
                        cpu.IP = target;
                        break;
                    }
                    case 3: // CALL FAR m16:16
                    {
                        ushort off = cpu.ReadModRM16();
                        int addr = V30.GetPhysicalAddress(cpu.GetSegmentRegister(cpu.ModRMSegment), (ushort)(cpu.ModRMOffset + 2));
                        ushort seg = bus.ReadMemoryWord(addr);
                        cpu.Push(cpu.CS);
                        cpu.Push(cpu.IP);
                        cpu.CS = seg;
                        cpu.IP = off;
                        break;
                    }
                    case 4: // JMP r/m16
                    {
                        cpu.IP = cpu.ReadModRM16();
                        break;
                    }
                    case 5: // JMP FAR m16:16
                    {
                        ushort off = cpu.ReadModRM16();
                        int addr = V30.GetPhysicalAddress(cpu.GetSegmentRegister(cpu.ModRMSegment), (ushort)(cpu.ModRMOffset + 2));
                        ushort seg = bus.ReadMemoryWord(addr);
                        cpu.CS = seg;
                        cpu.IP = off;
                        break;
                    }
                    case 6: // PUSH r/m16
                    {
                        cpu.Push(cpu.ReadModRM16());
                        break;
                    }
                }
                return 4;
            }
            #endregion

            #region 0x9A CALL far
            case 0x9A:
            {
                ushort newIP = cpu.FetchWord();
                ushort newCS = cpu.FetchWord();
                cpu.Push(cpu.CS);
                cpu.Push(cpu.IP);
                cpu.CS = newCS;
                cpu.IP = newIP;
                return 28;
            }
            #endregion

            #region 80186 extensions

            #region 0x60 PUSHA
            case 0x60:
            {
                ushort temp = cpu.SP;
                cpu.Push(cpu.AX);
                cpu.Push(cpu.CX);
                cpu.Push(cpu.DX);
                cpu.Push(cpu.BX);
                cpu.Push(temp);
                cpu.Push(cpu.BP);
                cpu.Push(cpu.SI);
                cpu.Push(cpu.DI);
                return 36;
            }
            #endregion

            #region 0x61 POPA
            case 0x61:
            {
                cpu.DI = cpu.Pop();
                cpu.SI = cpu.Pop();
                cpu.BP = cpu.Pop();
                cpu.Pop(); // skip SP
                cpu.BX = cpu.Pop();
                cpu.DX = cpu.Pop();
                cpu.CX = cpu.Pop();
                cpu.AX = cpu.Pop();
                return 51;
            }
            #endregion

            #region 0x62 BOUND
            case 0x62:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                // Just decode and skip - BOUND rarely triggers on valid data
                cpu.ReadModRM16(); // consume the memory operand
                return 13;
            }
            #endregion

            #region 0x68 PUSH imm16
            case 0x68:
            {
                ushort imm = cpu.FetchWord();
                cpu.Push(imm);
                return 3;
            }
            #endregion

            #region 0x69 IMUL r16, r/m16, imm16
            case 0x69:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort rm = cpu.ReadModRM16();
                ushort imm = cpu.FetchWord();
                int result = (short)rm * (short)imm;
                cpu.SetRegister16(cpu.ModRM.Reg, (ushort)result);
                cpu.Flags.CF = cpu.Flags.OF = (result != (short)(ushort)result);
                return 26;
            }
            #endregion

            #region 0x6A PUSH imm8 (sign-extended)
            case 0x6A:
            {
                byte imm = cpu.FetchByte();
                cpu.Push((ushort)(short)(sbyte)imm);
                return 3;
            }
            #endregion

            #region 0x6B IMUL r16, r/m16, imm8
            case 0x6B:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                ushort rm = cpu.ReadModRM16();
                byte imm = cpu.FetchByte();
                int result = (short)rm * (sbyte)imm;
                cpu.SetRegister16(cpu.ModRM.Reg, (ushort)result);
                cpu.Flags.CF = cpu.Flags.OF = (result != (short)(ushort)result);
                return 26;
            }
            #endregion

            #region 0xC8 ENTER
            case 0xC8:
            {
                ushort size = cpu.FetchWord();
                byte level = cpu.FetchByte();
                cpu.Push(cpu.BP);
                ushort framePtr = cpu.SP;
                if (level > 0)
                {
                    for (int i = 1; i < level; i++)
                    {
                        cpu.BP -= 2;
                        cpu.Push(bus.ReadMemoryWord(V30.GetPhysicalAddress(cpu.SS, cpu.BP)));
                    }
                    cpu.Push(framePtr);
                }
                cpu.BP = framePtr;
                cpu.SP -= size;
                return 15;
            }
            #endregion

            #region 0xC9 LEAVE
            case 0xC9:
            {
                cpu.SP = cpu.BP;
                cpu.BP = cpu.Pop();
                return 5;
            }
            #endregion

            #region 0x6C/0x6D INS
            case 0x6C: // INSB
            {
                byte val = bus.ReadIoByte(cpu.DX);
                bus.WriteMemoryByte(V30.GetPhysicalAddress(cpu.ES, cpu.DI), val);
                cpu.DI = (ushort)(cpu.Flags.DF ? cpu.DI - 1 : cpu.DI + 1);
                return 14;
            }
            case 0x6D: // INSW
            {
                ushort val = (ushort)(bus.ReadIoByte(cpu.DX) | (bus.ReadIoByte((ushort)(cpu.DX + 1)) << 8));
                bus.WriteMemoryWord(V30.GetPhysicalAddress(cpu.ES, cpu.DI), val);
                cpu.DI = (ushort)(cpu.Flags.DF ? cpu.DI - 2 : cpu.DI + 2);
                return 14;
            }
            #endregion

            #region 0x6E/0x6F OUTS
            case 0x6E: // OUTSB
            {
                int srcSeg = cpu.SegmentOverride >= 0 ? cpu.SegmentOverride : 3;
                byte val = bus.ReadMemoryByte(V30.GetPhysicalAddress(cpu.GetSegmentRegister(srcSeg), cpu.SI));
                bus.WriteIoByte(cpu.DX, val);
                cpu.SI = (ushort)(cpu.Flags.DF ? cpu.SI - 1 : cpu.SI + 1);
                return 14;
            }
            case 0x6F: // OUTSW
            {
                int srcSeg = cpu.SegmentOverride >= 0 ? cpu.SegmentOverride : 3;
                int addr = V30.GetPhysicalAddress(cpu.GetSegmentRegister(srcSeg), cpu.SI);
                ushort val = bus.ReadMemoryWord(addr);
                bus.WriteIoByte(cpu.DX, (byte)val);
                bus.WriteIoByte((ushort)(cpu.DX + 1), (byte)(val >> 8));
                cpu.SI = (ushort)(cpu.Flags.DF ? cpu.SI - 2 : cpu.SI + 2);
                return 14;
            }
            #endregion

            #endregion

            #region 0x0F two-byte opcodes (286+/386+ extensions)
            case 0x0F:
            {
                byte op2 = cpu.FetchByte();
                return ExecuteTwoByteOpcode(cpu, bus, op2);
            }
            #endregion

            default:
                { int key = (opcode << 16) | (cpu.CS << 4) + cpu.IP - 1; if (_warnedOpcodes.Add(key)) Console.Error.WriteLine($"[CPU] Unimplemented opcode 0x{opcode:X2} at {cpu.CS:X4}:{(ushort)(cpu.IP - 1):X4}"); }
                return 1;
        }
    }

    private static int ExecuteTwoByteOpcode(V30 cpu, SystemBus bus, byte op2)
    {
        switch (op2)
        {
            // Jcc rel16 (0x80-0x8F)
            case 0x80: // JO rel16
            case 0x81: // JNO rel16
            case 0x82: // JB/JC rel16
            case 0x83: // JNB/JNC rel16
            case 0x84: // JZ/JE rel16
            case 0x85: // JNZ/JNE rel16
            case 0x86: // JBE/JNA rel16
            case 0x87: // JNBE/JA rel16
            case 0x88: // JS rel16
            case 0x89: // JNS rel16
            case 0x8A: // JP rel16
            case 0x8B: // JNP rel16
            case 0x8C: // JL rel16
            case 0x8D: // JNL/JGE rel16
            case 0x8E: // JLE/JNG rel16
            case 0x8F: // JNLE/JG rel16
            {
                short rel = (short)cpu.FetchWord();
                bool cond = (op2 & 0x0F) switch
                {
                    0x0 => cpu.Flags.OF,
                    0x1 => !cpu.Flags.OF,
                    0x2 => cpu.Flags.CF,
                    0x3 => !cpu.Flags.CF,
                    0x4 => cpu.Flags.ZF,
                    0x5 => !cpu.Flags.ZF,
                    0x6 => cpu.Flags.CF || cpu.Flags.ZF,
                    0x7 => !cpu.Flags.CF && !cpu.Flags.ZF,
                    0x8 => cpu.Flags.SF,
                    0x9 => !cpu.Flags.SF,
                    0xA => cpu.Flags.PF,
                    0xB => !cpu.Flags.PF,
                    0xC => cpu.Flags.SF != cpu.Flags.OF,
                    0xD => cpu.Flags.SF == cpu.Flags.OF,
                    0xE => cpu.Flags.ZF || (cpu.Flags.SF != cpu.Flags.OF),
                    0xF => !cpu.Flags.ZF && (cpu.Flags.SF == cpu.Flags.OF),
                    _ => false
                };
                if (cond)
                    cpu.IP = (ushort)(cpu.IP + rel);
                return 3;
            }

            // MOVZX r16, r/m8
            case 0xB6:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte val = cpu.ReadModRM8();
                cpu.SetRegister16(cpu.ModRM.Reg, val);
                return 3;
            }

            // MOVSX r16, r/m8
            case 0xBE:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                byte val = cpu.ReadModRM8();
                cpu.SetRegister16(cpu.ModRM.Reg, (ushort)(short)(sbyte)val);
                return 3;
            }

            // SETcc r/m8 (0x90-0x9F)
            case 0x90: case 0x91: case 0x92: case 0x93:
            case 0x94: case 0x95: case 0x96: case 0x97:
            case 0x98: case 0x99: case 0x9A: case 0x9B:
            case 0x9C: case 0x9D: case 0x9E: case 0x9F:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                bool cond = (op2 & 0x0F) switch
                {
                    0x0 => cpu.Flags.OF,
                    0x1 => !cpu.Flags.OF,
                    0x2 => cpu.Flags.CF,
                    0x3 => !cpu.Flags.CF,
                    0x4 => cpu.Flags.ZF,
                    0x5 => !cpu.Flags.ZF,
                    0x6 => cpu.Flags.CF || cpu.Flags.ZF,
                    0x7 => !cpu.Flags.CF && !cpu.Flags.ZF,
                    0x8 => cpu.Flags.SF,
                    0x9 => !cpu.Flags.SF,
                    0xA => cpu.Flags.PF,
                    0xB => !cpu.Flags.PF,
                    0xC => cpu.Flags.SF != cpu.Flags.OF,
                    0xD => cpu.Flags.SF == cpu.Flags.OF,
                    0xE => cpu.Flags.ZF || (cpu.Flags.SF != cpu.Flags.OF),
                    0xF => !cpu.Flags.ZF && (cpu.Flags.SF == cpu.Flags.OF),
                    _ => false
                };
                cpu.WriteModRM8(cond ? (byte)1 : (byte)0);
                return 4;
            }

            // SHLD/SHRD (0xA4/0xA5/0xAC/0xAD) - just skip with ModRM
            case 0xA4: case 0xA5: case 0xAC: case 0xAD:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                cpu.FetchByte(); // imm8 or CL
                return 3;
            }

            default:
                { int key = (0x0F00 | op2) << 16 | (cpu.CS << 4) + cpu.IP - 2; if (_warnedOpcodes.Add(key)) Console.Error.WriteLine($"[CPU] Unimplemented two-byte opcode 0x0F 0x{op2:X2} at {cpu.CS:X4}:{(ushort)(cpu.IP - 2):X4}"); }
                return 1;
        }
    }

    #region String operation helpers

    private static void ExecuteMovsb(V30 cpu, SystemBus bus, int segOverride)
    {
        int srcSeg = segOverride >= 0 ? segOverride : 3;
        int srcAddr = V30.GetPhysicalAddress(cpu.GetSegmentRegister(srcSeg), cpu.SI);
        int dstAddr = V30.GetPhysicalAddress(cpu.ES, cpu.DI);
        bus.WriteMemoryByte(dstAddr, bus.ReadMemoryByte(srcAddr));
        cpu.SI = (ushort)(cpu.Flags.DF ? cpu.SI - 1 : cpu.SI + 1);
        cpu.DI = (ushort)(cpu.Flags.DF ? cpu.DI - 1 : cpu.DI + 1);
    }

    private static void ExecuteMovsw(V30 cpu, SystemBus bus, int segOverride)
    {
        int srcSeg = segOverride >= 0 ? segOverride : 3;
        int srcAddr = V30.GetPhysicalAddress(cpu.GetSegmentRegister(srcSeg), cpu.SI);
        int dstAddr = V30.GetPhysicalAddress(cpu.ES, cpu.DI);
        bus.WriteMemoryWord(dstAddr, bus.ReadMemoryWord(srcAddr));
        cpu.SI = (ushort)(cpu.Flags.DF ? cpu.SI - 2 : cpu.SI + 2);
        cpu.DI = (ushort)(cpu.Flags.DF ? cpu.DI - 2 : cpu.DI + 2);
    }

    private static void ExecuteCmpsb(V30 cpu, SystemBus bus, int segOverride)
    {
        int srcSeg = segOverride >= 0 ? segOverride : 3;
        int srcAddr = V30.GetPhysicalAddress(cpu.GetSegmentRegister(srcSeg), cpu.SI);
        int dstAddr = V30.GetPhysicalAddress(cpu.ES, cpu.DI);
        byte a = bus.ReadMemoryByte(srcAddr);
        byte b = bus.ReadMemoryByte(dstAddr);
        Sub8(cpu, a, b);
        cpu.SI = (ushort)(cpu.Flags.DF ? cpu.SI - 1 : cpu.SI + 1);
        cpu.DI = (ushort)(cpu.Flags.DF ? cpu.DI - 1 : cpu.DI + 1);
    }

    private static void ExecuteCmpsw(V30 cpu, SystemBus bus, int segOverride)
    {
        int srcSeg = segOverride >= 0 ? segOverride : 3;
        int srcAddr = V30.GetPhysicalAddress(cpu.GetSegmentRegister(srcSeg), cpu.SI);
        int dstAddr = V30.GetPhysicalAddress(cpu.ES, cpu.DI);
        ushort a = bus.ReadMemoryWord(srcAddr);
        ushort b = bus.ReadMemoryWord(dstAddr);
        Sub16(cpu, a, b);
        cpu.SI = (ushort)(cpu.Flags.DF ? cpu.SI - 2 : cpu.SI + 2);
        cpu.DI = (ushort)(cpu.Flags.DF ? cpu.DI - 2 : cpu.DI + 2);
    }

    private static void ExecuteStosb(V30 cpu, SystemBus bus)
    {
        int addr = V30.GetPhysicalAddress(cpu.ES, cpu.DI);
        bus.WriteMemoryByte(addr, cpu.AL);
        cpu.DI = (ushort)(cpu.Flags.DF ? cpu.DI - 1 : cpu.DI + 1);
    }

    private static void ExecuteStosw(V30 cpu, SystemBus bus)
    {
        int addr = V30.GetPhysicalAddress(cpu.ES, cpu.DI);
        bus.WriteMemoryWord(addr, cpu.AX);
        cpu.DI = (ushort)(cpu.Flags.DF ? cpu.DI - 2 : cpu.DI + 2);
    }

    private static void ExecuteLodsb(V30 cpu, SystemBus bus, int segOverride)
    {
        int seg = segOverride >= 0 ? segOverride : 3;
        int addr = V30.GetPhysicalAddress(cpu.GetSegmentRegister(seg), cpu.SI);
        cpu.AL = bus.ReadMemoryByte(addr);
        cpu.SI = (ushort)(cpu.Flags.DF ? cpu.SI - 1 : cpu.SI + 1);
    }

    private static void ExecuteLodsw(V30 cpu, SystemBus bus, int segOverride)
    {
        int seg = segOverride >= 0 ? segOverride : 3;
        int addr = V30.GetPhysicalAddress(cpu.GetSegmentRegister(seg), cpu.SI);
        cpu.AX = bus.ReadMemoryWord(addr);
        cpu.SI = (ushort)(cpu.Flags.DF ? cpu.SI - 2 : cpu.SI + 2);
    }

    private static void ExecuteScasb(V30 cpu, SystemBus bus)
    {
        int addr = V30.GetPhysicalAddress(cpu.ES, cpu.DI);
        byte val = bus.ReadMemoryByte(addr);
        Sub8(cpu, cpu.AL, val);
        cpu.DI = (ushort)(cpu.Flags.DF ? cpu.DI - 1 : cpu.DI + 1);
    }

    private static void ExecuteScasw(V30 cpu, SystemBus bus)
    {
        int addr = V30.GetPhysicalAddress(cpu.ES, cpu.DI);
        ushort val = bus.ReadMemoryWord(addr);
        Sub16(cpu, cpu.AX, val);
        cpu.DI = (ushort)(cpu.Flags.DF ? cpu.DI - 2 : cpu.DI + 2);
    }

    #endregion

    #region 32-bit operand size handling (0x66 prefix)

    /// <summary>
    /// Handles instructions with 0x66 operand size prefix.
    /// Converts 16-bit operations to 32-bit equivalents for the most common instructions.
    /// </summary>
    private static int Execute32(V30 cpu, SystemBus bus, byte opcode, int segOverride)
    {
        switch (opcode)
        {
            // MOV r32, r/m32
            case 0x8B:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                uint val = cpu.ReadModRM32();
                cpu.SetRegister32(cpu.ModRM.Reg, val);
                return 2;
            }
            // MOV r/m32, r32
            case 0x89:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                cpu.WriteModRM32(cpu.GetRegister32(cpu.ModRM.Reg));
                return 2;
            }
            // MOV r32, imm32 (0xB8-0xBF)
            case 0xB8: case 0xB9: case 0xBA: case 0xBB:
            case 0xBC: case 0xBD: case 0xBE: case 0xBF:
            {
                int reg = opcode - 0xB8;
                uint imm = cpu.FetchDWord();
                cpu.SetRegister32(reg, imm);
                return 2;
            }
            // XOR r32, r/m32
            case 0x33:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                uint rm = cpu.ReadModRM32();
                uint reg = cpu.GetRegister32(cpu.ModRM.Reg);
                uint result = reg ^ rm;
                cpu.SetRegister32(cpu.ModRM.Reg, result);
                cpu.Flags.CF = false;
                cpu.Flags.OF = false;
                cpu.Flags.UpdateSZP16((ushort)result); // approximate
                cpu.Flags.ZF = result == 0;
                cpu.Flags.SF = (result & 0x80000000) != 0;
                return 3;
            }
            // XOR r/m32, r32
            case 0x31:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                uint rm = cpu.ReadModRM32();
                uint reg = cpu.GetRegister32(cpu.ModRM.Reg);
                uint result = rm ^ reg;
                cpu.WriteModRM32(result);
                cpu.Flags.CF = false;
                cpu.Flags.OF = false;
                cpu.Flags.ZF = result == 0;
                cpu.Flags.SF = (result & 0x80000000) != 0;
                return 3;
            }
            // ADD r/m32, r32
            case 0x01:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                uint rm = cpu.ReadModRM32();
                uint reg = cpu.GetRegister32(cpu.ModRM.Reg);
                ulong result = (ulong)rm + reg;
                cpu.WriteModRM32((uint)result);
                cpu.Flags.CF = result > 0xFFFFFFFF;
                cpu.Flags.ZF = (uint)result == 0;
                cpu.Flags.SF = ((uint)result & 0x80000000) != 0;
                return 3;
            }
            // ADD r32, r/m32
            case 0x03:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                uint rm = cpu.ReadModRM32();
                uint reg = cpu.GetRegister32(cpu.ModRM.Reg);
                ulong result = (ulong)reg + rm;
                cpu.SetRegister32(cpu.ModRM.Reg, (uint)result);
                cpu.Flags.CF = result > 0xFFFFFFFF;
                cpu.Flags.ZF = (uint)result == 0;
                cpu.Flags.SF = ((uint)result & 0x80000000) != 0;
                return 3;
            }
            // SUB r32, r/m32
            case 0x2B:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                uint rm = cpu.ReadModRM32();
                uint reg = cpu.GetRegister32(cpu.ModRM.Reg);
                uint result = reg - rm;
                cpu.SetRegister32(cpu.ModRM.Reg, result);
                cpu.Flags.CF = reg < rm;
                cpu.Flags.ZF = result == 0;
                cpu.Flags.SF = (result & 0x80000000) != 0;
                return 3;
            }
            // CMP r32, r/m32
            case 0x3B:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                uint rm = cpu.ReadModRM32();
                uint reg = cpu.GetRegister32(cpu.ModRM.Reg);
                uint result = reg - rm;
                cpu.Flags.CF = reg < rm;
                cpu.Flags.ZF = result == 0;
                cpu.Flags.SF = (result & 0x80000000) != 0;
                cpu.Flags.OF = ((reg ^ rm) & (reg ^ result) & 0x80000000) != 0;
                return 3;
            }
            // CMP r/m32, r32
            case 0x39:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                uint rm = cpu.ReadModRM32();
                uint reg = cpu.GetRegister32(cpu.ModRM.Reg);
                uint result = rm - reg;
                cpu.Flags.CF = rm < reg;
                cpu.Flags.ZF = result == 0;
                cpu.Flags.SF = (result & 0x80000000) != 0;
                cpu.Flags.OF = ((rm ^ reg) & (rm ^ result) & 0x80000000) != 0;
                return 3;
            }
            // AND r32, r/m32
            case 0x23:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                uint rm = cpu.ReadModRM32();
                uint reg = cpu.GetRegister32(cpu.ModRM.Reg);
                uint result = reg & rm;
                cpu.SetRegister32(cpu.ModRM.Reg, result);
                cpu.Flags.CF = false;
                cpu.Flags.OF = false;
                cpu.Flags.ZF = result == 0;
                cpu.Flags.SF = (result & 0x80000000) != 0;
                return 3;
            }
            // OR r32, r/m32
            case 0x0B:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                uint rm = cpu.ReadModRM32();
                uint reg = cpu.GetRegister32(cpu.ModRM.Reg);
                uint result = reg | rm;
                cpu.SetRegister32(cpu.ModRM.Reg, result);
                cpu.Flags.CF = false;
                cpu.Flags.OF = false;
                cpu.Flags.ZF = result == 0;
                cpu.Flags.SF = (result & 0x80000000) != 0;
                return 3;
            }
            // TEST r/m32, r32
            case 0x85:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                uint rm = cpu.ReadModRM32();
                uint reg = cpu.GetRegister32(cpu.ModRM.Reg);
                uint result = rm & reg;
                cpu.Flags.CF = false;
                cpu.Flags.OF = false;
                cpu.Flags.ZF = result == 0;
                cpu.Flags.SF = (result & 0x80000000) != 0;
                return 3;
            }
            // PUSH r32 (0x50-0x57)
            case 0x50: case 0x51: case 0x52: case 0x53:
            case 0x54: case 0x55: case 0x56: case 0x57:
            {
                cpu.Push32(cpu.GetRegister32(opcode - 0x50));
                return 2;
            }
            // POP r32 (0x58-0x5F)
            case 0x58: case 0x59: case 0x5A: case 0x5B:
            case 0x5C: case 0x5D: case 0x5E: case 0x5F:
            {
                cpu.SetRegister32(opcode - 0x58, cpu.Pop32());
                return 2;
            }
            // INC r32 (0x40-0x47)
            case 0x40: case 0x41: case 0x42: case 0x43:
            case 0x44: case 0x45: case 0x46: case 0x47:
            {
                int reg = opcode - 0x40;
                uint val = cpu.GetRegister32(reg);
                uint result = val + 1;
                cpu.SetRegister32(reg, result);
                cpu.Flags.ZF = result == 0;
                cpu.Flags.SF = (result & 0x80000000) != 0;
                cpu.Flags.OF = val == 0x7FFFFFFF;
                return 2;
            }
            // DEC r32 (0x48-0x4F)
            case 0x48: case 0x49: case 0x4A: case 0x4B:
            case 0x4C: case 0x4D: case 0x4E: case 0x4F:
            {
                int reg = opcode - 0x48;
                uint val = cpu.GetRegister32(reg);
                uint result = val - 1;
                cpu.SetRegister32(reg, result);
                cpu.Flags.ZF = result == 0;
                cpu.Flags.SF = (result & 0x80000000) != 0;
                cpu.Flags.OF = val == 0x80000000;
                return 2;
            }
            // MOV EAX, [addr]
            case 0xA1:
            {
                ushort offset = cpu.FetchWord();
                int seg = segOverride >= 0 ? segOverride : 3;
                int addr = V30.GetPhysicalAddress(cpu.GetSegmentRegister(seg), offset);
                cpu.EAX = (uint)(bus.ReadMemoryByte(addr) |
                                  (bus.ReadMemoryByte(addr + 1) << 8) |
                                  (bus.ReadMemoryByte(addr + 2) << 16) |
                                  (bus.ReadMemoryByte(addr + 3) << 24));
                return 4;
            }
            // MOV [addr], EAX
            case 0xA3:
            {
                ushort offset = cpu.FetchWord();
                int seg = segOverride >= 0 ? segOverride : 3;
                int addr = V30.GetPhysicalAddress(cpu.GetSegmentRegister(seg), offset);
                bus.WriteMemoryByte(addr, (byte)cpu.EAX);
                bus.WriteMemoryByte(addr + 1, (byte)(cpu.EAX >> 8));
                bus.WriteMemoryByte(addr + 2, (byte)(cpu.EAX >> 16));
                bus.WriteMemoryByte(addr + 3, (byte)(cpu.EAX >> 24));
                return 4;
            }
            // PUSH imm32
            case 0x68:
            {
                uint imm = cpu.FetchDWord();
                cpu.Push32(imm);
                return 3;
            }
            // ADD/OR/ADC/SBB/AND/SUB/XOR/CMP r/m32, imm32 (Group 1)
            case 0x81:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                uint rm = cpu.ReadModRM32();
                uint imm = cpu.FetchDWord();
                uint result = 0;
                switch (cpu.ModRM.Reg)
                {
                    case 0: // ADD
                        result = rm + imm;
                        cpu.Flags.CF = (ulong)rm + imm > 0xFFFFFFFF;
                        cpu.WriteModRM32(result);
                        break;
                    case 1: // OR
                        result = rm | imm;
                        cpu.Flags.CF = false; cpu.Flags.OF = false;
                        cpu.WriteModRM32(result);
                        break;
                    case 4: // AND
                        result = rm & imm;
                        cpu.Flags.CF = false; cpu.Flags.OF = false;
                        cpu.WriteModRM32(result);
                        break;
                    case 5: // SUB
                        result = rm - imm;
                        cpu.Flags.CF = rm < imm;
                        cpu.WriteModRM32(result);
                        break;
                    case 6: // XOR
                        result = rm ^ imm;
                        cpu.Flags.CF = false; cpu.Flags.OF = false;
                        cpu.WriteModRM32(result);
                        break;
                    case 7: // CMP
                        result = rm - imm;
                        cpu.Flags.CF = rm < imm;
                        cpu.Flags.OF = ((rm ^ imm) & (rm ^ result) & 0x80000000) != 0;
                        break;
                    default:
                        cpu.WriteModRM32(rm); // no-op for unhandled
                        break;
                }
                cpu.Flags.ZF = result == 0;
                cpu.Flags.SF = (result & 0x80000000) != 0;
                return 3;
            }
            // Group 1 r/m32, imm8 (sign-extended)
            case 0x83:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                uint rm = cpu.ReadModRM32();
                uint imm = (uint)(int)(sbyte)cpu.FetchByte();
                uint result = 0;
                switch (cpu.ModRM.Reg)
                {
                    case 0: result = rm + imm; cpu.Flags.CF = (ulong)rm + imm > 0xFFFFFFFF; cpu.WriteModRM32(result); break;
                    case 1: result = rm | imm; cpu.Flags.CF = false; cpu.WriteModRM32(result); break;
                    case 4: result = rm & imm; cpu.Flags.CF = false; cpu.WriteModRM32(result); break;
                    case 5: result = rm - imm; cpu.Flags.CF = rm < imm; cpu.WriteModRM32(result); break;
                    case 6: result = rm ^ imm; cpu.Flags.CF = false; cpu.WriteModRM32(result); break;
                    case 7: result = rm - imm; cpu.Flags.CF = rm < imm; cpu.Flags.OF = ((rm ^ imm) & (rm ^ result) & 0x80000000) != 0; break;
                    default: cpu.WriteModRM32(rm); break;
                }
                cpu.Flags.ZF = result == 0;
                cpu.Flags.SF = (result & 0x80000000) != 0;
                return 3;
            }
            // SHL/SHR/SAR r/m32, imm8
            case 0xC1:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                uint val = cpu.ReadModRM32();
                byte count = (byte)(cpu.FetchByte() & 0x1F);
                uint result = val;
                switch (cpu.ModRM.Reg)
                {
                    case 4: // SHL
                        if (count > 0) { cpu.Flags.CF = ((val >> (32 - count)) & 1) != 0; result = val << count; }
                        break;
                    case 5: // SHR
                        if (count > 0) { cpu.Flags.CF = ((val >> (count - 1)) & 1) != 0; result = val >> count; }
                        break;
                    case 7: // SAR
                        if (count > 0) { cpu.Flags.CF = (((int)val >> (count - 1)) & 1) != 0; result = (uint)((int)val >> count); }
                        break;
                    case 0: // ROL
                        if (count > 0) { result = (val << count) | (val >> (32 - count)); cpu.Flags.CF = (result & 1) != 0; }
                        break;
                    case 1: // ROR
                        if (count > 0) { result = (val >> count) | (val << (32 - count)); cpu.Flags.CF = (result & 0x80000000) != 0; }
                        break;
                }
                cpu.WriteModRM32(result);
                cpu.Flags.ZF = result == 0;
                cpu.Flags.SF = (result & 0x80000000) != 0;
                return 5;
            }
            // MOV r/m32, imm32
            case 0xC7:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                uint imm = cpu.FetchDWord();
                cpu.WriteModRM32(imm);
                return 3;
            }
            // XCHG EAX, r32 (0x91-0x97) + NOP (0x90)
            case 0x90: return 1; // NOP
            case 0x91: case 0x92: case 0x93:
            case 0x94: case 0x95: case 0x96: case 0x97:
            {
                int reg = opcode - 0x90;
                uint tmp = cpu.EAX;
                cpu.EAX = cpu.GetRegister32(reg);
                cpu.SetRegister32(reg, tmp);
                return 3;
            }
            // MOVS dword
            case 0xA5:
            {
                int srcSeg = segOverride >= 0 ? segOverride : 3;
                int srcAddr = V30.GetPhysicalAddress(cpu.GetSegmentRegister(srcSeg), cpu.SI);
                int dstAddr = V30.GetPhysicalAddress(cpu.ES, cpu.DI);
                for (int i = 0; i < 4; i++)
                    bus.WriteMemoryByte(dstAddr + i, bus.ReadMemoryByte(srcAddr + i));
                cpu.SI = (ushort)(cpu.Flags.DF ? cpu.SI - 4 : cpu.SI + 4);
                cpu.DI = (ushort)(cpu.Flags.DF ? cpu.DI - 4 : cpu.DI + 4);
                return 5;
            }
            // STOS dword
            case 0xAB:
            {
                int dstAddr = V30.GetPhysicalAddress(cpu.ES, cpu.DI);
                bus.WriteMemoryByte(dstAddr, (byte)cpu.EAX);
                bus.WriteMemoryByte(dstAddr + 1, (byte)(cpu.EAX >> 8));
                bus.WriteMemoryByte(dstAddr + 2, (byte)(cpu.EAX >> 16));
                bus.WriteMemoryByte(dstAddr + 3, (byte)(cpu.EAX >> 24));
                cpu.DI = (ushort)(cpu.Flags.DF ? cpu.DI - 4 : cpu.DI + 4);
                return 4;
            }
            // PUSHFD
            case 0x9C:
            {
                cpu.Push32((uint)cpu.Flags.Value);
                return 4;
            }
            // POPFD
            case 0x9D:
            {
                cpu.Flags.Value = (ushort)cpu.Pop32();
                return 5;
            }
            // LEA r32, m (same encoding as 16-bit)
            case 0x8D:
            {
                byte modrm = cpu.FetchByte();
                cpu.DecodeModRM16(modrm);
                cpu.SetRegister32(cpu.ModRM.Reg, (uint)(ushort)cpu.ModRMOffset);
                return 2;
            }
            // 0x0F two-byte opcodes with 32-bit operand size
            case 0x0F:
            {
                byte op2 = cpu.FetchByte();
                return ExecuteTwoByteOpcode(cpu, bus, op2);
            }

            default:
                // Fall back to 16-bit execution for unhandled 0x66 prefixed opcodes
                cpu.OperandSize32 = false;
                return Execute(cpu, bus, opcode, false, false, segOverride);
        }
    }

    #endregion
}
