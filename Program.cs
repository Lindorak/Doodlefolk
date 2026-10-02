namespace Doodlefolk;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        AppPaths.Configure(args);
        // The self-test runs alongside your Doodlefolk, with its own data, and reports by exit code.
        if (args.Contains("--selftest"))
        {
            if (!args.Contains("--data"))
            {
                AppPaths.DataDir = Path.Combine(Path.GetTempPath(), "Doodlefolk-selftest");
                AppPaths.PicturesDir = Path.Combine(AppPaths.DataDir, "pictures");
            }
            // Start from nothing every time, but only ever wipe a folder the self-test made itself.
            string marker = Path.Combine(AppPaths.DataDir, ".doodlefolk-selftest");
            if (Directory.Exists(AppPaths.DataDir) && Directory.EnumerateFileSystemEntries(AppPaths.DataDir).Any())
            {
                if (!File.Exists(marker)) { Environment.Exit(2); return; }
                try { Directory.Delete(AppPaths.DataDir, true); } catch { }
            }
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(marker, "Made by Doodlefolk --selftest; safe to delete.");
            ApplicationConfiguration.Initialize();
            using var test = new App(args);
            Application.Run(test);
            Environment.Exit(test.SelfTestExitCode);
            return;
        }
        if (!args.Contains("--data")) Migration.FromOldName();
        using var mutex = new Mutex(true, "Doodlefolk.SingleInstance", out bool first);
        // Started by an install or an update: wait for the old copy to finish closing.
        if (!first && args.Contains("--wait"))
        {
            try { first = mutex.WaitOne(TimeSpan.FromSeconds(20)); }
            catch (AbandonedMutexException) { first = true; }
        }
        if (args.Contains("--uninstall"))
        {
            ApplicationConfiguration.Initialize();
            if (!first) { MessageBox.Show("Please quit Doodlefolk first (Studio → Settings → Quit), then try again.", "Doodlefolk"); return; }
            App.Uninstall();
            return;
        }
        if (!first) return;
        ApplicationConfiguration.Initialize();
        using var app = new App(args);
        Application.Run(app);
        World.Log("message loop ended");
    }
}
