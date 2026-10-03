using System.Text.Json;

namespace Doodlefolk.Headless;

static class Cli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length == 0 || args is ["--selftest"])
                return Checks.Run();
            if (args.Length is 2 or 4 && args[0] == "--replay" && (args.Length == 2 || args[2] == "--out"))
            {
                // Input size and tick bounds keep accidental/malformed replay files cheap to reject.
                var file = new FileInfo(args[1]);
                if (file.Length > 64 * 1024) throw new ArgumentException("Replay JSON must be at most 64 KiB.");
                var spec = ReplaySpec.Parse(File.ReadAllText(file.FullName));
                var json = JsonSerializer.Serialize(Replay.Run(spec), ReplaySpec.JsonOptions);
                if (args.Length == 4)
                {
                    // Never overwrite an existing report, source file, or the input replay.
                    using var fileOutput = new StreamWriter(new FileStream(args[3], FileMode.CreateNew, FileAccess.Write));
                    fileOutput.WriteLine(json);
                }
                output.WriteLine(json);
                return 0;
            }
            error.WriteLine("Usage: dotnet run --project tests/Doodlefolk.Headless -c Release -- [--selftest | --replay FILE [--out NEW_FILE]]");
            return 2;
        }
        catch (Exception e)
        {
            error.WriteLine($"ERROR {e.Message}");
            return 1;
        }
    }
}
