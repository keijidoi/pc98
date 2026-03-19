namespace PC98Emu;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: PC98Emu <disk_image> [additional_images...]");
            Console.WriteLine("Supported formats: .d88, .fdi, .hdi, .nhd, .nfd, .hdm");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --boot <drive>    Boot from specified drive (fd0, fd1, hd0) [default: auto]");
            Console.WriteLine("  --fd<N> <file>    Load floppy image into specific drive (e.g., --fd2 disk.hdm)");
            return;
        }

        var emu = new Emulator();
        int floppyDrive = 0;
        int hddDrive = 0;
        int bootDrive = -1; // auto

        // Parse args
        var files = new List<string>();
        var explicitFloppies = new Dictionary<int, string>(); // drive -> path
        var explicitHdds = new Dictionary<int, string>(); // drive -> path
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--boot" && i + 1 < args.Length)
            {
                string bd = args[++i].ToLowerInvariant();
                bootDrive = bd switch
                {
                    "fd0" => 0,
                    "fd1" => 1,
                    "fd2" => 2,
                    "fd3" => 3,
                    "hd0" => 0x80,
                    "hd1" => 0x81,
                    _ => 0
                };
            }
            else if (args[i].StartsWith("--fd") && args[i].Length == 5 && i + 1 < args.Length)
            {
                int driveNum = args[i][4] - '0';
                explicitFloppies[driveNum] = args[++i];
            }
            else if (args[i].StartsWith("--hd") && args[i].Length == 5 && i + 1 < args.Length)
            {
                int driveNum = args[i][4] - '0';
                explicitHdds[driveNum] = args[++i];
            }
            else
            {
                files.Add(args[i]);
            }
        }

        // Check if any HDD images are in the file list (to avoid incorrect floppy mirroring)
        bool hasHddInFiles = files.Any(f =>
        {
            string e = Path.GetExtension(f).ToLowerInvariant().TrimStart('.');
            return e is "hdi" or "nhd" or "nfd";
        });

        // Load explicitly assigned floppies
        foreach (var (driveNum, path) in explicitFloppies)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"Error: File not found: {path}");
                return;
            }
            byte[] data = File.ReadAllBytes(path);
            string ext = Path.GetExtension(path).ToLowerInvariant().TrimStart('.');
            Console.WriteLine($"Loading floppy drive {driveNum}: {Path.GetFileName(path)} ({ext})");
            emu.LoadFloppyDisk(driveNum, data, ext);
        }

        // Load explicitly assigned HDDs
        foreach (var (driveNum, path) in explicitHdds)
        {
            if (!File.Exists(path))
            {
                Console.WriteLine($"Error: File not found: {path}");
                return;
            }
            byte[] data = File.ReadAllBytes(path);
            string ext = Path.GetExtension(path).ToLowerInvariant().TrimStart('.');
            Console.WriteLine($"Loading HDD {driveNum}: {Path.GetFileName(path)} ({ext.ToUpper()})");
            emu.LoadHardDisk(driveNum, data, ext);
            if (driveNum >= hddDrive) hddDrive = driveNum + 1;
        }

        foreach (var filePath in files)
        {
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"Error: File not found: {filePath}");
                return;
            }

            byte[] data = File.ReadAllBytes(filePath);
            string ext = Path.GetExtension(filePath).ToLowerInvariant().TrimStart('.');

            switch (ext)
            {
                case "d88":
                case "d98":
                case "88d":
                case "98d":
                    Console.WriteLine($"Loading floppy drive {floppyDrive}: {Path.GetFileName(filePath)} (D88)");
                    emu.LoadFloppyDisk(floppyDrive++, data, ext);
                    break;
                case "hdm":
                case "tfd":
                case "xdf":
                case "dup":
                    Console.WriteLine($"Loading floppy drive {floppyDrive}: {Path.GetFileName(filePath)} (raw)");
                    emu.LoadFloppyDisk(floppyDrive++, data, ext);
                    break;
                case "fdi":
                    Console.WriteLine($"Loading floppy drive {floppyDrive}: {Path.GetFileName(filePath)} (FDI)");
                    emu.LoadFloppyDisk(floppyDrive++, data, ext);
                    break;
                case "hdi":
                case "nhd":
                case "nfd":
                    Console.WriteLine($"Loading HDD {hddDrive}: {Path.GetFileName(filePath)} ({ext.ToUpper()})");
                    emu.LoadHardDisk(hddDrive++, data, ext);
                    break;
                default:
                    Console.WriteLine($"Warning: Unknown format '{ext}' for {Path.GetFileName(filePath)}, trying as raw floppy");
                    emu.LoadFloppyDisk(floppyDrive++, data, "hdm");
                    break;
            }
        }

        emu.Initialize();

        // Auto-detect boot drive: prefer HDD if loaded, otherwise first floppy
        if (bootDrive < 0)
            bootDrive = hddDrive > 0 ? 0x80 : 0;

        emu.Boot(bootDrive);
        emu.Run();
    }
}
