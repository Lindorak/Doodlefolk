using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Microsoft.Win32;

namespace Doodlefolk;

/// <summary>Installing, starting with Windows, updating, and saving battery.
/// Install copies Doodlefolk to your user programs folder (no admin needed), adds it to the Start menu and to
/// "Installed apps" (so it can be removed the usual way); your figures and settings stay where they are.
/// Updates: once a day it asks GitHub whether there's a newer release; installing one is always your click. The new
/// version is downloaded over HTTPS from the project's own GitHub releases, checked against the size GitHub lists,
/// swapped in, and Doodlefolk restarts.
/// Battery saver: on battery (or always, if you choose), a lighter frame rate and simpler graphics.</summary>
sealed partial class App
{
    const string Repo = "Lindorak/Doodlefolk";
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Doodlefolk";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Doodlefolk");
    static string InstalledExe => Path.Combine(InstallDir, "Doodlefolk.exe");
    static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Doodlefolk.lnk");
    static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;
    public static bool IsInstalled => string.Equals(Path.GetFullPath(ExePath), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);
    public static Version AppVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);
    static string VersionText => $"{AppVersion.Major}.{AppVersion.Minor}.{AppVersion.Build}";

    // ---------------- install / uninstall ----------------

    public string Install()
    {
        if (IsInstalled) return "Already installed.";
        try
        {
            Directory.CreateDirectory(InstallDir);
            File.Copy(ExePath, InstalledExe, true);
            MakeShortcut(StartMenuLink, InstalledExe);
            using (var k = Registry.CurrentUser.CreateSubKey(UninstallKey))
            {
                k.SetValue("DisplayName", "Doodlefolk");
                k.SetValue("DisplayVersion", VersionText);
                k.SetValue("Publisher", "Doodlefolk");
                k.SetValue("DisplayIcon", InstalledExe);
                k.SetValue("InstallLocation", InstallDir);
                k.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
                k.SetValue("URLInfoAbout", $"https://github.com/{Repo}");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                k.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
            }
            if (_settings.StartWithWindows) SetStartWithWindows(true, InstalledExe);
            World.Log("installed to " + InstallDir);
            // Hand over to the installed copy.
            SaveCast();
            Process.Start(new ProcessStartInfo(InstalledExe, "--wait") { UseShellExecute = true });
            _overlay.BeginInvoke(() => ExitThread());
            return "Installed! Doodlefolk is now in your Start menu.";
        }
        catch (Exception e) { return "Couldn't install: " + e.Message; }
    }

    /// <summary>Run as "Doodlefolk.exe --uninstall" (from Installed apps): remove the program, keep the figures.</summary>
    public static void Uninstall()
    {
        try { File.Delete(StartMenuLink); } catch { }
        try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false); } catch { }
        try { using var run = Registry.CurrentUser.OpenSubKey(RunKey, true); run?.DeleteValue("Doodlefolk", false); } catch { }
        MessageBox.Show("Doodlefolk has been removed. Your figures and settings are still in %APPDATA%\\Doodlefolk, in case you come back.", "Doodlefolk");
        // The program can't delete itself while running: a moment after it exits, the folder goes.
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t 3 /nobreak >nul & rmdir /s /q \"{InstallDir}\"")
            { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
        }
        catch { }
    }

    void SetStartWithWindows(bool on, string? exe = null)
    {
        try
        {
            using var run = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) run.SetValue("Doodlefolk", $"\"{exe ?? (IsInstalled ? InstalledExe : ExePath)}\"");
            else run.DeleteValue("Doodlefolk", false);
        }
        catch (Exception e) { World.Log("startup entry: " + e.Message); }
    }

    static void MakeShortcut(string link, string target)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("no WScript.Shell");
        dynamic shell = Activator.CreateInstance(shellType)!;
        try
        {
            dynamic sc = shell.CreateShortcut(link);
            sc.TargetPath = target;
            sc.WorkingDirectory = Path.GetDirectoryName(target);
            sc.Description = "Stick figures that live on your desktop";
            sc.IconLocation = target + ",0";
            sc.Save();
        }
        finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
    }

    // ---------------- updates ----------------

    (Version version, string tag, string url, long size, string notes)? _update;
    double _updateCheckAt = 20;
    volatile bool _updating;
    public string UpdateStatus = "";

    void UpdateFrame(double now)
    {
        if (!_settings.CheckUpdates || SteamHub.Ready || now < _updateCheckAt) return;
        _updateCheckAt = now + 6 * 3600;
        if (_settings.LastUpdateCheck is DateTime last && (DateTime.Now - last).TotalHours < 20 && _update == null) return;
        _ = CheckForUpdate();
    }

    async Task<string> CheckForUpdate()
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repo}/releases/latest");
            req.Headers.UserAgent.ParseAdd($"Doodlefolk/{VersionText}");
            req.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var res = await Http.SendAsync(req);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
            var r = doc.RootElement;
            string tag = r.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return "Couldn't read the latest version.";
            var asset = r.GetProperty("assets").EnumerateArray().FirstOrDefault(a => (a.GetProperty("name").GetString() ?? "").EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase));
            _overlay.BeginInvoke(() => { _settings.LastUpdateCheck = DateTime.Now; });
            if (v <= AppVersion || asset.ValueKind != JsonValueKind.Object)
            {
                UpdateStatus = $"You're up to date (version {VersionText}).";
                return UpdateStatus;
            }
            string url = asset.GetProperty("browser_download_url").GetString() ?? "";
            if (!url.StartsWith($"https://github.com/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase)) return "Unexpected download address; skipped.";
            _update = (v, tag, url, asset.GetProperty("size").GetInt64(), r.TryGetProperty("name", out var nm) ? nm.GetString() ?? tag : tag);
            UpdateStatus = $"{_update.Value.notes} is out (you have {VersionText}).";
            World.Log("update available: " + tag);
            _overlay.BeginInvoke(() => _w.News("app", $"A new version of Doodlefolk is out: {tag}. Update from the Studio's Settings.", 2));
            return UpdateStatus;
        }
        catch (Exception e)
        {
            World.Log("update check: " + e.Message);
            return UpdateStatus = "Couldn't check for updates right now.";
        }
    }

    /// <summary>Download the new version, swap it in and restart (your click in the Studio).</summary>
    async Task<string> ApplyUpdate(bool dryRun = false)
    {
        if (_update is not { } u) return "No update to install.";
        if (_updating) return "Already updating…";
        _updating = true;
        string work = Path.Combine(Path.GetTempPath(), "Doodlefolk-update");
        try
        {
            UpdateStatus = $"Downloading {u.tag}…";
            Directory.CreateDirectory(work);
            string zip = Path.Combine(work, "update.zip");
            using (var res = await Http.GetAsync(u.url, HttpCompletionOption.ResponseHeadersRead))
            {
                res.EnsureSuccessStatusCode();
                await using var fs = File.Create(zip);
                await res.Content.CopyToAsync(fs);
            }
            if (new FileInfo(zip).Length != u.size) throw new IOException("the download was incomplete");
            string newExe = Path.Combine(work, "Doodlefolk.exe");
            using (var archive = ZipFile.OpenRead(zip))
            {
                var entry = archive.Entries.FirstOrDefault(e => e.Name.Equals("Doodlefolk.exe", StringComparison.OrdinalIgnoreCase)) ?? throw new IOException("no Doodlefolk.exe in the download");
                entry.ExtractToFile(newExe, true);
            }
            var info = FileVersionInfo.GetVersionInfo(newExe);
            World.Log($"update downloaded: {info.ProductVersion} ({new FileInfo(newExe).Length / 1024 / 1024} MB)");
            if (dryRun) return UpdateStatus = $"Downloaded and checked {u.tag} (test run: not installed).";
            // A running program can be renamed, not overwritten: move this one aside, put the new one in its place.
            string current = ExePath, old = current + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(current, old);
            try { File.Move(newExe, current); }
            catch { File.Move(old, current); throw; }
            if (IsInstalled) try { using var k = Registry.CurrentUser.OpenSubKey(UninstallKey, true); k?.SetValue("DisplayVersion", u.version.ToString(3)); } catch { }
            _overlay.BeginInvoke(() =>
            {
                SaveCast();
                Process.Start(new ProcessStartInfo(current, "--wait") { UseShellExecute = true });
                ExitThread();
            });
            return UpdateStatus = $"Updated to {u.tag}. Restarting…";
        }
        catch (Exception e) { return UpdateStatus = "Couldn't update: " + e.Message; }
        finally { _updating = false; }
    }

    /// <summary>Tidy the previous version left beside us by an update.</summary>
    static void CleanUpOldVersion()
    {
        try { string old = ExePath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
    }

    // ---------------- battery saver ----------------

    bool _lite;
    double _powerCheckAt;

    void PowerFrame(double now, bool force = false)
    {
        if (now < _powerCheckAt && !force) return;
        _powerCheckAt = now + 5;
        bool onBattery = SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline;
        bool lite = _settings.BatterySaver switch { "always" => true, "off" => false, _ => onBattery };
        if (lite == _lite && !force) return;
        _lite = lite;
        World.Log(lite ? "battery saver on" : "battery saver off");
        Gfx.Q = lite ? GfxSettings.For("low") : _settings.Gfx;
        ApplyFps();
        ForceFullRedraw();
    }
}
