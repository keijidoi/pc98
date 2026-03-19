using System;

namespace PC98Emu.Disk;

/// <summary>
/// Raw floppy disk image (no header). Common PC-98 2HD format:
/// 77 cylinders, 2 heads, 8 sectors/track, 1024 bytes/sector = 1261568 bytes.
/// </summary>
public class HDMImage : IDiskImage
{
    private readonly byte[] _data;

    public int Cylinders { get; }
    public int Heads { get; }
    public int SectorsPerTrack { get; }
    public int SectorSize { get; }

    public HDMImage(byte[] data)
    {
        _data = data;

        // Detect geometry from file size
        int totalBytes = data.Length;

        if (totalBytes == 1261568) // 77 * 2 * 8 * 1024 (2HD)
        {
            Cylinders = 77;
            Heads = 2;
            SectorsPerTrack = 8;
            SectorSize = 1024;
        }
        else if (totalBytes == 1228800) // 77 * 2 * 8 * 1024 (alternative) or 80 * 2 * 15 * 512
        {
            // IBM 1.2MB format
            Cylinders = 80;
            Heads = 2;
            SectorsPerTrack = 15;
            SectorSize = 512;
        }
        else if (totalBytes == 737280) // 80 * 2 * 9 * 512 (2DD 720KB)
        {
            Cylinders = 80;
            Heads = 2;
            SectorsPerTrack = 9;
            SectorSize = 512;
        }
        else if (totalBytes == 327680) // 40 * 2 * 8 * 512 (2D 320KB)
        {
            Cylinders = 40;
            Heads = 2;
            SectorsPerTrack = 8;
            SectorSize = 512;
        }
        else
        {
            // Default: assume 2HD
            Cylinders = 77;
            Heads = 2;
            SectorsPerTrack = 8;
            SectorSize = 1024;
        }
    }

    public bool ReadSector(int cylinder, int head, int sector, byte[] buffer)
    {
        int lba = ((cylinder * Heads + head) * SectorsPerTrack) + (sector - 1);
        int offset = lba * SectorSize;
        if (offset < 0 || offset + SectorSize > _data.Length) return false;
        Array.Copy(_data, offset, buffer, 0, Math.Min(SectorSize, buffer.Length));
        return true;
    }

    public bool WriteSector(int cylinder, int head, int sector, byte[] buffer)
    {
        int lba = ((cylinder * Heads + head) * SectorsPerTrack) + (sector - 1);
        int offset = lba * SectorSize;
        if (offset < 0 || offset + SectorSize > _data.Length) return false;
        Array.Copy(buffer, 0, _data, offset, Math.Min(SectorSize, buffer.Length));
        return true;
    }
}
