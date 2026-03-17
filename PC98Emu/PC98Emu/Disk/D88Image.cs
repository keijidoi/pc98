using System;

namespace PC98Emu.Disk;

public class D88Image : IDiskImage
{
    private readonly byte[] _data;
    private readonly int[] _trackOffsets = new int[164];

    public int Cylinders { get; private set; }
    public int Heads { get; private set; }
    public int SectorsPerTrack { get; private set; }
    public int SectorSize { get; private set; }

    public D88Image(byte[] data)
    {
        _data = data;

        // Parse header: disk size at 0x1C (4 bytes LE)
        // Track offsets start at 0x20, 164 entries of 4 bytes each
        for (int i = 0; i < 164; i++)
        {
            int off = 0x20 + i * 4;
            _trackOffsets[i] = _data[off] | (_data[off + 1] << 8) | (_data[off + 2] << 16) | (_data[off + 3] << 24);
        }

        // Detect geometry from track table
        // Count how many tracks have non-zero offsets
        int trackCount = 0;
        for (int i = 0; i < 164; i++)
        {
            if (_trackOffsets[i] != 0) trackCount++;
        }

        // Detect sectors per track and sector size from first track
        SectorsPerTrack = 8;
        SectorSize = 512;
        if (_trackOffsets[0] != 0)
        {
            int pos = _trackOffsets[0];
            if (pos + 16 <= _data.Length)
            {
                int sectorCount = _data[pos + 4] | (_data[pos + 5] << 8);
                int sizeCode = _data[pos + 3];
                SectorsPerTrack = sectorCount > 0 ? sectorCount : 8;
                SectorSize = 128 << sizeCode;
            }
        }

        // Default geometry
        Heads = 2;
        if (trackCount > 2)
            Cylinders = (trackCount + Heads - 1) / Heads;
        else
            Cylinders = 77; // Default for standard 2D/2DD/2HD
    }

    public bool ReadSector(int cylinder, int head, int sector, byte[] buffer)
    {
        int trackIndex = cylinder * Heads + head;
        if (trackIndex < 0 || trackIndex >= 164) return false;

        int offset = _trackOffsets[trackIndex];
        if (offset == 0) return false;

        // Iterate sector headers in this track to find matching C/H/R
        int pos = offset;
        while (pos + 16 <= _data.Length)
        {
            int c = _data[pos + 0];
            int h = _data[pos + 1];
            int r = _data[pos + 2];
            int dataSize = _data[pos + 14] | (_data[pos + 15] << 8);

            if (c == cylinder && h == head && r == sector)
            {
                int dataStart = pos + 16;
                int copyLen = Math.Min(dataSize, buffer.Length);
                if (dataStart + copyLen > _data.Length) return false;
                Array.Copy(_data, dataStart, buffer, 0, copyLen);
                return true;
            }

            // Move to next sector header
            pos += 16 + dataSize;
        }

        return false;
    }

    public bool WriteSector(int cylinder, int head, int sector, byte[] buffer)
    {
        int trackIndex = cylinder * Heads + head;
        if (trackIndex < 0 || trackIndex >= 164) return false;

        int offset = _trackOffsets[trackIndex];
        if (offset == 0) return false;

        int pos = offset;
        while (pos + 16 <= _data.Length)
        {
            int c = _data[pos + 0];
            int h = _data[pos + 1];
            int r = _data[pos + 2];
            int dataSize = _data[pos + 14] | (_data[pos + 15] << 8);

            if (c == cylinder && h == head && r == sector)
            {
                int dataStart = pos + 16;
                int copyLen = Math.Min(dataSize, buffer.Length);
                if (dataStart + copyLen > _data.Length) return false;
                Array.Copy(buffer, 0, _data, dataStart, copyLen);
                return true;
            }

            pos += 16 + dataSize;
        }

        return false;
    }
}
