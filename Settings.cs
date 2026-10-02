using System.Text.Json;

namespace Doodlefolk;

sealed class SavedFigure
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#E53935";
    public float Size { get; set; } = 1;
    public Gear Gear { get; set; }
    public StyleChoice? Style { get; set; }
    public Tastes? Tastes { get; set; }
    /// <summary>How it feels about the user: fondness -1..1 and trust 0..1.</summary>
    public float? Fondness { get; set; }
    public float? Trust { get; set; }
    public bool Hunter { get; set; }
    public Look? Look { get; set; }
    public Gender? Gender { get; set; }
    public Attraction? Attraction { get; set; }
    /// <summary>Romantic feelings toward other saved figures, by name, and who they're dating.</summary>
    public Dictionary<string, float> Love { get; set; } = new();
    public string? Sweetheart { get; set; }
    public List<DiaryEntry> Diary { get; set; } = new();
    public Dictionary<string, float> Skills { get; set; } = new();
    public DateTime? Born { get; set; }
    public List<string> Parents { get; set; } = new();
    public float Grown { get; set; } = 1;
    public float AdultSize { get; set; }
    public DateTime? LastBaby { get; set; }
    public int Trophies { get; set; }
    public DateTime? ChampionOn { get; set; }
    public float? Weight { get; set; }
    public Dictionary<string, int[]> Record { get; set; } = new();
    public List<Gift> Gifts { get; set; } = new();
    public string Hobby { get; set; } = "";
    public string Job { get; set; } = "";
    public int? Coins { get; set; }
    public float AgeBank { get; set; }
    public List<string> Collection { get; set; } = new();
    /// <summary>Learned habits: activity kind → value (-1..1), and how often each was tried.</summary>
    public Dictionary<string, float> Learned { get; set; } = new();
    public Dictionary<string, int> Tried { get; set; } = new();
    public Personality Traits { get; set; } = new();
    /// <summary>Feelings toward other saved figures, by name.</summary>
    public Dictionary<string, float> Affinity { get; set; } = new();
}

sealed class SavedItem
{
    public string Key { get; set; } = "";
    public float Size { get; set; } = 1;
    public string Color { get; set; } = "";
    public bool Flip { get; set; }
    /// <summary>Resting tilt in degrees.</summary>
    public float Tilt { get; set; }
    /// <summary>Whose home it is (a figure's name), if anyone's.</summary>
    public string? Owner { get; set; }
    public float Fill { get; set; } = 1;
    public float ScaleX { get; set; } = 1;
    public float ScaleY { get; set; } = 1;
    public int Dirt { get; set; }
    public float Growth { get; set; }
    public string PlantKind { get; set; } = "";
    public string? Planter { get; set; }
}

sealed class SavedClub
{
    public string Name { get; set; } = "";
    public string Colour { get; set; } = "";
    public List<string> Members { get; set; } = new();
    public string Thing { get; set; } = "";
    public DateTime Founded { get; set; }
}

sealed class SavedPet
{
    public PetKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";
    public string Accent { get; set; } = "";
    public float Size { get; set; } = 1;
    public string? Owner { get; set; }
    public float Age { get; set; } = 1;
    public DateTime? Born { get; set; }
    public float Weight { get; set; } = 0.18f;
    public float UserBond { get; set; } = 0.2f;
    /// <summary>Hunger, thirst, bladder, bowel, energy, attention, boredom, stress, stamina, frustration.</summary>
    public float[]? Needs { get; set; }
    public Dictionary<string, float> Restraint { get; set; } = new();
    public Dictionary<string, float> Skills { get; set; } = new();
    public List<string> Vocabulary { get; set; } = new();
    public Dictionary<string, float> PetBonds { get; set; } = new();
    public List<SavedLog> Log { get; set; } = new();
    public int Sprays { get; set; }
    public int Treats { get; set; }
    public float Health { get; set; } = 1;
    public float Clean { get; set; } = 1;
    public string Sick { get; set; } = "";
    public string Temper { get; set; } = "";
    public bool? Female { get; set; }
    public string Wear { get; set; } = "";
    public string WearColour { get; set; } = "";
    public string Rare { get; set; } = "";
    public float Pregnant { get; set; }
    public string Mother { get; set; } = "";
    public DateTime? LastLitter { get; set; }
}

sealed class SavedLog
{
    public DateTime When { get; set; }
    public string Text { get; set; } = "";
}

sealed class SavedProp
{
    public PropKind Kind { get; set; }
    public float Size { get; set; } = 1;
    public float Bounce { get; set; } = 0.65f;
    public string Color { get; set; } = "#E53935";
}

/// <summary>User preferences and the remembered cast, persisted as JSON in %APPDATA%\Doodlefolk\settings.json.</summary>
sealed class Settings
{
    public const int MatchMonitor = -1, Unlimited = 0;

    /// <summary>Frame-rate cap in fps, or <see cref="MatchMonitor"/> / <see cref="Unlimited"/>.</summary>
    public int FpsCap { get; set; } = 60;
    /// <summary>Drop to half the monitor's rate while nothing's moving fast (saves power).</summary>
    public bool SmartFps { get; set; } = true;
    /// <summary>Graphics quality (preset and its parts).</summary>
    public GfxSettings Gfx { get; set; } = new();
    /// <summary>Bring back the same figures and balls next launch.</summary>
    public bool RememberCast { get; set; } = true;
    /// <summary>Studio look: "auto" (follow Windows), "paper" or "chalk".</summary>
    public string Theme { get; set; } = "auto";
    public bool SoundOn { get; set; } = true;
    public float SoundVolume { get; set; } = 0.55f;
    /// <summary>Babble voices when they talk.</summary>
    public bool Voices { get; set; } = true;
    /// <summary>Screen awareness (all read on this PC only; nothing is saved or sent).</summary>
    public bool ScreenTerrain { get; set; } = true;
    public bool ScreenReact { get; set; } = true;
    public bool ScreenLinks { get; set; } = true;
    public bool ScreenMedia { get; set; } = true;
    /// <summary>Notice when you finish typing (timing only) and when notifications pop up.</summary>
    public bool NoticeTyping { get; set; } = true;
    public bool Notifications { get; set; } = true;
    /// <summary>How often weather happens: off, rare, sometimes, often. And whether night makes them sleepy.</summary>
    public string WeatherMode { get; set; } = "sometimes";
    public bool DayNight { get; set; } = true;
    /// <summary>Birthday parties and holiday dress-up.</summary>
    public bool Celebrations { get; set; } = true;
    /// <summary>Couples who've been together a long while can have a baby.</summary>
    public bool Babies { get; set; } = true;
    /// <summary>Just pets on the desktop (the figures are kept aside).</summary>
    public bool PetMode { get; set; }
    /// <summary>How demanding pets are: relaxed (slow needs, no accidents), normal, realistic.</summary>
    public string PetCare { get; set; } = "normal";
    /// <summary>Everyone (figures and animals) gets tired with exertion.</summary>
    public bool StaminaOn { get; set; } = true;
    /// <summary>Everyone's weight changes with eating and exercise.</summary>
    public bool WeightOn { get; set; } = true;
    /// <summary>Figures help look after the pets (fill bowls, scoop the litter box).</summary>
    public bool PetHelp { get; set; } = true;
    /// <summary>Bonded pairs of animals can have litters.</summary>
    public bool PetBreeding { get; set; } = true;
    /// <summary>Figures with a lasso may rope your cursor (only when you've left the mouse alone; moving it breaks free).</summary>
    public bool LassoCursor { get; set; } = true;
    /// <summary>Figures have jobs, earn coins and spend them.</summary>
    public bool Jobs { get; set; } = true;
    /// <summary>How fast figures age: off, slow (a year a day), fast (a year an hour).</summary>
    public string LifePace { get; set; } = "off";
    /// <summary>The town holds festivals, talent shows and race days now and then.</summary>
    public bool Events { get; set; } = true;
    /// <summary>The name of the cast in play (others are kept in the casts folder).</summary>
    public string CastName { get; set; } = "My cast";
    /// <summary>The Studio tour has been seen (or skipped).</summary>
    public bool TourDone { get; set; }
    /// <summary>Not saved: there was no settings file yet (the very first run).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool FirstRun { get; set; }
    /// <summary>Accessibility: calm mode, colour-blind team badges.</summary>
    public bool Calm { get; set; }
    public bool ColourBlind { get; set; }
    /// <summary>Quiet hours: hide everyone on these days between these times.</summary>
    public bool PauseSchedule { get; set; }
    /// <summary>Desktop reactions: downloads finishing (only the file's kind is noticed), you getting frustrated.</summary>
    public bool NoticeDownloads { get; set; } = true;
    /// <summary>Talk to them out loud (Windows speech recognition, on this PC).</summary>
    public bool VoiceInput { get; set; }
    /// <summary>Ideas from StickBuddies: dance in time to the beat; gestures instead of words; a nudge to take a break
    /// after a long stretch of games or videos.</summary>
    public bool BeatDance { get; set; } = true;
    /// <summary>Town mood: cozy, classic or chaos.</summary>
    public string TownMood { get; set; } = "classic";
    /// <summary>Visitors drop by; gift crates waiting for the mail carrier; rare hats found; the Doodledex.</summary>
    public bool Visitors { get; set; } = true;
    /// <summary>Big moments (a first date, a baby, a race won) are photographed for the album, town only.</summary>
    public bool AutoAlbum { get; set; } = true;
    /// <summary>Background sound (rain, birds, the pond, the town talking…): off until you turn it on.</summary>
    public bool AmbienceOn { get; set; }
    public float AmbienceVolume { get; set; } = 0.6f;
    /// <summary>Each channel's level (0..1), by name; missing ones are 1 (the lo-fi has its own switch).</summary>
    public Dictionary<string, float> AmbienceLevels { get; set; } = new();
    /// <summary>Channels follow the town (rain when it rains, crickets on warm nights); off: they just play at their levels.</summary>
    public bool AmbienceFollow { get; set; } = true;
    public bool LoFiOn { get; set; }
    /// <summary>The lo-fi beat plays during focus sessions (when ambience is on).</summary>
    public bool FocusLoFi { get; set; } = true;
    public int CratesWaiting { get; set; }
    public List<string> UnlockedHats { get; set; } = new();
    public Dictionary<string, DexEntry> Dex { get; set; } = new();
    /// <summary>Everything caught in the pond, by kind (see Fishes).</summary>
    public Dictionary<string, FishRecord> FishLog { get; set; } = new();
    public int FishTotal { get; set; }
    /// <summary>"auto" (from the weather location, else north), "north" or "south": which way round the seasons go.</summary>
    public string Hemisphere { get; set; } = "auto";
    /// <summary>Focus sessions: start the next one by itself after the break; how many so far; your to-dos.</summary>
    public bool FocusAutoNext { get; set; }
    public int FocusSessions { get; set; }
    public int FocusMinutes { get; set; }
    public int FocusLength { get; set; } = 25;
    public List<FocusTask> FocusTasks { get; set; } = new();
    /// <summary>Learned: how busy you usually are at each hour of the day (0..1).</summary>
    public float[] BusyByHour { get; set; } = new float[24];
    public bool GesturesOnly { get; set; }
    public bool BreakNudges { get; set; }
    public int BreakMinutes { get; set; } = 60;
    /// <summary>Start with Windows; check GitHub for new versions; battery saver: off, battery (when unplugged), always.</summary>
    public bool StartWithWindows { get; set; }
    public bool CheckUpdates { get; set; } = true;
    public DateTime? LastUpdateCheck { get; set; }
    public string BatterySaver { get; set; } = "battery";
    /// <summary>Optional AI conversations: on/off, the key (encrypted with Windows DPAPI for this user), model, daily cap.</summary>
    public bool AiChat { get; set; }
    public string AiKeyProtected { get; set; } = "";
    public string AiModel { get; set; } = "gpt-5-mini";
    public int AiDailyLimit { get; set; } = 150;
    public bool NoticeFrustration { get; set; } = true;
    public List<Reminder> Reminders { get; set; } = new();
    public string PauseFrom { get; set; } = "09:00";
    public string PauseTo { get; set; } = "17:00";
    public List<int> PauseDays { get; set; } = new() { 1, 2, 3, 4, 5 };
    /// <summary>Where to take the real weather from (when the weather is set to "real").</summary>
    public string WeatherPlace { get; set; } = "";
    public double? WeatherLat { get; set; }
    public double? WeatherLon { get; set; }
    public DateTime? LastSeen { get; set; }
    public int WishesGranted { get; set; }
    public List<SavedClub> Clubs { get; set; } = new();
    public List<NewsItem> News { get; set; } = new();
    /// <summary>Your sticker book: sticker key → when you got it.</summary>
    public Dictionary<string, DateTime> Stickers { get; set; } = new();
    /// <summary>Figures ask you for things they want (a thought bubble you can click to give it to them).</summary>
    public bool Wishes { get; set; } = true;
    /// <summary>Crushes, dating, jealousy and breakups.</summary>
    public bool Romance { get; set; } = true;
    public FightSettings Fight { get; set; } = new();
    public List<SavedFigure> Figures { get; set; } = new();
    /// <summary>The user's own saved figures (name, colour, size, personality, gear) to spawn any time.</summary>
    public List<SavedFigure> Library { get; set; } = new();
    public List<SavedProp> Props { get; set; } = new();
    public List<SavedItem> Items { get; set; } = new();
    public List<SavedPet> Pets { get; set; } = new();

    public static string FilePath => PathOnDisk;
    static string PathOnDisk => System.IO.Path.Combine(AppPaths.DataDir, "settings.json");

    public static Settings Load()
    {
        if (!File.Exists(PathOnDisk)) return new Settings { FirstRun = true };
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathOnDisk)) ?? new Settings();
        }
        catch (JsonException)
        {
            // Damaged (say, the PC lost power mid-save): keep it aside rather than overwrite it with a blank cast.
            try { File.Copy(PathOnDisk, System.IO.Path.ChangeExtension(PathOnDisk, $".broken-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true); } catch { }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathOnDisk)!);
            // Write beside it, then swap in: a crash mid-save leaves the old file whole.
            string tmp = PathOnDisk + ".saving";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, PathOnDisk, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static string Hex(Vortice.Mathematics.Color4 c) =>
        $"#{(int)(c.R * 255):X2}{(int)(c.G * 255):X2}{(int)(c.B * 255):X2}";

    public static Vortice.Mathematics.Color4 ParseHex(string s) =>
        s.Length == 7 && s[0] == '#' && uint.TryParse(s[1..], System.Globalization.NumberStyles.HexNumber, null, out var v)
            ? M.Hex(v) : M.Hex(0xE53935);
}
