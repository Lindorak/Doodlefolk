namespace Doodlefolk;

/// <summary>Things a figure can like or dislike.</summary>
enum Thing
{
    // Activities
    PlayingBall, Juggling, Climbing, Exploring, Chatting, HighFives, Fighting, Sparring, Napping, Tricks, Dancing, Sitting, Eating, Reading,
    // Places
    HighPlaces, Taskbar, Ledges,
    // Toys
    SoccerBalls, Basketballs, BeachBalls,
    // You
    YourCursor, BeingPickedUp, BeingThrown,
    // Animals
    Pets,
}

/// <summary>A figure's likes and dislikes, Sims-style: opinions from -1 (hates) to 1 (loves) on things,
/// plus a favourite and a least favourite colour. Generated from personality + a seed, editable by the user.</summary>
sealed class Tastes
{
    /// <summary>Opinions by Thing name (strings so the saved file stays readable and survives new Things).</summary>
    public Dictionary<string, float> Opinions { get; set; } = new();
    public string FavoriteColour { get; set; } = "";
    public string DislikedColour { get; set; } = "";

    static readonly Thing[] AllThings = Enum.GetValues<Thing>();
    static readonly string[] ThingKeys = AllThings.Select(t => t.ToString()).ToArray();

    // Opinions as a flat array, rebuilt when the dictionary changes (asked about many times a frame).
    float[]? _vec;
    Dictionary<string, float>? _vecSrc;
    int _vecCount;

    float[] Vec
    {
        get
        {
            if (_vec != null && ReferenceEquals(_vecSrc, Opinions) && _vecCount == Opinions.Count) return _vec;
            _vec = new float[ThingKeys.Length];
            for (int i = 0; i < ThingKeys.Length; i++) _vec[i] = Opinions.TryGetValue(ThingKeys[i], out var v) ? v : 0;
            _vecSrc = Opinions;
            _vecCount = Opinions.Count;
            return _vec;
        }
    }

    public float Of(Thing t) => Vec[(int)t];
    public void Set(Thing t, float v)
    {
        if (MathF.Abs(v) < 0.01f) Opinions.Remove(ThingKeys[(int)t]);
        else Opinions[ThingKeys[(int)t]] = Math.Clamp(v, -1, 1);
        _vec = null;
    }

    public bool Likes(Thing t) => Of(t) > 0.35f;
    public bool Dislikes(Thing t) => Of(t) < -0.35f;

    public Tastes Clone() => new() { Opinions = new(Opinions), FavoriteColour = FavoriteColour, DislikedColour = DislikedColour };

    public static string Name(Thing t) => t switch
    {
        Thing.PlayingBall => "Playing ball", Thing.HighFives => "High-fives", Thing.HighPlaces => "High places",
        Thing.Ledges => "Sitting on ledges", Thing.SoccerBalls => Prop.KindName(PropKind.SoccerBall) + "s",
        Thing.Basketballs => "Basketballs", Thing.BeachBalls => "Beach balls", Thing.YourCursor => "Your cursor",
        Thing.BeingPickedUp => "Being picked up", Thing.BeingThrown => "Being thrown",
        _ => t.ToString(),
    };

    public static Thing? ForProp(PropKind k) => k switch
    {
        PropKind.SoccerBall => Thing.SoccerBalls, PropKind.Basketball => Thing.Basketballs, PropKind.BeachBall => Thing.BeachBalls, _ => null,
    };

    /// <summary>How alike two figures' tastes are, -1..1 (shared loves and shared hates both count).</summary>
    public float Similarity(Tastes o)
    {
        float dot = 0, a = 0, b = 0;
        float[] va = Vec, vb = o.Vec;
        for (int i = 0; i < va.Length; i++)
        {
            float x = va[i], y = vb[i];
            dot += x * y; a += x * x; b += y * y;
        }
        float sim = a > 0 && b > 0 ? dot / MathF.Sqrt(a * b) : 0;
        if (FavoriteColour != "" && FavoriteColour == o.FavoriteColour) sim += 0.2f;
        return Math.Clamp(sim, -1, 1);
    }

    /// <summary>Things both love (for "we both like…" moments).</summary>
    public IEnumerable<Thing> SharedLikes(Tastes o) => AllThings.Where(t => Likes(t) && o.Likes(t));

    /// <summary>Roll tastes that fit a personality: a few strong likes, a couple of dislikes, the rest lukewarm.</summary>
    public static Tastes Generate(Personality p, Random r)
    {
        float E = p.Energy, C = p.Curiosity, B = p.Bravery, Pl = p.Playfulness, A = p.Aggression, So = p.Sociability;
        // Personality-driven leaning for each thing, then noise.
        var lean = new Dictionary<Thing, float>
        {
            [Thing.PlayingBall] = Pl * 0.9f + E * 0.3f - 0.4f,
            [Thing.Juggling] = Pl * 0.8f - 0.35f,
            [Thing.Climbing] = E * 0.6f + B * 0.4f - 0.45f,
            [Thing.Exploring] = C * 0.9f - 0.35f,
            [Thing.Chatting] = So * 0.9f - 0.4f,
            [Thing.HighFives] = So * 0.6f + Pl * 0.4f - 0.45f,
            [Thing.Fighting] = A * 1.0f - 0.55f,
            [Thing.Sparring] = A * 0.5f + Pl * 0.5f - 0.4f,
            [Thing.Napping] = (1 - E) * 0.9f - 0.35f,
            [Thing.Tricks] = Pl * 0.6f + E * 0.4f - 0.45f,
            [Thing.Dancing] = Pl * 0.6f + So * 0.4f - 0.45f,
            [Thing.Sitting] = (1 - E) * 0.7f - 0.3f,
            [Thing.Eating] = (1 - E) * 0.3f + Pl * 0.2f - 0.15f,
            [Thing.Reading] = C * 0.6f + (1 - E) * 0.4f - 0.5f,
            [Thing.HighPlaces] = B * 0.8f + C * 0.2f - 0.5f,
            [Thing.Taskbar] = (1 - C) * 0.5f - 0.25f,
            [Thing.Ledges] = (1 - E) * 0.4f + B * 0.3f - 0.3f,
            [Thing.SoccerBalls] = Pl * 0.4f - 0.2f,
            [Thing.Basketballs] = Pl * 0.4f - 0.2f,
            [Thing.BeachBalls] = Pl * 0.3f + So * 0.2f - 0.25f,
            [Thing.YourCursor] = So * 0.5f + C * 0.4f - B * 0.1f - 0.3f,
            [Thing.BeingPickedUp] = So * 0.3f + Pl * 0.3f - (1 - B) * 0.4f - 0.15f,
            [Thing.BeingThrown] = B * 0.6f + Pl * 0.5f - 0.75f,
        };
        var t = new Tastes();
        var ranked = lean.Select(kv => (kv.Key, v: kv.Value + (float)(r.NextDouble() * 2 - 1) * 0.45f)).OrderByDescending(x => x.v).ToList();
        int likes = 3 + r.Next(3), dislikes = 1 + r.Next(3);
        for (int i = 0; i < ranked.Count; i++)
        {
            var (thing, v) = ranked[i];
            float opinion = i < likes ? 0.5f + (float)r.NextDouble() * 0.5f
                          : i >= ranked.Count - dislikes ? -(0.5f + (float)r.NextDouble() * 0.5f)
                          : Math.Clamp(v * 0.4f, -0.3f, 0.3f);
            t.Set(thing, opinion);
        }
        var colours = Palette.All.Select(c => c.Name).ToList();
        t.FavoriteColour = colours[r.Next(colours.Count)];
        do t.DislikedColour = colours[r.Next(colours.Count)]; while (t.DislikedColour == t.FavoriteColour);
        return t;
    }

    public string Describe()
    {
        var likes = Opinions.Where(kv => kv.Value > 0.35f).OrderByDescending(kv => kv.Value).Select(kv => Name(Enum.Parse<Thing>(kv.Key))).Take(4).ToList();
        var hates = Opinions.Where(kv => kv.Value < -0.35f).OrderBy(kv => kv.Value).Select(kv => Name(Enum.Parse<Thing>(kv.Key))).Take(3).ToList();
        string s = likes.Count > 0 ? "Loves " + string.Join(", ", likes).ToLowerInvariant() : "No strong likes";
        if (hates.Count > 0) s += "; hates " + string.Join(", ", hates).ToLowerInvariant();
        if (FavoriteColour != "") s += $". Favourite colour: {FavoriteColour.ToLowerInvariant()}";
        return s + ".";
    }
}
