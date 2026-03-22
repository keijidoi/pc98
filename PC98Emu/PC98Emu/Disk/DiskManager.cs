namespace PC98Emu.Disk;

public class DiskManager
{
    private readonly IDiskImage?[] _floppies = new IDiskImage?[4];
    private readonly IDiskImage?[] _hdds = new IDiskImage?[2];

    public void MountFloppy(int drive, IDiskImage image)
    {
        if (drive >= 0 && drive < _floppies.Length)
            _floppies[drive] = image;
    }

    public void MountHDD(int drive, IDiskImage image)
    {
        if (drive >= 0 && drive < _hdds.Length)
            _hdds[drive] = image;
    }

    public IDiskImage? GetFloppy(int drive)
    {
        return (drive >= 0 && drive < _floppies.Length) ? _floppies[drive] : null;
    }

    public IDiskImage? GetHDD(int drive)
    {
        return (drive >= 0 && drive < _hdds.Length) ? _hdds[drive] : null;
    }

    public void UnmountHDD(int drive)
    {
        if (drive >= 0 && drive < _hdds.Length)
            _hdds[drive] = null;
    }
}
