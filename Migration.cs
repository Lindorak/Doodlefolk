using Microsoft.Win32;

namespace Doodlefolk;

/// <summary>Doodlefolk used to be called StickFight. The first time Doodlefolk runs, it brings your figures, casts,
/// mods and settings across from %APPDATA%\StickFight (a copy: the old folder stays as it was, as a backup), and moves
/// the "start with Windows" entry over. Old photos stay in Pictures\StickFight.</summary>
static class Migration
{
    const string OldName = "StickFight";

    public static void FromOldName()
    {
        try
        {
            string oldDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), OldName);
            if (!Directory.Exists(AppPaths.DataDir) && File.Exists(Path.Combine(oldDir, "settings.json")))
            {
                CopyDir(oldDir, AppPaths.DataDir);
                File.WriteAllText(Path.Combine(AppPaths.DataDir, "moved-from-stickfight.txt"),
                    $"Copied from {oldDir} on {DateTime.Now:yyyy-MM-dd HH:mm} when StickFight became Doodlefolk. The old folder was left as it was.");
            }
            // The old "start with Windows" entry would launch the old program: drop it (the setting carries over, and
            // Doodlefolk writes its own entry the next time the setting is applied).
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (run?.GetValue(OldName) is string oldCmd && !File.Exists(oldCmd.Trim('"'))) run.DeleteValue(OldName, false);
            else if (run?.GetValue(OldName) != null) { run.DeleteValue(OldName, false); NeedsStartupEntry = true; }
        }
        catch { /* never stop the app starting over this */ }
    }

    /// <summary>The old version started with Windows: Doodlefolk should too.</summary>
    public static bool NeedsStartupEntry;

    static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var f in Directory.GetFiles(from))
        {
            string name = Path.GetFileName(f);
            if (name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(f, Path.Combine(to, name), false);
        }
        foreach (var d in Directory.GetDirectories(from))
            if (!Path.GetFileName(d).Equals("WebView2", StringComparison.OrdinalIgnoreCase)) CopyDir(d, Path.Combine(to, Path.GetFileName(d)));
    }
}
