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
    /// <summary>Bring back the same figures and balls next launch.</summary>
    public bool RememberCast { get; set; } = true;
    /// <summary>Studio look: "auto" (follow Windows), "paper" or "chalk".</summary>
    public string Theme { get; set; } = "auto";
    public bool SoundOn { get; set; } = true;
    public float SoundVolume { get; set; } = 0.55f;
    /// <summary>Screen awareness (all read on this PC only; nothing is saved or sent).</summary>
    public bool ScreenTerrain { get; set; } = true;
    public bool ScreenReact { get; set; } = true;
    public bool ScreenLinks { get; set; } = true;
    public bool ScreenMedia { get; set; } = true;
    public FightSettings Fight { get; set; } = new();
    public List<SavedFigure> Figures { get; set; } = new();
    /// <summary>The user's own saved figures (name, colour, size, personality, gear) to spawn any time.</summary>
    public List<SavedFigure> Library { get; set; } = new();
    public List<SavedProp> Props { get; set; } = new();
    public List<SavedItem> Items { get; set; } = new();

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
