using PC98Emu.Bus;

namespace PC98Emu.Graphics;

/// <summary>
/// PC-98 GRCG (Graphic Charger) emulation.
/// Port 0x7C: mode register (write)
/// Port 0x7E: tile register (write, sequential for planes 0-3)
///
/// Modes (bits 7,6 of port 0x7C):
///   0x00 = Off
///   0x40 = TDW (Tile Draw Write) — GVRAM write fills tile pattern
///   0x80 = TCR (Tile Compare Read) — GVRAM read compares with tile
///   0xC0 = RMW (Read-Modify-Write) — read-modify-write with tile
///
/// Bits 0-3 of port 0x7C: plane enable mask (1=affected by GRCG operations).
/// </summary>
public class GRCG : IDevice
{
    private readonly SystemBus _bus;

    public byte Mode { get; private set; }      // 0x7C value
    public bool Enabled => (Mode & 0xC0) != 0;
    public byte PlaneMask => (byte)(Mode & 0x0F); // which planes are affected

    // Tile pattern: one byte per plane (0=B, 1=R, 2=G, 3=I)
    public readonly byte[] Tile = new byte[4];
    private int _tileIndex; // cycles 0→1→2→3→0...

    public GRCG(SystemBus bus)
    {
        _bus = bus;
    }

    public byte ReadByte(int port) => 0xFF;

    public void WriteByte(int port, byte value)
    {
        switch (port)
        {
            case 0x7C:
                Mode = value;
                _tileIndex = 0; // Reset tile index on mode write
                break;
            case 0x7E:
                Tile[_tileIndex] = value;
                _tileIndex = (_tileIndex + 1) & 3;
                break;
        }
    }

    /// <summary>
    /// Handle GRCG-intercepted GVRAM write.
    /// In TDW mode: write tile pattern to all enabled planes at the given GVRAM offset.
    /// In RMW mode: for enabled planes, write tile; for disabled planes, write original data.
    /// </summary>
    public void WriteGvram(byte[] memory, int gvramOffset, byte writtenValue)
    {
        int modeType = Mode & 0xC0;

        // Plane base addresses
        int[] planeBase = { 0xA8000, 0xB0000, 0xB8000, 0xE0000 };

        for (int p = 0; p < 4; p++)
        {
            int addr = planeBase[p] + gvramOffset;
            if (addr >= memory.Length) continue;

            if ((PlaneMask & (1 << p)) != 0)
            {
                // This plane is affected by GRCG
                if (modeType == 0x40) // TDW
                {
                    // Write tile pattern where writtenValue has 1-bits
                    memory[addr] = (byte)((Tile[p] & writtenValue) | (~writtenValue & 0));
                    // TDW: writtenValue acts as mask. 1=write tile bit, 0=write 0
                    memory[addr] = (byte)(Tile[p] & writtenValue);
                }
                else if (modeType == 0xC0) // RMW
                {
                    // RMW: writtenValue=mask. 1-bits: write tile, 0-bits: keep original
                    byte orig = memory[addr];
                    memory[addr] = (byte)((Tile[p] & writtenValue) | (orig & ~writtenValue));
                }
            }
            // Disabled planes: no change (keep existing data)
        }
    }

    /// <summary>
    /// Handle GRCG-intercepted GVRAM read in TCR mode.
    /// Returns a byte where each bit is 1 if all enabled planes match the tile at that pixel.
    /// </summary>
    public byte ReadGvram(byte[] memory, int gvramOffset)
    {
        int[] planeBase = { 0xA8000, 0xB0000, 0xB8000, 0xE0000 };
        byte result = 0xFF; // Start with all bits matching

        for (int p = 0; p < 4; p++)
        {
            if ((PlaneMask & (1 << p)) == 0) continue; // Skip disabled planes

            int addr = planeBase[p] + gvramOffset;
            if (addr >= memory.Length) continue;

            byte planeData = memory[addr];
            // XOR with tile: matching bits become 0, non-matching become 1
            // Then invert: matching bits become 1
            result &= (byte)~(planeData ^ Tile[p]);
        }
        return result;
    }

    public ushort ReadWord(int port) => ReadByte(port);
    public void WriteWord(int port, ushort value) => WriteByte(port, (byte)value);
    public void Reset() { Mode = 0; _tileIndex = 0; Array.Clear(Tile); }
    public void Tick(int cycles) { }
    public int[] GetPortRange() => new[] { 0x7C, 0x7E };
}
