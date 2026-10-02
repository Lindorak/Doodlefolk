using System.Diagnostics;

namespace Doodlefolk;

/// <summary>Where things live: settings, mods and casts in %APPDATA%\Doodlefolk; photos and clips in
/// Pictures\Doodlefolk. The self-test points both at a throwaway folder (--data) so it never touches yours.</summary>
static class AppPaths
{
    public static string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Doodlefolk");
    public static string PicturesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Doodlefolk");

    /// <summary>"--data dir": keep everything in that folder instead.</summary>
    public static void Configure(string[] args)
    {
        int i = Array.IndexOf(args, "--data");
        if (i < 0 || i + 1 >= args.Length) return;
        DataDir = Path.GetFullPath(args[i + 1]);
        PicturesDir = Path.Combine(DataDir, "pictures");
        Directory.CreateDirectory(DataDir);
    }
}

/// <summary>The app's clock. Normally real time; the self-test runs it faster.</summary>
sealed class SimClock
{
    readonly Stopwatch _sw = Stopwatch.StartNew();
    double _offset, _base;
    double _scale = 1;

    public double Scale
    {
        get => _scale;
        set { double now = Seconds; _base = _sw.Elapsed.TotalSeconds; _offset = now; _scale = value; }
    }

    double Seconds => _offset + (_sw.Elapsed.TotalSeconds - _base) * _scale;
    public TimeSpan Elapsed => TimeSpan.FromSeconds(Seconds);
}
