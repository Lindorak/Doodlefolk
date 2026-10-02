using Vortice.Mathematics;

namespace Doodlefolk;

enum FishTime { Any, Day, Night, DawnDusk }
enum FishRarity { Common, Uncommon, Rare, Legendary }

/// <summary>A kind of thing you can pull out of the pond.</summary>
sealed record FishKind(string Key, string Name, int[] Months, FishTime Time, FishRarity Rarity, float MinCm, float MaxCm,
                       uint Colour, string Hint, bool RainOnly = false, bool Junk = false)
{
    public Color4 Col => M.Hex(Colour);
    public bool InMonth(int month) => Months.Length == 0 || Months.Contains(month);
}

/// <summary>A record of one kind caught.</summary>
sealed class FishRecord
{
    public int Count { get; set; }
    public float BestCm { get; set; }
    public string BestBy { get; set; } = "";
    public DateTime First { get; set; }
}

/// <summary>The pond's fish change with the seasons and the time of day (an idea from Animal Crossing): spring trout
/// at dawn, catfish on summer nights, pike in winter, an eel when it rains, and a few rare ones (a golden koi, the
/// moonfish) worth staying up for. Everything caught goes in the Doodledex with the biggest one so far. In the
/// southern hemisphere the months turn round.</summary>
static class Fishes
{
    static readonly int[] All = Array.Empty<int>(), Spring = { 3, 4, 5 }, Summer = { 6, 7, 8 }, Autumn = { 9, 10, 11 }, Winter = { 12, 1, 2 };
    static int[] Of(params int[][] seasons) => seasons.SelectMany(s => s).ToArray();

    public static readonly FishKind[] Kinds =
    {
        new("minnow", "Minnow", All, FishTime.Any, FishRarity.Common, 3, 8, 0x9AA8B0, "Always about."),
        new("goldfish", "Goldfish", All, FishTime.Day, FishRarity.Common, 6, 20, 0xF57C00, "Any day, any time of year."),
        new("carp", "Carp", All, FishTime.Any, FishRarity.Common, 25, 70, 0x8D7B4A, "Year-round, lazy and large."),
        new("perch", "Perch", Of(Spring, Summer, Autumn), FishTime.Day, FishRarity.Common, 12, 35, 0x9CCC65, "Spring to autumn, in daylight."),
        new("bluegill", "Bluegill", Of(Summer), FishTime.Day, FishRarity.Common, 10, 25, 0x5C6BC0, "Summer days."),
        new("sunfish", "Sunfish", new[] { 6, 7 }, FishTime.Day, FishRarity.Uncommon, 10, 22, 0xFFB300, "Midsummer, when it's sunny."),
        new("bass", "Bass", Of(Spring, Summer, Autumn), FishTime.DawnDusk, FishRarity.Uncommon, 25, 60, 0x558B2F, "Dawn and dusk, spring to autumn."),
        new("trout", "Rainbow trout", Of(Spring, new[] { 9, 10 }), FishTime.DawnDusk, FishRarity.Uncommon, 25, 65, 0xEC6F8F, "Spring and early autumn, first and last light."),
        new("catfish", "Catfish", Of(Summer, new[] { 5, 9 }), FishTime.Night, FishRarity.Uncommon, 30, 110, 0x6D5D4B, "Warm nights."),
        new("eel", "Eel", Of(Summer, new[] { 5, 9 }), FishTime.Night, FishRarity.Rare, 40, 100, 0x4E5B31, "Rainy summer nights.", RainOnly: true),
        new("crayfish", "Crayfish", Of(Spring, Summer), FishTime.Night, FishRarity.Uncommon, 7, 15, 0xC62828, "Spring and summer nights."),
        new("tadpole", "Tadpole", Spring, FishTime.Day, FishRarity.Common, 1, 4, 0x3E3A33, "Spring days."),
        new("frog", "Frog", Of(Spring, Summer), FishTime.Any, FishRarity.Uncommon, 5, 12, 0x43A047, "Spring and summer, best in the rain.", RainOnly: true),
        new("salmon", "Salmon", new[] { 9, 10, 11 }, FishTime.Any, FishRarity.Uncommon, 50, 110, 0xF4845F, "Autumn's run."),
        new("pike", "Pike", Of(Autumn, Winter), FishTime.Day, FishRarity.Uncommon, 40, 120, 0x7CB342, "Autumn and winter days."),
        new("char", "Arctic char", Winter, FishTime.Any, FishRarity.Rare, 30, 75, 0xE57373, "Cold winter water."),
        new("smelt", "Smelt", Winter, FishTime.Night, FishRarity.Common, 10, 20, 0xB0BEC5, "Winter nights."),
        new("koi", "Koi", All, FishTime.Day, FishRarity.Rare, 30, 80, 0xF5F5F5, "Rare. Patient days."),
        new("goldenkoi", "Golden koi", All, FishTime.Day, FishRarity.Legendary, 40, 90, 0xFFD54F, "Very rare. Lucky days."),
        new("axolotl", "Axolotl", Of(Spring, Summer), FishTime.Night, FishRarity.Legendary, 15, 30, 0xF8BBD0, "Very rare. Warm nights."),
        new("moonfish", "Moonfish", All, FishTime.Night, FishRarity.Legendary, 20, 60, 0xCFD8DC, "Legend says: only very late at night."),
        new("snail", "Pond snail", Of(Spring, Summer, Autumn), FishTime.Any, FishRarity.Common, 1, 5, 0x8D6E63, "Mild months."),
        new("turtle", "Little turtle", Summer, FishTime.Day, FishRarity.Rare, 10, 25, 0x689F38, "Hot summer days."),
        new("boot", "Old boot", All, FishTime.Any, FishRarity.Common, 25, 32, 0x5D4037, "Someone lost it.", Junk: true),
        new("can", "Tin can", All, FishTime.Any, FishRarity.Common, 8, 12, 0x90A4AE, "Litter. They tidy it away.", Junk: true),
        new("bottle", "Message in a bottle", All, FishTime.Any, FishRarity.Rare, 20, 25, 0x80CBC4, "Who sent it?", Junk: true),
    };

    public static FishKind? Find(string key) => Kinds.FirstOrDefault(k => k.Key == key);

    /// <summary>The colours of what's biting (refreshed every few seconds), for the shapes under the pond's surface.</summary>
    public static Color4[] PondColours = Array.Empty<Color4>();
    public static void RefreshPond(bool raining) =>
        PondColours = Biting(DateTime.Now, raining).Where(k => !k.Junk && k.Rarity <= FishRarity.Uncommon && k.MaxCm >= 10).Select(k => k.Col).ToArray();

    /// <summary>The month as it feels where you are (the southern hemisphere's summer is in January).</summary>
    public static int SeasonalMonth(DateTime d) => Seasons.South ? (d.Month + 5) % 12 + 1 : d.Month;

    static bool TimeOk(FishTime t, int hour) => t switch
    {
        FishTime.Day => hour is >= 7 and < 19,
        FishTime.Night => hour >= 20 || hour < 5,
        FishTime.DawnDusk => hour is >= 5 and < 8 or >= 17 and < 21,
        _ => true,
    };

    static float Weight(FishRarity r) => r switch { FishRarity.Common => 10, FishRarity.Uncommon => 4, FishRarity.Rare => 1.1f, _ => 0.18f };

    /// <summary>What's biting now.</summary>
    public static List<FishKind> Biting(DateTime now, bool raining)
    {
        int m = SeasonalMonth(now), h = now.Hour;
        return Kinds.Where(k => k.InMonth(m) && TimeOk(k.Time, h) && (!k.RainOnly || raining || k.Key == "frog")
                                && (k.Key != "moonfish" || h is >= 0 and < 3)).ToList();
    }

    /// <summary>A catch: which kind and how long. Rain makes the rain-lovers likelier; luck tilts towards rare ones.</summary>
    public static (FishKind kind, float cm) Catch(Random rng, DateTime now, bool raining, float luck = 0)
    {
        var pool = Biting(now, raining);
        float W(FishKind k) => Weight(k.Rarity) * (k.Rarity >= FishRarity.Rare ? 1 + luck : 1) * (k.RainOnly && raining ? 3 : 1) * (k.Junk ? 0.5f : 1);
        float total = pool.Sum(W), pick = (float)rng.NextDouble() * total;
        var kind = pool[^1];
        foreach (var k in pool) { pick -= W(k); if (pick <= 0) { kind = k; break; } }
        // Lengths lean small: the big ones are the stories.
        float t = (float)Math.Pow(rng.NextDouble(), 1.8);
        return (kind, MathF.Round(kind.MinCm + (kind.MaxCm - kind.MinCm) * t, 1));
    }

    /// <summary>"tiny"…"HUGE" for a length, relative to its kind.</summary>
    public static string Size(FishKind k, float cm)
    {
        float t = (cm - k.MinCm) / Math.Max(0.1f, k.MaxCm - k.MinCm);
        return t switch { < 0.12f => "tiny", < 0.3f => "little", < 0.6f => "decent", < 0.85f => "big", _ => "HUGE" };
    }

    public static string Months(FishKind k)
    {
        if (k.Months.Length == 0) return "all year";
        string[] names = { "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };
        var ms = k.Months.Select(m => Seasons.South ? (m + 5) % 12 + 1 : m).OrderBy(m => m).ToList();
        return string.Join(", ", ms.Select(m => names[m]));
    }
}
