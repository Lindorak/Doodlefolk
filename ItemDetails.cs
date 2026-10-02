namespace StickFight;

/// <summary>What an object is made of, which decides its texture in the detailed art style.</summary>
enum Material { Plain, Wood, Fabric, Metal, Plastic, Cardboard, Food, Paper }

/// <summary>The detailed art style: hand-drawn extras for objects (cushion buttons, stitching, speaker grilles, springs,
/// bark, sesame seeds...). They're only drawn when "Detailed" art is on; the simple look stays as it was.</summary>
static class ItemDetails
{
    static Shape R(float x0, float y0, float x1, float y1, int c, bool over = false) => new('r', new[] { x0, y0, x1, y1 }, c, Over: over, NoOutline: true, Detail: true);
    static Shape E(float cx, float cy, float rx, float ry, int c, bool over = false) => new('e', new[] { cx, cy, rx, ry }, c, Over: over, NoOutline: true, Detail: true);
    static Shape L(float x0, float y0, float x1, float y1, int c, float w = 0.6f, bool over = false) => new('l', new[] { x0, y0, x1, y1 }, c, w, Over: over, NoOutline: true, Detail: true);
    static Shape C(int c, float w, params float[] xy) => new('c', xy, c, w, NoOutline: true, Detail: true);
    static Shape CO(int c, float w, params float[] xy) => new('c', xy, c, w, Over: true, NoOutline: true, Detail: true);
    static Shape P(int c, params float[] xy) => new('p', xy, c, NoOutline: true, Detail: true);

    static float[] Zig(float x0, float x1, float yLo, float yHi, int n)
    {
        var p = new List<float>();
        for (int i = 0; i <= n; i++) { p.Add(x0 + (x1 - x0) * i / n); p.Add(i % 2 == 0 ? yLo : yHi); }
        return p.ToArray();
    }

    public static readonly Dictionary<string, Shape[]> Extra = new()
    {
        ["couch"] = new[] { E(-24, 24, 0.9f, 0.9f, 1), E(-8, 24, 0.9f, 0.9f, 1), E(8, 24, 0.9f, 0.9f, 1), E(24, 24, 0.9f, 0.9f, 1),
                            L(-35, 16.6f, 35, 16.6f, 2, 0.5f), L(-31, 14.4f, 31, 14.4f, 2, 0.5f), C(1, 0.5f, -37, 18, -35.5f, 20, -34, 18.5f), C(1, 0.5f, 37, 18, 35.5f, 20, 34, 18.5f) },
        ["armchair"] = new[] { E(-7, 27, 0.8f, 0.8f, 1), E(0, 29, 0.8f, 0.8f, 1), E(7, 27, 0.8f, 0.8f, 1), L(-11, 15, 11, 15, 2, 0.5f) },
        ["tvchair"] = new[] { E(0, 30, 2.4f, 1.2f, 2, true), L(-12, 13.2f, 12, 13.2f, 2, 0.5f) },
        ["bed"] = new[] { E(-38, 34, 1.6f, 1.6f, 4), E(38, 22, 1.4f, 1.4f, 4), C(7, 0.5f, -32, 16.5f, -27, 17.6f, -22, 16.5f), L(-16, 14.6f, 35, 14.6f, 1, 0.6f),
                          L(-16, 13.6f, 35, 13.6f, 2, 0.4f), CO(1, 0.5f, -10, 15.5f, -6, 16.5f, -2, 15.6f), CO(1, 0.5f, 10, 15.6f, 15, 16.6f, 20, 15.6f) },
        ["sleepingbag"] = new[] { L(-16, 3.5f, 31, 3.5f, 2, 0.5f), L(-16, 5.5f, 31, 5.5f, 2, 0.4f) },
        ["beanbag"] = new[] { C(2, 0.6f, -9, 12, -4, 14.5f, 2, 14), E(-8, 10, 2.5f, 1.2f, 2) },
        ["throne"] = new[] { E(-7, 40, 1.3f, 1.3f, 16), E(7, 40, 1.3f, 1.3f, 10), E(0, 31, 1.6f, 1.6f, 22), L(-10, 16.5f, 10, 16.5f, 15, 0.6f) },
        ["radio"] = new[] { E(-6.4f, 6.9f, 0.8f, 0.8f, 7), E(4.6f, 6.9f, 0.8f, 0.8f, 7), L(-1.6f, 10.6f, -1.6f, 11.4f, 8, 0.4f), L(0, 10.6f, 0, 11.4f, 8, 0.4f),
                            L(1.6f, 10.6f, 1.6f, 11.4f, 8, 0.4f), L(-10, 11, 10, 11, 2, 0.5f), R(-9.5f, 1, -1.6f, 2, 1), R(1.6f, 1, 9.5f, 2, 1) },
        ["book"] = new[] { R(3.4f, 0.6f, 4.3f, 10.4f, 7), L(3.4f, 3, 4.3f, 3, 21, 0.3f), L(3.4f, 6, 4.3f, 6, 21, 0.3f), R(-2.8f, 4.5f, 2.8f, 5.5f, 15) },
        ["campfire"] = new[] { E(-14, 1.2f, 2.4f, 1.6f, 5), E(-8.5f, 0.8f, 2, 1.4f, 6), E(8.5f, 0.8f, 2, 1.4f, 6), E(14, 1.2f, 2.4f, 1.6f, 5),
                               L(-9, 2, -3, 3.2f, 13, 0.5f), L(3, 3.2f, 9, 2, 13, 0.5f) },
        ["trampoline"] = new[] { C(5, 0.6f, Zig(-25, 25, 10.6f, 11.6f, 20)), L(-20, 13.3f, 4, 13.3f, 7, 0.5f) },
        ["box"] = new[] { R(-2.2f, 23, 2.2f, 30, 17), C(8, 0.6f, 9, 6, 9, 13), P(8, 7.3f, 13, 10.7f, 13, 9, 16), L(-15, 2, -15, 28, 1, 0.4f), L(15, 2, 15, 28, 1, 0.4f) },
        ["barrel"] = new[] { L(-6, 1, -6, 31, 1, 0.5f, true), L(0, 1, 0, 31, 1, 0.5f, true), L(6, 1, 6, 31, 1, 0.5f, true), E(-9, 7, 0.7f, 0.7f, 5, true), E(9, 25, 0.7f, 0.7f, 5, true) },
        ["tent"] = new[] { CO(1, 0.6f, 0, 2, 0, 38), CO(2, 1.2f, -26, 6, -13, 24), CO(2, 1.2f, 26, 6, 13, 24), E(0, 46.5f, 1.4f, 1.4f, 9) },
        ["table"] = new[] { L(-27, 23.4f, 27, 23.4f, 2, 0.5f), E(-24, 18, 0.8f, 0.8f, 4), E(24, 18, 0.8f, 0.8f, 4) },
        ["bench"] = new[] { E(-24, 14.5f, 0.7f, 0.7f, 5), E(24, 14.5f, 0.7f, 0.7f, 5), E(-24, 21.5f, 0.7f, 0.7f, 5), E(24, 21.5f, 0.7f, 0.7f, 5) },
        ["apple"] = new[] { E(-1.6f, 5.6f, 0.9f, 1.3f, 7), C(1, 0.4f, -2, 1.2f, 0, 0.6f, 2, 1.2f) },
        ["banana"] = new[] { C(1, 0.4f, -4, 2.2f, 0, 3.6f, 4, 3.6f) },
        ["pizza"] = new[] { E(3.5f, 7.5f, 1, 1, 9), E(-4.2f, 8.3f, 0.6f, 0.6f, 10), E(1.2f, 2, 0.5f, 0.5f, 10), L(-6, 9.7f, 6, 9.7f, 13, 0.4f), C(20, 0.8f, -3, 8.6f, -2.7f, 7.4f), C(20, 0.8f, 2.5f, 8.6f, 2.8f, 7.6f) },
        ["burger"] = new[] { E(-3, 7.4f, 0.5f, 0.3f, 7), E(0, 8.3f, 0.5f, 0.3f, 7), E(3, 7.4f, 0.5f, 0.3f, 7), R(-5.8f, 5.3f, 5.8f, 5.8f, 9), C(10, 0.8f, Zig(-6.2f, 6.2f, 4.2f, 4.9f, 8)) },
        ["cake"] = new[] { C(0, 1.1f, -7, 7, -6, 5.6f, -5, 7, -3, 5.2f, -1, 7, 1, 5.6f, 3, 7, 5, 5.2f, 7, 7), E(-4, 2.5f, 0.5f, 0.3f, 16), E(0, 4, 0.5f, 0.3f, 10), E(4, 2.5f, 0.5f, 0.3f, 18), E(-2, 1, 0.5f, 0.3f, 11) },
        ["cookie"] = new[] { C(1, 0.4f, -3, 2, -1, 1.5f) },
        ["watermelon"] = new[] { C(18, 0.6f, -6.6f, 1.8f, 0, 0.4f, 6.6f, 1.8f), E(0, 3.6f, 0.5f, 0.7f, 8), E(-4, 4.4f, 0.5f, 0.7f, 8), E(4, 4.4f, 0.5f, 0.7f, 8) },
        ["icecream"] = new[] { L(-2, 3, 1, 6, 4, 0.4f), L(2, 3, -1, 6, 4, 0.4f), E(-1, 10.2f, 0.7f, 1, 7) },
        ["pillow"] = new[] { L(-7, 3.5f, 7, 3.5f, 2, 0.4f), E(-8, 0.4f, 0.8f, 0.8f, 1), E(8, 0.4f, 0.8f, 0.8f, 1), E(-8, 6.6f, 0.8f, 0.8f, 1), E(8, 6.6f, 0.8f, 0.8f, 1) },
        ["sword"] = new[] { E(0, 4, 1, 1, 16), L(-0.4f, 8, -0.4f, 33, 7, 0.4f) },
        ["snowman"] = new[] { R(-6.5f, 31.5f, 6.5f, 33.5f, 9), L(4.5f, 32, 7, 26.5f, 9, 1.6f), E(-1.6f, 36, 0.5f, 0.5f, 8), E(0, 35.4f, 0.5f, 0.5f, 8), E(1.6f, 36, 0.5f, 0.5f, 8) },
        ["pencil"] = new[] { L(0.8f, 2, 0.8f, 22, 2, 0.6f), L(-2.2f, -2.5f, 2.2f, -2.5f, 2, 0.4f), R(-2.2f, 18, 2.2f, 19, 8) },
        ["goal"] = Array.Empty<Shape>(),
    };

    /// <summary>What an object's main colour stands for, for its texture.</summary>
    public static Material MaterialOf(ItemDef d) => d.Key switch
    {
        "chair" or "stool" or "bench" or "table" or "bat" or "stick" or "barrel" or "throne" => Material.Wood,
        "armchair" or "couch" or "tvchair" or "beanbag" or "bed" or "sleepingbag" or "pillow" or "tent" or "hammock" => Material.Fabric,
        "sword" or "pan" => Material.Metal,
        "radio" or "blaster" or "watergun" or "trampoline" or "umbrella" => Material.Plastic,
        "box" => Material.Cardboard,
        "book" => Material.Paper,
        _ => d.Verbs.Contains(Verb.Eat) ? Material.Food : Material.Plain,
    };
}
