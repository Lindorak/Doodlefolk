using Steamworks;

namespace Doodlefolk;

/// <summary>Steam, when Doodlefolk runs through it (the Steam build, or a developer copy with steam_appid.txt beside
/// it): Workshop mods (subscribed items load like mods in the mods folder, and you can publish your own), achievements
/// for the sticker book, and a line of rich presence. Everywhere else (the GitHub download) it does nothing at all.
/// Cloud saves need no code: Steam Auto-Cloud is pointed at %APPDATA%\Doodlefolk in the Steamworks settings (see
/// docs/STEAM.md).</summary>
static class SteamHub
{
    public static bool Ready { get; private set; }
    public static string Status { get; private set; } = "Not running through Steam.";
    public static uint AppId => Ready ? SteamUtils.GetAppID().m_AppId : 0;
    static double _presenceAt;

    public static void Init()
    {
        bool launchedBySteam = Environment.GetEnvironmentVariable("SteamAppId") != null || Environment.GetEnvironmentVariable("SteamGameId") != null;
        bool devCopy = File.Exists(Path.Combine(AppContext.BaseDirectory, "steam_appid.txt"));
        if (!launchedBySteam && !devCopy) return;
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "steam_api64.dll"))) { Status = "This copy has no Steam support (steam_api64.dll is missing)."; return; }
        try
        {
            var result = SteamAPI.InitEx(out string message);
            Ready = result == ESteamAPIInitResult.k_ESteamAPIInitResult_OK;
            Status = Ready ? $"Connected to Steam as {SteamFriends.GetPersonaName()} (app {AppId})." : $"Couldn't connect to Steam: {message}";
        }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            Status = "Couldn't load Steam: " + e.Message;
        }
        World.Log("steam: " + Status);
    }

    public static void Frame(double now, int figures, int pets)
    {
        if (!Ready) return;
        try
        {
            SteamAPI.RunCallbacks();
            if (_statsDirty && now > _storeAt) { _statsDirty = false; _storeAt = now + 60; SteamUserStats.StoreStats(); }
            if (now > _presenceAt)
            {
                _presenceAt = now + 60;
                SteamFriends.SetRichPresence("status", $"{figures} doodlefolk and {pets} pets on the desktop");
            }
        }
        catch (Exception e) { World.Log("steam: " + e.Message); }
    }

    public static void Shutdown()
    {
        if (!Ready) return;
        try { if (_statsDirty) SteamUserStats.StoreStats(); } catch { }
        Ready = false;
        try { SteamAPI.Shutdown(); } catch { }
    }

    // ---------------- achievements ----------------

    /// <summary>Stickers are achievements on Steam: "litter" → STICKER_LITTER (set up on the Steamworks site).</summary>
    public static void Achieve(string sticker)
    {
        if (!Ready) return;
        try
        {
            string id = "STICKER_" + sticker.ToUpperInvariant();
            if (SteamUserStats.GetAchievement(id, out bool done) && !done)
            {
                SteamUserStats.SetAchievement(id);
                SteamUserStats.StoreStats();
            }
        }
        catch (Exception e) { World.Log("steam achievement: " + e.Message); }
    }

    // ---------------- stats ----------------

    static bool _statsDirty;
    static double _storeAt;

    /// <summary>Add to one of your Steam stats (set up on the Steamworks site; see docs/STEAM.md). Stored once a minute.
    /// Global totals of these drive the community goals.</summary>
    public static void AddStat(string name, int by)
    {
        if (!Ready || by == 0) return;
        try
        {
            if (SteamUserStats.GetStat(name, out int v) && SteamUserStats.SetStat(name, v + by)) _statsDirty = true;
        }
        catch (Exception e) { World.Log("steam stat: " + e.Message); }
    }

    /// <summary>Raise a "biggest/longest" stat if this beats it.</summary>
    public static void MaxStat(string name, float value)
    {
        if (!Ready) return;
        try
        {
            if (SteamUserStats.GetStat(name, out float v) && value > v && SteamUserStats.SetStat(name, value)) _statsDirty = true;
        }
        catch (Exception e) { World.Log("steam stat: " + e.Message); }
    }

    // ---------------- workshop ----------------

    /// <summary>The folders of the Workshop items you're subscribed to that have finished downloading.</summary>
    public static List<string> WorkshopFolders()
    {
        var dirs = new List<string>();
        if (!Ready) return dirs;
        try
        {
            uint n = SteamUGC.GetNumSubscribedItems();
            if (n == 0) return dirs;
            var ids = new PublishedFileId_t[n];
            n = SteamUGC.GetSubscribedItems(ids, n);
            for (int i = 0; i < n; i++)
            {
                var state = (EItemState)SteamUGC.GetItemState(ids[i]);
                if (!state.HasFlag(EItemState.k_EItemStateInstalled)) { SteamUGC.DownloadItem(ids[i], false); continue; }
                if (SteamUGC.GetItemInstallInfo(ids[i], out _, out string folder, 1024, out _) && Directory.Exists(folder)) dirs.Add(folder);
            }
        }
        catch (Exception e) { World.Log("steam workshop: " + e.Message); }
        return dirs;
    }

    static CallResult<CreateItemResult_t>? _create;
    static CallResult<SubmitItemUpdateResult_t>? _submit;
    public static bool Publishing { get; private set; }

    /// <summary>Publish a mod (its JSON file and any SVG files beside it that it names) as a new Workshop item.</summary>
    public static string Publish(string modFile, string title, string description, string? previewPng, Action<string> done)
    {
        if (!Ready) return "Steam isn't running.";
        if (Publishing) return "Already publishing something.";
        if (!File.Exists(modFile)) return "That mod file is gone.";
        // A folder with just this mod in it.
        string stage = Path.Combine(Path.GetTempPath(), "Doodlefolk-workshop", Path.GetFileNameWithoutExtension(modFile));
        try
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, true);
            Directory.CreateDirectory(stage);
            File.Copy(modFile, Path.Combine(stage, Path.GetFileName(modFile)));
            string text = File.ReadAllText(modFile);
            foreach (var svg in Directory.GetFiles(Path.GetDirectoryName(modFile)!, "*.svg"))
                if (text.Contains(Path.GetFileName(svg), StringComparison.OrdinalIgnoreCase)) File.Copy(svg, Path.Combine(stage, Path.GetFileName(svg)));
        }
        catch (Exception e) { return "Couldn't prepare it: " + e.Message; }
        Publishing = true;
        _create ??= CallResult<CreateItemResult_t>.Create();
        _create.Set(SteamUGC.CreateItem(SteamUtils.GetAppID(), EWorkshopFileType.k_EWorkshopFileTypeCommunity), (r, failed) =>
        {
            if (failed || r.m_eResult != EResult.k_EResultOK) { Publishing = false; done($"Steam said no ({(failed ? "network trouble" : r.m_eResult.ToString())})."); return; }
            if (r.m_bUserNeedsToAcceptWorkshopLegalAgreement) SteamFriends.ActivateGameOverlayToWebPage("https://steamcommunity.com/sharedfiles/workshoplegalagreement");
            var h = SteamUGC.StartItemUpdate(SteamUtils.GetAppID(), r.m_nPublishedFileId);
            SteamUGC.SetItemTitle(h, title.Length > 0 ? title : Path.GetFileNameWithoutExtension(modFile));
            SteamUGC.SetItemDescription(h, description);
            SteamUGC.SetItemContent(h, stage);
            SteamUGC.SetItemVisibility(h, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic);
            SteamUGC.SetItemTags(h, new List<string> { "Mod" });
            if (previewPng != null && File.Exists(previewPng)) SteamUGC.SetItemPreview(h, previewPng);
            _submit ??= CallResult<SubmitItemUpdateResult_t>.Create();
            _submit.Set(SteamUGC.SubmitItemUpdate(h, "First upload"), (s, failed2) =>
            {
                Publishing = false;
                done(failed2 || s.m_eResult != EResult.k_EResultOK ? $"Upload failed ({(failed2 ? "network trouble" : s.m_eResult.ToString())})." : $"Published to the Workshop! (item {r.m_nPublishedFileId.m_PublishedFileId})");
            });
        });
        return "Publishing to the Workshop…";
    }
}
