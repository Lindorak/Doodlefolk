namespace Doodlefolk;

enum Gender { Girl, Boy, Nonbinary }

/// <summary>Who a figure can fall for. None: not into romance (they still make friends).</summary>
[Flags]
enum Attraction { None = 0, Girls = 1, Boys = 2, Nonbinary = 4, Everyone = Girls | Boys | Nonbinary }

static class Romance
{
    public static Attraction Bit(Gender g) => g switch { Gender.Girl => Attraction.Girls, Gender.Boy => Attraction.Boys, _ => Attraction.Nonbinary };

    public static string Pronoun(Gender g) => g switch { Gender.Girl => "she", Gender.Boy => "he", _ => "they" };

    /// <summary>A random gender and attraction for a new figure (both can be changed in the Studio).</summary>
    public static (Gender, Attraction) Roll(Random r)
    {
        double x = r.NextDouble();
        var g = x < 0.46 ? Gender.Girl : x < 0.92 ? Gender.Boy : Gender.Nonbinary;
        double y = r.NextDouble();
        Attraction other = g switch
        {
            Gender.Girl => Attraction.Boys,
            Gender.Boy => Attraction.Girls,
            _ => r.NextDouble() < 0.5 ? Attraction.Girls : Attraction.Boys,
        };
        var a = y < 0.05 ? Attraction.None
              : y < 0.62 ? other | (r.NextDouble() < 0.3 ? Attraction.Nonbinary : 0)
              : y < 0.75 ? Bit(g)
              : Attraction.Everyone;
        return (g, a);
    }

    public static string Describe(Attraction a) => a switch
    {
        Attraction.None => "isn't into romance",
        Attraction.Everyone => "can fall for anyone",
        _ => "can fall for " + string.Join(" and ", new[] { (Attraction.Girls, "girls"), (Attraction.Boys, "boys"), (Attraction.Nonbinary, "nonbinary folks") }
                                                        .Where(x => a.HasFlag(x.Item1)).Select(x => x.Item2)),
    };
}
