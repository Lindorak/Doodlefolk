using System.Text.Json;

namespace StickFight;

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
    public List<string> Collection { get; set; } = new();
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

/// <summary>User preferences and the remembered cast, persisted as JSON in %APPDATA%\StickFight\settings.json.</summary>
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

    static string PathOnDisk => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "StickFight", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(PathOnDisk))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathOnDisk)) ?? new Settings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathOnDisk)!);
            File.WriteAllText(PathOnDisk, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static string Hex(Vortice.Mathematics.Color4 c) =>
        $"#{(int)(c.R * 255):X2}{(int)(c.G * 255):X2}{(int)(c.B * 255):X2}";

    public static Vortice.Mathematics.Color4 ParseHex(string s) =>
        s.Length == 7 && s[0] == '#' && uint.TryParse(s[1..], System.Globalization.NumberStyles.HexNumber, null, out var v)
            ? M.Hex(v) : M.Hex(0xE53935);
}
