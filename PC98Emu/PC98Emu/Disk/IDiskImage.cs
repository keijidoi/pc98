namespace PC98Emu.Disk;

public interface IDiskImage
{
    bool ReadSector(int cylinder, int head, int sector, byte[] buffer);
    bool WriteSector(int cylinder, int head, int sector, byte[] buffer);
    int Cylinders { get; }
    int Heads { get; }
    int SectorsPerTrack { get; }
    int SectorSize { get; }
}
