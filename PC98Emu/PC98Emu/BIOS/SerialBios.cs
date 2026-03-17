using PC98Emu.CPU;
using PC98Emu.Bus;

namespace PC98Emu.BIOS;

public class SerialBios
{
    private readonly V30 _cpu;
    private readonly SystemBus _bus;

    public SerialBios(V30 cpu, SystemBus bus)
    {
        _cpu = cpu;
        _bus = bus;
    }

    public void Handle()
    {
        // Serial BIOS is stubbed - not available
        _cpu.AH = 0x80;
    }
}
