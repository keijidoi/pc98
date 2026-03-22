namespace PC98Emu.Disk;

/// <summary>
/// Read-only FAT16 filesystem reader for PC-98 disk images.
/// Supports 256-byte sector sizes (PC-98 SASI/SCSI).
/// </summary>
public class Fat16Reader
{
    private readonly IDiskImage _disk;
    private readonly int _partitionLba; // LBA of partition start (in physical sectors)

    // BPB parameters
    private int _bytesPerSector;
    private int _sectorsPerCluster;
    private int _reservedSectors;
    private int _numFats;
    private int _rootEntries;
    private int _totalSectors;
    private int _sectorsPerFat;

    // Derived
    private int _fatStartLba;
    private int _rootDirStartLba;
    private int _dataStartLba;
    private int _rootDirSectors;

    // Cached FAT table
    private ushort[]? _fat;
    private bool _isFat12;

    public bool IsInitialized { get; private set; }

    public Fat16Reader(IDiskImage disk, int partitionLba)
    {
        _disk = disk;
        _partitionLba = partitionLba;
    }

    public bool Initialize()
    {
        // Bootstrap: read PBR using physical sectors first (before BPB is known)
        byte[]? pbrPhys = ReadPhysicalSector(_partitionLba);
        if (pbrPhys == null) return false;

        _bytesPerSector = pbrPhys[0x0B] | (pbrPhys[0x0C] << 8);
        _sectorsPerCluster = pbrPhys[0x0D];
        _reservedSectors = pbrPhys[0x0E] | (pbrPhys[0x0F] << 8);
        _numFats = pbrPhys[0x10];
        _rootEntries = pbrPhys[0x11] | (pbrPhys[0x12] << 8);
        _totalSectors = pbrPhys[0x13] | (pbrPhys[0x14] << 8);
        if (_totalSectors == 0)
            _totalSectors = pbrPhys[0x20] | (pbrPhys[0x21] << 8) | (pbrPhys[0x22] << 16) | (pbrPhys[0x23] << 24);
        _sectorsPerFat = pbrPhys[0x16] | (pbrPhys[0x17] << 8);

        if (_bytesPerSector == 0 || _sectorsPerCluster == 0 || _numFats == 0)
        {
            Console.Error.WriteLine("[FAT16] Invalid BPB parameters");
            return false;
        }

        _physPerLogical = _bytesPerSector / _disk.SectorSize;
        if (_physPerLogical < 1) _physPerLogical = 1;

        // Convert partition LBA from physical to logical sectors
        int partLogical = _partitionLba / _physPerLogical;
        _fatStartLba = partLogical + _reservedSectors;
        _rootDirSectors = (_rootEntries * 32 + _bytesPerSector - 1) / _bytesPerSector;
        _rootDirStartLba = _fatStartLba + _numFats * _sectorsPerFat;
        _dataStartLba = _rootDirStartLba + _rootDirSectors;

        // Determine FAT type by cluster count
        int dataSectors = _totalSectors - _reservedSectors - _numFats * _sectorsPerFat - _rootDirSectors;
        int totalClusters = dataSectors / _sectorsPerCluster;
        _isFat12 = totalClusters < 4085;

        Console.Error.WriteLine($"[FAT] BPB: bps={_bytesPerSector} spc={_sectorsPerCluster} reserved={_reservedSectors} fats={_numFats} rootEntries={_rootEntries} total={_totalSectors} spf={_sectorsPerFat}");
        Console.Error.WriteLine($"[FAT] Layout: fatStart={_fatStartLba} rootDir={_rootDirStartLba} dataStart={_dataStartLba} clusters={totalClusters} type={(_isFat12 ? "FAT12" : "FAT16")}");

        // Read FAT table
        _fat = ReadFat();
        if (_fat == null) return false;

        IsInitialized = true;
        return true;
    }

    /// <summary>
    /// Find a file by path (supports subdirectories, e.g., "MAKYO\MAKYO.EXE").
    /// Returns (startCluster, fileSize) or (0, 0) if not found.
    /// </summary>
    public (ushort cluster, uint size) FindFile(string filename)
    {
        if (!IsInitialized) return (0, 0);

        // Strip drive letter (e.g., "B:\MAKYO\GAME.EXE" → "MAKYO\GAME.EXE")
        string path = filename;
        if (path.Length >= 2 && path[1] == ':')
            path = path.Substring(2);
        path = path.TrimStart('\\', '/');

        // Split into directory components and final filename
        string[] parts = path.Split(new[] { '\\', '/' });

        if (parts.Length == 1)
        {
            // Simple filename — search root directory
            return FindFileInRootDir(parts[0], skipDirs: true);
        }

        // Walk subdirectories
        ushort dirCluster = 0; // 0 = root directory
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var (cl, _) = dirCluster == 0
                ? FindFileInRootDir(parts[i], skipDirs: false, dirsOnly: true)
                : FindFileInSubDir(dirCluster, parts[i], skipDirs: false, dirsOnly: true);
            if (cl == 0) return (0, 0); // Directory not found
            dirCluster = cl;
        }

        // Search the final filename in the target subdirectory
        return FindFileInSubDir(dirCluster, parts[^1], skipDirs: true);
    }

    /// <summary>
    /// Resolve a directory path (e.g., "MAKYO" or "MAKYO\SUB") to its starting cluster.
    /// Returns 0 for root directory, or the cluster number for subdirectories.
    /// Returns -1 if the directory is not found.
    /// </summary>
    public int ResolveDirPath(string dirPath)
    {
        if (!IsInitialized || string.IsNullOrEmpty(dirPath)) return 0; // root

        string path = dirPath.Replace('/', '\\').Trim('\\');
        if (path.Length == 0) return 0;

        string[] parts = path.Split('\\');
        ushort dirCluster = 0;
        for (int i = 0; i < parts.Length; i++)
        {
            var (cl, _) = dirCluster == 0
                ? FindFileInRootDir(parts[i], skipDirs: false, dirsOnly: true)
                : FindFileInSubDir(dirCluster, parts[i], skipDirs: false, dirsOnly: true);
            if (cl == 0) return -1; // not found
            dirCluster = cl;
        }
        return dirCluster;
    }

    /// <summary>
    /// Find a directory entry in the root directory.
    /// </summary>
    private (ushort cluster, uint size) FindFileInRootDir(string filename, bool skipDirs, bool dirsOnly = false)
    {
        string name83 = To83Name(filename);
        if (name83 == null) return (0, 0);

        for (int sec = 0; sec < _rootDirSectors; sec++)
        {
            byte[] data = ReadLogicalSector(_rootDirStartLba + sec);
            if (data == null) continue;

            int entriesPerSector = _bytesPerSector / 32;
            for (int e = 0; e < entriesPerSector; e++)
            {
                int off = e * 32;
                if (data[off] == 0x00) return (0, 0);
                if (data[off] == 0xE5) continue;
                byte attr = data[off + 0x0B];
                if ((attr & 0x08) != 0) continue; // Volume label
                bool isDir = (attr & 0x10) != 0;
                if (skipDirs && isDir) continue;
                if (dirsOnly && !isDir) continue;

                string entryName = "";
                for (int i = 0; i < 11; i++)
                    entryName += (char)data[off + i];

                if (entryName == name83)
                {
                    ushort cluster = (ushort)(data[off + 0x1A] | (data[off + 0x1B] << 8));
                    uint size = (uint)(data[off + 0x1C] | (data[off + 0x1D] << 8) |
                                       (data[off + 0x1E] << 16) | (data[off + 0x1F] << 24));
                    Console.Error.WriteLine($"[FAT16] Found '{entryName.Trim()}' cluster={cluster} size={size} dir={isDir}");
                    return (cluster, size);
                }
            }
        }
        return (0, 0);
    }

    /// <summary>
    /// Find a file/directory in a subdirectory given its start cluster.
    /// </summary>
    private (ushort cluster, uint size) FindFileInSubDir(ushort dirCluster, string filename, bool skipDirs, bool dirsOnly = false)
    {
        string name83 = To83Name(filename);
        if (name83 == null || _fat == null) return (0, 0);

        Console.Error.WriteLine($"[FAT16] Searching subdir cluster={dirCluster} for '{name83}' (skipDirs={skipDirs} dirsOnly={dirsOnly})");

        int eofMarker = _isFat12 ? 0xFF8 : 0xFFF8;
        ushort cl = dirCluster;

        while (cl >= 2 && cl < eofMarker)
        {
            int lba = _dataStartLba + (cl - 2) * _sectorsPerCluster;
            for (int s = 0; s < _sectorsPerCluster; s++)
            {
                byte[] data = ReadLogicalSector(lba + s);
                if (data == null) { Console.Error.WriteLine($"[FAT16] ReadLogicalSector({lba + s}) returned null"); continue; }

                int entriesPerSector = _bytesPerSector / 32;
                for (int e = 0; e < entriesPerSector; e++)
                {
                    int off = e * 32;
                    if (data[off] == 0x00) { Console.Error.WriteLine($"[FAT16] End of subdir at entry {e}"); return (0, 0); }
                    if (data[off] == 0xE5) continue;
                    byte attr = data[off + 0x0B];
                    if ((attr & 0x08) != 0) continue;
                    bool isDir = (attr & 0x10) != 0;

                    string entryName = "";
                    for (int i = 0; i < 11; i++)
                        entryName += (char)data[off + i];
                    ushort eCl = (ushort)(data[off + 0x1A] | (data[off + 0x1B] << 8));
                    uint eSz = (uint)(data[off + 0x1C] | (data[off + 0x1D] << 8) |
                                      (data[off + 0x1E] << 16) | (data[off + 0x1F] << 24));
                    Console.Error.WriteLine($"[FAT16] SubDir entry: '{entryName}' attr={attr:X2} cluster={eCl} size={eSz}");

                    if (skipDirs && isDir) continue;
                    if (dirsOnly && !isDir) continue;

                    if (entryName == name83)
                    {
                        Console.Error.WriteLine($"[FAT16] Found '{entryName.Trim()}' in subdir cluster={eCl} size={eSz} dir={isDir}");
                        return (eCl, eSz);
                    }
                }
            }
            cl = _fat[cl];
        }
        Console.Error.WriteLine($"[FAT16] Not found in subdir: '{name83}'");
        return (0, 0);
    }

    /// <summary>
    /// Get directory entries from a subdirectory given its start cluster.
    /// </summary>
    public List<byte[]> GetSubDirEntries(ushort dirCluster)
    {
        var entries = new List<byte[]>();
        if (!IsInitialized || _fat == null) return entries;

        int eofMarker = _isFat12 ? 0xFF8 : 0xFFF8;
        ushort cl = dirCluster;

        while (cl >= 2 && cl < eofMarker)
        {
            int lba = _dataStartLba + (cl - 2) * _sectorsPerCluster;
            for (int s = 0; s < _sectorsPerCluster; s++)
            {
                byte[] data = ReadLogicalSector(lba + s);
                if (data == null) continue;

                int entriesPerSector = _bytesPerSector / 32;
                for (int e = 0; e < entriesPerSector; e++)
                {
                    int off = e * 32;
                    if (data[off] == 0x00) return entries;
                    if (data[off] == 0xE5) continue;
                    var entry = new byte[32];
                    Array.Copy(data, off, entry, 0, 32);
                    entries.Add(entry);
                }
            }
            cl = _fat[cl];
        }
        return entries;
    }

    /// <summary>
    /// Read a file's entire content given its start cluster.
    /// </summary>
    public byte[]? ReadFile(ushort startCluster, uint fileSize)
    {
        if (!IsInitialized || _fat == null || startCluster < 2) return null;

        byte[] result = new byte[fileSize];
        int bytesRead = 0;
        ushort cluster = startCluster;
        int clusterBytes = _sectorsPerCluster * _bytesPerSector;

        int eofMarker = _isFat12 ? 0xFF8 : 0xFFF8;
        while (cluster >= 2 && cluster < eofMarker && bytesRead < fileSize)
        {
            int lba = _dataStartLba + (cluster - 2) * _sectorsPerCluster;
            for (int s = 0; s < _sectorsPerCluster && bytesRead < fileSize; s++)
            {
                byte[] sector = ReadLogicalSector(lba + s);
                if (sector == null)
                {
                    Console.Error.WriteLine($"[FAT16] ReadFile failed: cluster={cluster} lba={lba + s} physLba={( lba + s) * _physPerLogical}");
                    return null;
                }
                int toCopy = Math.Min(_bytesPerSector, (int)(fileSize - bytesRead));
                Array.Copy(sector, 0, result, bytesRead, toCopy);
                bytesRead += toCopy;
            }
            cluster = _fat[cluster];
        }

        return result;
    }

    /// <summary>
    /// List files in root directory (for debugging).
    /// </summary>
    public void ListRootDir()
    {
        if (!IsInitialized) return;
        Console.Error.WriteLine("[FAT16] Root directory:");
        for (int sec = 0; sec < _rootDirSectors; sec++)
        {
            byte[] data = ReadLogicalSector(_rootDirStartLba + sec);
            if (data == null) continue;
            int entriesPerSector = _bytesPerSector / 32;
            for (int e = 0; e < entriesPerSector; e++)
            {
                int off = e * 32;
                if (data[off] == 0x00) return;
                if (data[off] == 0xE5) continue;
                string name = "";
                for (int i = 0; i < 11; i++) name += (char)data[off + i];
                byte attr = data[off + 0x0B];
                ushort cluster = (ushort)(data[off + 0x1A] | (data[off + 0x1B] << 8));
                uint size = (uint)(data[off + 0x1C] | (data[off + 0x1D] << 8) |
                                   (data[off + 0x1E] << 16) | (data[off + 0x1F] << 24));
                Console.Error.WriteLine($"  {name} attr={attr:X2} cluster={cluster} size={size}");
            }
        }
    }

    /// <summary>
    /// Get raw 32-byte directory entries from root directory.
    /// Each entry is a 32-byte array. Skips deleted (0xE5) entries. Stops at end marker (0x00).
    /// </summary>
    public List<byte[]> GetRootDirEntries()
    {
        var entries = new List<byte[]>();
        if (!IsInitialized) return entries;
        for (int sec = 0; sec < _rootDirSectors; sec++)
        {
            byte[] data = ReadLogicalSector(_rootDirStartLba + sec);
            if (data == null) continue;
            int entriesPerSector = _bytesPerSector / 32;
            for (int e = 0; e < entriesPerSector; e++)
            {
                int off = e * 32;
                if (data[off] == 0x00) return entries; // End of directory
                if (data[off] == 0xE5) continue; // Deleted
                var entry = new byte[32];
                Array.Copy(data, off, entry, 0, 32);
                entries.Add(entry);
            }
        }
        return entries;
    }

    private ushort[]? ReadFat()
    {
        int fatBytes = _sectorsPerFat * _bytesPerSector;
        byte[] fatRaw = new byte[fatBytes];
        for (int s = 0; s < _sectorsPerFat; s++)
        {
            byte[] sector = ReadLogicalSector(_fatStartLba + s);
            if (sector == null) return null;
            Array.Copy(sector, 0, fatRaw, s * _bytesPerSector, _bytesPerSector);
        }

        ushort[] fat;
        if (_isFat12)
        {
            // FAT12: each entry is 12 bits (1.5 bytes)
            int entries = fatBytes * 2 / 3; // approximate max entries
            fat = new ushort[entries];
            for (int i = 0; i < entries; i++)
            {
                int byteOff = i * 3 / 2;
                if (byteOff + 1 >= fatBytes) break;
                if ((i & 1) == 0)
                {
                    // Even entry: low 8 bits of byte[n] + low 4 bits of byte[n+1]
                    fat[i] = (ushort)(fatRaw[byteOff] | ((fatRaw[byteOff + 1] & 0x0F) << 8));
                }
                else
                {
                    // Odd entry: high 4 bits of byte[n] + all 8 bits of byte[n+1]
                    fat[i] = (ushort)((fatRaw[byteOff] >> 4) | (fatRaw[byteOff + 1] << 4));
                }
            }
        }
        else
        {
            int entries = fatBytes / 2;
            fat = new ushort[entries];
            for (int i = 0; i < entries; i++)
                fat[i] = (ushort)(fatRaw[i * 2] | (fatRaw[i * 2 + 1] << 8));
        }

        // Debug: dump first 32 FAT entries
        Console.Error.Write($"[FAT] FAT[0..31]: ");
        for (int i = 0; i < 32 && i < fat.Length; i++)
            Console.Error.Write($"{fat[i]:X3} ");
        Console.Error.WriteLine();

        return fat;
    }

    // Number of physical sectors per BPB logical sector
    private int _physPerLogical;

    /// <summary>
    /// Read one BPB logical sector at the given logical LBA.
    /// Handles the case where BPB sector size > physical sector size
    /// (e.g., BPB=1024 bytes, physical=256 bytes → reads 4 physical sectors).
    /// </summary>
    private byte[]? ReadLogicalSector(int logicalLba)
    {
        byte[] result = new byte[_bytesPerSector];
        int physLba = logicalLba * _physPerLogical;

        for (int i = 0; i < _physPerLogical; i++)
        {
            int pLba = physLba + i;
            int spt = _disk.SectorsPerTrack;
            int heads = _disk.Heads;
            int cylinder = pLba / (spt * heads);
            int rem = pLba % (spt * heads);
            int head = rem / spt;
            int sector = (rem % spt) + 1; // 1-based

            byte[] buffer = new byte[_disk.SectorSize];
            if (!_disk.ReadSector(cylinder, head, sector, buffer))
                return null;
            Array.Copy(buffer, 0, result, i * _disk.SectorSize, _disk.SectorSize);
        }
        return result;
    }

    /// <summary>
    /// Read one physical sector at the given physical LBA.
    /// </summary>
    private byte[]? ReadPhysicalSector(int physLba)
    {
        int spt = _disk.SectorsPerTrack;
        int heads = _disk.Heads;
        int cylinder = physLba / (spt * heads);
        int rem = physLba % (spt * heads);
        int head = rem / spt;
        int sector = (rem % spt) + 1;

        byte[] buffer = new byte[_disk.SectorSize];
        if (!_disk.ReadSector(cylinder, head, sector, buffer))
            return null;
        return buffer;
    }

    private static string? To83Name(string path)
    {
        // Strip drive letter and path
        string name = path;
        int lastSlash = name.LastIndexOfAny(new[] { '\\', '/' });
        if (lastSlash >= 0) name = name.Substring(lastSlash + 1);

        // Split name.ext
        int dot = name.IndexOf('.');
        string basename, ext;
        if (dot >= 0)
        {
            basename = name.Substring(0, dot);
            ext = name.Substring(dot + 1);
        }
        else
        {
            basename = name;
            ext = "";
        }

        basename = basename.ToUpperInvariant().PadRight(8).Substring(0, 8);
        ext = ext.ToUpperInvariant().PadRight(3).Substring(0, 3);
        return basename + ext;
    }
}
