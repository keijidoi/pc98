using PC98Emu.CPU;
using PC98Emu.Bus;

namespace PC98Emu.BIOS;

public class GraphicsBios
{
    private readonly V30 _cpu;
    private readonly SystemBus _bus;

    // PC-98 GVRAM planes: B, R, G, (E/I)
    // Each plane is 32KB at:
    // Plane 0 (Blue):  0xA8000-0xAFFFF
    // Plane 1 (Red):   0xB0000-0xB7FFF
    // Plane 2 (Green): 0xB8000-0xBFFFF
    private const int GVRAM_PLANE_B = 0xA8000;
    private const int GVRAM_PLANE_R = 0xB0000;
    private const int GVRAM_PLANE_G = 0xB8000;
    private const int SCREEN_WIDTH = 640;

    public GraphicsBios(V30 cpu, SystemBus bus)
    {
        _cpu = cpu;
        _bus = bus;
    }

    public void Handle()
    {
        byte func = _cpu.AH;
        switch (func)
        {
            case 0x40:
                SetPixel();
                break;
            case 0x41:
                GetPixel();
                break;
            default:
                _cpu.AH = 0;
                break;
        }
    }

    private void SetPixel()
    {
        int x = _cpu.BX;
        int y = _cpu.CX;
        byte color = _cpu.DL;

        int byteOffset = (y * (SCREEN_WIDTH / 8)) + (x / 8);
        int bitMask = 0x80 >> (x % 8);

        SetPlaneBit(GVRAM_PLANE_B, byteOffset, bitMask, (color & 0x01) != 0);
        SetPlaneBit(GVRAM_PLANE_R, byteOffset, bitMask, (color & 0x02) != 0);
        SetPlaneBit(GVRAM_PLANE_G, byteOffset, bitMask, (color & 0x04) != 0);
    }

    private void GetPixel()
    {
        int x = _cpu.BX;
        int y = _cpu.CX;

        int byteOffset = (y * (SCREEN_WIDTH / 8)) + (x / 8);
        int bitMask = 0x80 >> (x % 8);

        byte color = 0;
        if ((_bus.ReadMemoryByte(GVRAM_PLANE_B + byteOffset) & bitMask) != 0) color |= 0x01;
        if ((_bus.ReadMemoryByte(GVRAM_PLANE_R + byteOffset) & bitMask) != 0) color |= 0x02;
        if ((_bus.ReadMemoryByte(GVRAM_PLANE_G + byteOffset) & bitMask) != 0) color |= 0x04;

        _cpu.DL = color;
    }

    private void SetPlaneBit(int planeBase, int byteOffset, int bitMask, bool set)
    {
        int addr = planeBase + byteOffset;
        byte val = _bus.ReadMemoryByte(addr);
        if (set)
            val |= (byte)bitMask;
        else
            val &= (byte)~bitMask;
        _bus.WriteMemoryByte(addr, val);
    }
}
