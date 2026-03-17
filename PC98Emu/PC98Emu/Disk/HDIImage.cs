using System;

namespace PC98Emu.Disk;

public class HDIImage : IDiskImage
{
    private readonly byte[] _data;
    private readonly int _headerSize;

    public int Cylinders { get; }
    public int Heads { get; }
    public int SectorsPerTrack { get; }
    public int SectorSize { get; }

    public HDIImage(byte[] data)
    {
        _data = data;

        // HDI header (4096 bytes):
        // 0x00: header size (4), 0x04: data size (4), 0x08: sector size (4),
        // 0x0C: surfaces/heads (4), 0x10: cylinders (4), 0x14: sectors per track (4)
        _headerSize = ReadInt32(0);
        SectorSize = ReadInt32(0x08);
        Heads = ReadInt32(0x0C);
        Cylinders = ReadInt32(0x10);
        SectorsPerTrack = ReadInt32(0x14);
    }

    public bool ReadSector(int cylinder, int head, int sector, byte[] buffer)
    {
        int lba = ((cylinder * Heads + head) * SectorsPerTrack) + (sector - 1);
        int offset = _headerSize + lba * SectorSize;
        if (offset + SectorSize > _data.Length) return false;
        Array.Copy(_data, offset, buffer, 0, Math.Min(SectorSize, buffer.Length));
        return true;
    }

    public bool WriteSector(int cylinder, int head, int sector, byte[] buffer)
    {
        int lba = ((cylinder * Heads + head) * SectorsPerTrack) + (sector - 1);
        int offset = _headerSize + lba * SectorSize;
        if (offset + SectorSize > _data.Length) return false;
        Array.Copy(buffer, 0, _data, offset, Math.Min(SectorSize, buffer.Length));
        return true;
    }

    private int ReadInt32(int offset)
    {
        return _data[offset] | (_data[offset + 1] << 8) | (_data[offset + 2] << 16) | (_data[offset + 3] << 24);
    }
}
