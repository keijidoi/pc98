namespace PC98Emu;

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: PC98Emu <disk_image>");
            Console.WriteLine("Supported formats: .d88, .fdi, .hdi, .nhd, .nfd");
            return;
        }

        string imagePath = args[0];
        if (!File.Exists(imagePath))
        {
            Console.WriteLine($"Error: File not found: {imagePath}");
            return;
        }

        var emu = new Emulator();
        byte[] imageData = File.ReadAllBytes(imagePath);

        string ext = Path.GetExtension(imagePath).ToLowerInvariant();
        // For now, load as floppy drive 0 (all formats)
        emu.LoadFloppyDisk(0, imageData);

        emu.Initialize();
        emu.Boot();
        emu.Run();
    }
}
