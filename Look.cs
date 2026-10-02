using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>One piece of clothing / hair / headwear. Head pieces are drawn in head-local units (x forward,
/// y up, 1 = head radius); shoes in foot-local units (S). Back shapes go behind the head (long hair, afros).
/// Colour indexes as for objects: 0 the piece's colour, 1 darker, 2 lighter, 3+ ItemDef.Fixed.</summary>
sealed record LookPart(string Key, string Name, Shape[] Front, Shape[]? Back = null);

/// <summary>What a figure is wearing. Each slot holds a part key ("" = nothing) and a colour.</summary>
sealed class Look
{
    public string Hat { get; set; } = "";
    public string HatColour { get; set; } = "#E53935";
    public string Hair { get; set; } = "";
    public string HairColour { get; set; } = "#3B2A20";
    public string Beard { get; set; } = "";
    public string Glasses { get; set; } = "";
    public string Top { get; set; } = "";
    public string TopColour { get; set; } = "#1E88E5";
    public string Neck { get; set; } = "";
    public string NeckColour { get; set; } = "#E53935";
    public string Waist { get; set; } = "";
    public string WaistColour { get; set; } = "#3E2723";
    public string Back { get; set; } = "";
    public string BackColour { get; set; } = "#C62828";
    public string Shoes { get; set; } = "";
    public string ShoeColour { get; set; } = "#F4F4F4";

    public Look Clone() => (Look)MemberwiseClone();

    // ---------------- the catalogue ----------------

    static Shape R(float x0, float y0, float x1, float y1, int c) => new('r', new[] { x0, y0, x1, y1 }, c);
    static Shape O(float x0, float y0, float x1, float y1, float r, int c) => new('o', new[] { x0, y0, x1, y1, r }, c);
    static Shape E(float cx, float cy, float rx, float ry, int c) => new('e', new[] { cx, cy, rx, ry }, c);
    static Shape P(int c, params float[] xy) => new('p', xy, c);
    static Shape L(float x0, float y0, float x1, float y1, int c, float w) => new('l', new[] { x0, y0, x1, y1 }, c, w);
    static Shape C(int c, float w, params float[] xy) => new('c', xy, c, w);
    /// <summary>A filled arc (dome) around (cx, cy), from angle a0 to a1 (degrees, 0 = forward, 90 = up).</summary>
    static Shape Dome(float cx, float cy, float rx, float ry, float a0, float a1, int c)
    {
        var pts = new List<float>();
        for (int i = 0; i <= 12; i++)
        {
            float a = (a0 + (a1 - a0) * i / 12f) * MathF.PI / 180;
            pts.Add(cx + MathF.Cos(a) * rx); pts.Add(cy + MathF.Sin(a) * ry);
        }
        return new('p', pts.ToArray(), c);
    }
    static Shape Ring(float cx, float cy, float r, int c, float w)
    {
        var pts = new List<float>();
        for (int i = 0; i <= 16; i++) { float a = i * MathF.Tau / 16; pts.Add(cx + MathF.Cos(a) * r); pts.Add(cy + MathF.Sin(a) * r); }
        return new('c', pts.ToArray(), c, w);
    }

    public static void AddHats(IEnumerable<LookPart> hats) => Hats = Hats.Concat(hats).ToArray();

    public static LookPart[] Hats =
    {
        new("cap", "Cap", new[] { Dome(0, 0.15f, 1.08f, 1.0f, 0, 180, 0), P(1, 0.25f, 0.12f, 1.7f, 0.05f, 1.65f, 0.25f, 0.3f, 0.32f), E(0, 1.15f, 0.15f, 0.12f, 1) }),
        new("tophat", "Top hat", new[] { R(-0.72f, 0.7f, 0.72f, 2.35f, 0), R(-0.72f, 0.75f, 0.72f, 1.05f, 1), O(-1.3f, 0.6f, 1.3f, 0.86f, 0.12f, 0) }),
        new("beanie", "Beanie", new[] { Dome(0, 0.05f, 1.12f, 1.12f, 0, 180, 0), R(-1.12f, 0.05f, 1.12f, 0.42f, 1), E(0, 1.3f, 0.32f, 0.32f, 2) }),
        new("crown", "Crown", new[] { P(15, -0.8f, 0.7f, 0.8f, 0.7f, 0.85f, 1.6f, 0.42f, 1.15f, 0, 1.75f, -0.42f, 1.15f, -0.85f, 1.6f), E(0, 0.95f, 0.14f, 0.14f, 9), E(0.55f, 0.95f, 0.1f, 0.1f, 16) }),
        new("cowboy", "Cowboy hat", new[] { O(-0.7f, 0.6f, 0.7f, 1.65f, 0.35f, 0), P(0, -1.8f, 0.8f, -1.4f, 0.5f, 1.4f, 0.5f, 1.8f, 0.8f, 1.2f, 0.64f, -1.2f, 0.64f), R(-0.7f, 0.66f, 0.7f, 0.86f, 1) }),
        new("party", "Party hat", new[] { P(0, -0.65f, 0.75f, 0.65f, 0.75f, 0.2f, 2.5f), L(-0.3f, 1.2f, 0.5f, 1.25f, 2, 0.12f), L(-0.1f, 1.75f, 0.35f, 1.8f, 2, 0.12f), E(0.2f, 2.55f, 0.25f, 0.25f, 11) }),
        new("wizard", "Wizard hat", new[] { P(0, -0.8f, 0.8f, 0.8f, 0.8f, 0.15f, 2.3f, -0.6f, 2.9f), O(-1.45f, 0.62f, 1.45f, 0.9f, 0.12f, 0), E(0.05f, 1.45f, 0.16f, 0.16f, 11) }),
        new("helmet", "Helmet", new[] { Dome(0, 0.1f, 1.2f, 1.18f, -5, 185, 0), L(-0.4f, 1.05f, 0.6f, 1.0f, 2, 0.15f), L(-0.85f, 0.1f, -0.2f, -0.75f, 8, 0.1f) }),
        new("viking", "Viking helmet", new[] { P(7, -1.15f, 0.55f, -1.55f, 1.5f, -0.85f, 0.85f), P(7, 1.15f, 0.55f, 1.55f, 1.5f, 0.85f, 0.85f), Dome(0, 0.25f, 1.12f, 1.0f, 0, 180, 5), L(-1.12f, 0.3f, 1.12f, 0.3f, 6, 0.2f) }),
        new("chef", "Chef's hat", new[] { R(-0.75f, 0.65f, 0.75f, 1.2f, 7), E(-0.45f, 1.45f, 0.5f, 0.45f, 7), E(0.1f, 1.6f, 0.55f, 0.5f, 7), E(0.55f, 1.42f, 0.45f, 0.42f, 7) }),
        new("headband", "Headband", new[] { R(-1.02f, 0.32f, 1.02f, 0.62f, 0) }, new[] { P(0, -0.95f, 0.5f, -1.75f, 0.15f, -1.65f, -0.05f, -0.95f, 0.38f), P(0, -0.95f, 0.45f, -1.55f, -0.35f, -1.4f, -0.45f, -0.9f, 0.35f) }),
        new("bow", "Bow", new[] { P(0, 0.1f, 1.0f, -0.55f, 1.45f, -0.65f, 0.75f), P(0, 0.1f, 1.0f, 0.75f, 1.45f, 0.85f, 0.75f), E(0.1f, 1.0f, 0.16f, 0.16f, 1) }),
        new("halo", "Halo", new[] { Ring(0, 1.55f, 0.75f, 15, 0.16f) }),
        // Rare: only from gift crates.
        new("flowercrown", "Flower crown", new[] { C(10, 0.12f, -1.0f, 0.75f, -0.5f, 0.95f, 0, 1.0f, 0.5f, 0.95f, 1.0f, 0.75f), E(-0.8f, 0.85f, 0.2f, 0.2f, 14), E(-0.3f, 1.0f, 0.22f, 0.22f, 11), E(0.2f, 1.02f, 0.22f, 0.22f, 0), E(0.7f, 0.88f, 0.2f, 0.2f, 14), E(-0.3f, 1.0f, 0.07f, 0.07f, 12), E(0.2f, 1.02f, 0.07f, 0.07f, 11) }),
        new("antlers", "Antlers", new[] { C(4, 0.13f, -0.45f, 0.85f, -0.7f, 1.6f, -1.15f, 2.0f), C(4, 0.11f, -0.62f, 1.35f, -0.25f, 1.75f), C(4, 0.13f, 0.45f, 0.85f, 0.7f, 1.6f, 1.15f, 2.0f), C(4, 0.11f, 0.62f, 1.35f, 0.25f, 1.75f) }),
        new("propeller", "Propeller cap", new[] { Dome(0, 0.15f, 1.08f, 1.0f, 0, 180, 0), P(1, 0.25f, 0.12f, 1.7f, 0.05f, 1.65f, 0.25f, 0.3f, 0.32f), L(0, 1.15f, 0, 1.5f, 6, 0.1f), P(9, 0, 1.5f, -0.75f, 1.62f, -0.75f, 1.45f), P(16, 0, 1.5f, 0.75f, 1.62f, 0.75f, 1.45f) }),
        new("pirate", "Pirate hat", new[] { P(8, -1.5f, 0.7f, -0.9f, 1.55f, 0, 1.25f, 0.9f, 1.55f, 1.5f, 0.7f), L(-1.3f, 0.78f, 1.3f, 0.78f, 15, 0.1f), E(0, 1.15f, 0.2f, 0.17f, 7), L(-0.12f, 1.0f, 0.12f, 1.0f, 7, 0.07f) }),
        new("jester", "Jester hat", new[] { Dome(0, 0.2f, 1.1f, 0.9f, 0, 180, 0), P(9, -0.2f, 1.0f, -1.6f, 1.8f, -1.3f, 0.7f), P(16, 0.2f, 1.0f, 1.6f, 1.8f, 1.3f, 0.7f), E(-1.6f, 1.8f, 0.18f, 0.18f, 11), E(1.6f, 1.8f, 0.18f, 0.18f, 11), R(-1.1f, 0.2f, 1.1f, 0.42f, 11) }),
        new("catears", "Cat ears", new[] { P(0, -0.85f, 0.6f, -0.55f, 1.45f, -0.15f, 0.85f), P(0, 0.85f, 0.6f, 0.55f, 1.45f, 0.15f, 0.85f), P(14, -0.68f, 0.78f, -0.55f, 1.2f, -0.35f, 0.9f), P(14, 0.68f, 0.78f, 0.55f, 1.2f, 0.35f, 0.9f) }),
        new("bunnyears", "Bunny ears", new[] { O(-0.6f, 0.7f, -0.2f, 2.2f, 0.2f, 7), O(0.2f, 0.7f, 0.6f, 2.2f, 0.2f, 7), O(-0.5f, 0.85f, -0.3f, 2.0f, 0.1f, 14), O(0.3f, 0.85f, 0.5f, 2.0f, 0.1f, 14) }),
        new("graduation", "Graduation cap", new[] { R(-0.7f, 0.6f, 0.7f, 1.0f, 8), P(8, -1.4f, 1.15f, 0, 1.45f, 1.4f, 1.15f, 0, 0.9f), L(0, 1.18f, 0.95f, 0.65f, 11, 0.08f), E(0.95f, 0.55f, 0.1f, 0.14f, 11) }),
    };

    /// <summary>Hats that only come in gift crates (locked until one turns up).</summary>
    public static readonly string[] RareHats = { "flowercrown", "antlers", "propeller", "pirate", "jester", "catears", "bunnyears", "graduation" };

    public static readonly LookPart[] Hairs =
    {
        new("short", "Short", new[] { Dome(0, 0.1f, 1.06f, 1.04f, 20, 200, 0) }),
        new("spiky", "Spiky", new[] { P(0, -1.0f, 0.2f, -1.25f, 0.9f, -0.62f, 0.78f, -0.6f, 1.4f, -0.12f, 0.98f, 0.12f, 1.5f, 0.4f, 0.98f, 0.9f, 1.15f, 0.95f, 0.45f, 0.55f, 0.62f, 0, 0.56f, -0.6f, 0.5f) }),
        new("mohawk", "Mohawk", new[] { P(0, -0.75f, 0.7f, -0.7f, 1.5f, -0.38f, 0.98f, -0.15f, 1.65f, 0.12f, 0.98f, 0.42f, 1.5f, 0.6f, 0.88f) }),
        new("ponytail", "Ponytail", new[] { Dome(0, 0.1f, 1.06f, 1.04f, 25, 205, 0), E(-0.95f, 0.45f, 0.18f, 0.18f, 1) }),
        new("bun", "Bun", new[] { Dome(0, 0.1f, 1.06f, 1.04f, 25, 205, 0), E(-0.72f, 1.0f, 0.45f, 0.45f, 0) }),
        new("long", "Long", new[] { Dome(0, 0.1f, 1.06f, 1.04f, 20, 200, 0) },
            new[] { P(0, 0.3f, 1.0f, -0.5f, 1.08f, -1.08f, 0.5f, -1.25f, -0.7f, -1.05f, -2.0f, -0.25f, -2.0f, -0.45f, -0.5f, -0.1f, 0.2f) }),
        new("afro", "Afro", new[] { Dome(-0.1f, 0.35f, 1.5f, 1.3f, 15, 200, 0) }, new[] { E(-0.15f, 0.35f, 1.55f, 1.4f, 0) }),
        new("curly", "Curly", new[] { E(-0.75f, 0.7f, 0.42f, 0.42f, 0), E(-0.25f, 1.0f, 0.44f, 0.44f, 0), E(0.35f, 0.95f, 0.42f, 0.42f, 0), E(-1.0f, 0.15f, 0.38f, 0.38f, 0), E(0.75f, 0.65f, 0.32f, 0.32f, 0) }),
        new("bob", "Bob", new[] { Dome(0, 0.1f, 1.08f, 1.05f, 15, 205, 0) },
            new[] { P(0, 0.95f, 0.1f, 0.85f, 0.9f, 0, 1.2f, -0.95f, 0.9f, -1.2f, -0.1f, -1.1f, -0.95f, -0.35f, -0.95f, -0.2f, 0) }),
    };

    public static readonly LookPart[] Beards =
    {
        new("full", "Full beard", new[] { P(0, 1.02f, 0.02f, 0.92f, -0.6f, 0.45f, -1.15f, -0.25f, -1.0f, -0.55f, -0.3f, -0.3f, -0.15f, 0.3f, -0.35f, 0.72f, -0.05f) }),
        new("goatee", "Goatee", new[] { P(0, 0.78f, -0.55f, 0.68f, -1.1f, 0.35f, -1.2f, 0.35f, -0.62f) }),
        new("mustache", "Moustache", new[] { P(0, 1.02f, -0.18f, 0.55f, -0.45f, 0.18f, -0.32f, 0.55f, -0.12f) }),
        new("stubble", "Stubble", new[] { E(0.7f, -0.55f, 0.06f, 0.06f, 0), E(0.45f, -0.75f, 0.06f, 0.06f, 0), E(0.85f, -0.3f, 0.06f, 0.06f, 0), E(0.2f, -0.85f, 0.06f, 0.06f, 0), E(0.6f, -0.8f, 0.05f, 0.05f, 0) }),
    };

    public static readonly LookPart[] GlassesParts =
    {
        new("round", "Round glasses", new[] { Ring(0.62f, 0.18f, 0.3f, 8, 0.11f), L(0.32f, 0.2f, -0.7f, 0.32f, 8, 0.09f) }),
        new("shades", "Sunglasses", new[] { O(0.22f, -0.02f, 1.0f, 0.4f, 0.14f, 8), L(0.25f, 0.28f, -0.7f, 0.35f, 8, 0.1f) }),
        new("monocle", "Monocle", new[] { Ring(0.62f, 0.2f, 0.3f, 15, 0.1f), C(15, 0.06f, 0.62f, -0.1f, 0.5f, -0.6f, 0.2f, -0.95f) }),
        new("goggles", "Goggles", new[] { L(-1.05f, 0.4f, 0.95f, 0.4f, 13, 0.3f), E(0.62f, 0.34f, 0.33f, 0.3f, 16), Ring(0.62f, 0.34f, 0.33f, 6, 0.1f) }),
        new("eyepatch", "Eye patch", new[] { L(-1.0f, 0.75f, 1.0f, -0.05f, 8, 0.08f), E(0.62f, 0.2f, 0.28f, 0.25f, 8) }),
    };

    public static readonly LookPart[] ShoeParts =
    {
        new("sneakers", "Sneakers", new[] { O(-1.6f, 0, 4.6f, 2.5f, 1.1f, 0), L(-1.6f, 0.2f, 4.6f, 0.2f, 7, 0.9f) }),
        new("boots", "Boots", new[] { R(-2.0f, 0, 2.2f, 5.0f, 0), O(-2.0f, 0, 4.4f, 2.4f, 1.0f, 0), L(-2.0f, 0.3f, 4.4f, 0.3f, 1, 0.8f) }),
    };

    /// <summary>Body pieces are drawn in code (they follow several joints).</summary>
    public static readonly (string Slot, string Key, string Name)[] BodyParts =
    {
        ("top", "tee", "T-shirt"), ("top", "tank", "Tank top"), ("top", "hoodie", "Hoodie"),
        ("neck", "tie", "Tie"), ("neck", "bowtie", "Bow tie"), ("neck", "scarf", "Scarf"),
        ("waist", "belt", "Belt"), ("waist", "skirt", "Skirt"),
        ("back", "cape", "Cape"),
    };

    public static LookPart? Find(LookPart[] parts, string key) => key.Length == 0 ? null : parts.FirstOrDefault(p => p.Key == key);

    static readonly string[] HairColours = { "#2B1D16", "#3B2A20", "#6D4C2F", "#A86B32", "#D9B262", "#E8D9A8", "#B33A1F", "#9E9E9E", "#F2F2F2", "#5C6BC0", "#EC407A", "#43A047" };
    static readonly string[] Cloth = { "#E53935", "#1E88E5", "#43A047", "#FB8C00", "#8E24AA", "#FDD835", "#00ACC1", "#EC407A", "#2E2E2E", "#F4F4F4", "#6D4C41", "#283593" };

    /// <summary>Roll an outfit that suits the personality (deterministic for a seed).</summary>
    public static Look Generate(Personality p, int seed, Gender g = Gender.Nonbinary)
    {
        // Gender nudges the odds a little (more long hair and skirts for girls, more beards for boys); nothing is ruled out.
        float girl = g == Gender.Girl ? 1 : 0, boy = g == Gender.Boy ? 1 : 0;
        float longHair = 1 + girl * 1.2f - boy * 0.4f, beard = 1 - girl * 0.95f + boy * 0.6f, skirt = 1 + girl * 1.5f - boy * 0.85f, bow = 1 + girl * 1.2f - boy * 0.6f;
        var r = new Random(seed ^ 0x5eed);
        float E = p.Energy, C = p.Curiosity, B = p.Bravery, Pl = p.Playfulness, A = p.Aggression, So = p.Sociability;
        string Pick(params (float w, string k)[] o)
        {
            float total = o.Sum(x => MathF.Max(0, x.w)), x0 = (float)r.NextDouble() * total;
            foreach (var (w, k) in o) { x0 -= MathF.Max(0, w); if (x0 <= 0) return k; }
            return o[0].k;
        }
        string Col(string[] set) => set[r.Next(set.Length)];
        var l = new Look
        {
            Hat = Pick((3.5f, ""), (Pl * 0.8f, "cap"), (So * (1 - E) * 0.6f, "tophat"), ((1 - E) * 0.5f, "beanie"), (A * B * 0.3f, "crown"),
                       (B * 0.4f, "cowboy"), (Pl * So * 0.6f, "party"), (C * (1 - E) * 0.4f, "wizard"), (B * E * 0.4f, "helmet"), (A * B * 0.3f, "viking"),
                       (0.1f, "chef"), (A * E * 0.6f, "headband"), (So * Pl * 0.3f * bow, "bow"), ((1 - A) * 0.08f, "halo")),
            Hair = Pick((1.5f, ""), (1.2f, "short"), (E * Pl * 1.0f, "spiky"), (A * B * 0.5f, "mohawk"), (So * 0.6f * longHair, "ponytail"), ((1 - E) * 0.4f * longHair, "bun"),
                        (So * 0.6f * longHair, "long"), (Pl * 0.4f, "afro"), (Pl * 0.5f, "curly"), (So * 0.4f * longHair, "bob")),
            Beard = Pick((5f, ""), ((1 - E) * 0.6f * beard, "full"), (C * 0.4f * beard, "goatee"), (A * 0.4f * beard, "mustache"), ((1 - So) * 0.4f * beard, "stubble")),
            Glasses = Pick((5f, ""), (C * 1.0f, "round"), (B * Pl * 0.6f, "shades"), (C * (1 - E) * 0.25f, "monocle"), (E * C * 0.3f, "goggles"), (A * B * 0.15f, "eyepatch")),
            Top = Pick((2.5f, ""), (1f, "tee"), (E * 0.5f, "tank"), ((1 - E) * 0.6f, "hoodie")),
            Neck = Pick((5f, ""), (So * (1 - Pl) * 0.6f, "tie"), (So * 0.4f, "bowtie"), ((1 - E) * 0.5f, "scarf")),
            Waist = Pick((5f, ""), (0.6f, "belt"), (So * 0.6f * skirt, "skirt")),
            Back = Pick((14f, ""), (B * Pl * 1.0f, "cape")),
            Shoes = Pick((2f, ""), (E * 1.2f, "sneakers"), ((1 - E) * 0.6f + B * 0.3f, "boots")),
            HatColour = Col(Cloth), HairColour = Col(HairColours), TopColour = Col(Cloth), NeckColour = Col(Cloth),
            WaistColour = r.NextDouble() < 0.5 ? "#3E2723" : Col(Cloth), BackColour = Col(Cloth), ShoeColour = r.NextDouble() < 0.5 ? "#F4F4F4" : Col(Cloth),
        };
        if (l.Hat is "halo" or "chef") l.HatColour = l.Hat == "chef" ? "#F7F5EF" : "#F2C14E";
        if (l.Hat == "crown") l.HatColour = "#F2C14E";
        return l;
    }
}
