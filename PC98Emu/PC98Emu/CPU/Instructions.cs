using PC98Emu.Bus;

namespace PC98Emu.CPU;

public static class Instructions
{
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
                cpu.Interrupt(vector);
                return 51;
            }
            case 0xCF: // IRET
            {
                cpu.IP = cpu.Pop();
                cpu.CS = cpu.Pop();
                cpu.Flags.Value = cpu.Pop();
                return 32;
            }
            #endregion

            #region Shift/Rotate Group 2: 0xD0-0xD3
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
                        if (val == 0) { cpu.Interrupt(0); return 1; }
                        ushort num = cpu.AX;
                        int quot = num / val;
                        int rem = num % val;
                        if (quot > 0xFF) { cpu.Interrupt(0); return 1; }
                        cpu.AL = (byte)quot;
                        cpu.AH = (byte)rem;
                        break;
                    }
                    case 7: // IDIV r/m8
                    {
                        byte val = cpu.ReadModRM8();
                        if (val == 0) { cpu.Interrupt(0); return 1; }
                        short num = (short)cpu.AX;
                        int quot = num / (sbyte)val;
                        int rem = num % (sbyte)val;
                        if (quot > 127 || quot < -128) { cpu.Interrupt(0); return 1; }
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
                        if (val == 0) { cpu.Interrupt(0); return 1; }
                        uint num = (uint)((cpu.DX << 16) | cpu.AX);
                        uint quot = num / val;
                        uint rem = num % val;
                        if (quot > 0xFFFF) { cpu.Interrupt(0); return 1; }
                        cpu.AX = (ushort)quot;
                        cpu.DX = (ushort)rem;
                        break;
                    }
                    case 7: // IDIV r/m16
                    {
                        ushort val = cpu.ReadModRM16();
                        if (val == 0) { cpu.Interrupt(0); return 1; }
                        int num = (int)((cpu.DX << 16) | cpu.AX);
                        int quot = num / (short)val;
                        int rem = num % (short)val;
                        if (quot > 32767 || quot < -32768) { cpu.Interrupt(0); return 1; }
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

            default:
                return 1; // Unimplemented: skip silently
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
}
