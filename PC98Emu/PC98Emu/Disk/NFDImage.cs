using System;
using System.Text;

namespace PC98Emu.Disk;

/// <summary>
/// Basic NFD disk image support (T98FDDIMAGE.R0 / R1 format).
/// Supports standard sectors only (no copy protection tracks).
/// </summary>
public class NFDImage : IDiskImage
{
    private readonly byte[] _data;
    private readonly int _headerSize;

    public int Cylinders { get; }
    public int Heads { get; }
    public int SectorsPerTrack { get; }
    public int SectorSize { get; }

    public NFDImage(byte[] data)
    {
        _data = data;

        // NFD header starts with "T98FDDIMAGE.R0\0" or "T98FDDIMAGE.R1\0"
        // R0 format: 0x110 (272) byte header with track table
        // For basic support, use fixed geometry and data offset

        // Detect revision
        bool isR1 = data.Length > 14 && data[13] == (byte)'R' && data[14] == (byte)'1';

        if (isR1)
        {
            // R1: header size at offset 0x10
            _headerSize = ReadInt32(0x10);
        }
        else
        {
            // R0: header is 0x110 (272) bytes + track entries
            // Standard header size for R0
            _headerSize = 0x110 + 164 * 16; // track table
        }

        // Default to standard 2HD geometry
        Cylinders = 77;
        Heads = 2;
        SectorsPerTrack = 8;
        SectorSize = 1024;

        // Try to detect from data size
        int dataSize = _data.Length - _headerSize;
        if (dataSize > 0)
        {
            // 2DD: 77*2*8*512
            if (dataSize == 77 * 2 * 8 * 512)
            {
                SectorSize = 512;
                SectorsPerTrack = 8;
            }
            // 2HD: 77*2*8*1024
            else if (dataSize == 77 * 2 * 8 * 1024)
            {
                SectorSize = 1024;
                SectorsPerTrack = 8;
            }
        }
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
