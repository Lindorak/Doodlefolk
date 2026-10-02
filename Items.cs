using System.Numerics;
using System.Text.RegularExpressions;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>Things a figure knows how to do with an object. Objects are described by their verbs, so any object
/// (built in, or one day made up from a typed word) is usable as long as its verbs are ones the brain knows.</summary>
enum Verb
{
    Sit,        // chair, couch, stool: sit on the seat (side view, or facing out of / into the screen)
    Lie,        // bed, mattress, sleeping bag: lie down and nap
    Hammock,    // lie in it and sway
    Bounce,     // trampoline: jump on it, higher and higher
    Stand,      // table, crate: just a surface to stand or sit on
    Eat,        // food: pick it up, sit down, eat it bite by bite
    Hide,       // box, barrel, tent: curl up inside, peek out
    Dance,      // radio: plays music; dancers come over
    Read,       // book: sit and read
    Warm,       // campfire: sit around it, hands out
    Wield,      // sword, bat, frying pan: carry it and swing it in fights
    Shoot,      // toy blaster, water gun: carry it and fire it
    Play,       // sports gear: goals, hoops, nets (figures organise games around it)
    Create,     // the Creator's Pencil: whoever holds it can draw things into the world
    Shelter,    // umbrella: carry it and stay dry
    Lasso,      // lasso: carry it, twirl it, rope a friend, a ball or your cursor
    Collect,    // a trinket: pick it up and keep it
    Tend,       // a plant: water it
    Ride,       // bike, skateboard, go-kart: hop on and ride along
    Swim,       // pond, pool: splash about
    Fish,       // pond: sit at the edge with a rod
}

enum Ammo { Dart, Water, Snow }

/// <summary>How seated figures face.</summary>
enum SeatFacing { Side, Out, In }

/// <summary>One doodle shape in object-local units (S), origin at the bottom centre, y pointing up.
/// Kind: 'r' rect x0 y0 x1 y1, 'o' rounded rect x0 y0 x1 y1 radius, 'e' ellipse cx cy rx ry, 'p' polygon x y ...,
/// 'l' line x0 y0 x1 y1 (width W), 'c' open polyline x y ... (width W). Col: 0 main colour, 1 darker, 2 lighter,
/// 3+ a fixed colour (see ItemDef.Fixed). Over: drawn in front of figures using the object (chair backs, blankets).</summary>
readonly record struct Shape(char Kind, float[] P, int Col, float W = 1.4f, bool Over = false, bool NoOutline = false, bool WhenUsed = false, bool Detail = false);

sealed class ItemDef
{
    public string Key = "", Name = "";
    public string[] Words = Array.Empty<string>();
    public float W, H;                         // bounding size (S units)
    public Color4 Color;
    public Shape[] Shapes = Array.Empty<Shape>();
    public Verb[] Verbs = Array.Empty<Verb>();
    public float Surface = -1, SurfX1, SurfX2; // a standable top (height, x range); -1: none
    public float SeatY, Comfort = 0.5f, Bounce, Mass = 1;
    public Material Material;
    public float[] Seats = Array.Empty<float>();// seat x positions
    public SeatFacing Facing;
    public bool Carry;                         // small enough to carry around (food, books)
    public int Bites = 4;                      // food portions
    // Weapons: held by the grip (origin), pointing along +y.
    public float Reach, Damage = 1, Knock = 1, FireRate = 0.4f;
    /// <summary>Sports gear: segments balls bounce off, as x0 y0 x1 y1 give (object units; give 1 = solid, low = net).</summary>
    public float[] Colliders = Array.Empty<float>();
    /// <summary>Which game this gear is for (Soccer, Basketball, Tennis, Badminton), or null.</summary>
    public string? Sport;
    public Ammo Ammo;
    public bool Weapon => Verbs.Contains(Verb.Wield) || Verbs.Contains(Verb.Shoot);
    public bool Ranged => Verbs.Contains(Verb.Shoot);
    /// <summary>Which tastes decide whether a figure likes this thing.</summary>
    public Thing[] Likes = Array.Empty<Thing>();
    public string Article => "aeiou".Contains(char.ToLowerInvariant(Name[0])) ? "an" : "a";

    public static readonly Color4[] Fixed =
    {
        default, default, default,
        M.Hex(0xA0703C), M.Hex(0x6E4B26), M.Hex(0x9AA3AD), M.Hex(0x5D6670), M.Hex(0xF7F5EF),   // 3 wood, 4 dark wood, 5 metal, 6 dark metal, 7 white
        M.Hex(0x2A2A2A), M.Hex(0xE53935), M.Hex(0x43A047), M.Hex(0xFDD835), M.Hex(0xFB8C00),   // 8 black, 9 red, 10 green, 11 yellow, 12 orange
        M.Hex(0x7B5134), M.Hex(0xF48FB1), M.Hex(0xF2C14E), M.Hex(0x1E88E5), M.Hex(0xE3B26B),   // 13 brown, 14 pink, 15 gold, 16 blue, 17 bread
        M.Hex(0x7CB342), M.Hex(0xFF7043), M.Hex(0xFFD54F), M.Hex(0xEFE6D2), M.Hex(0x8E24AA),   // 18 lettuce, 19 flame, 20 flame light, 21 cream, 22 purple
    };
}

/// <summary>The built-in catalogue, and turning typed words ("a giant red couch") into objects.</summary>
static class ItemCatalog
{
    static Shape R(float x0, float y0, float x1, float y1, int c, bool over = false, bool used = false) => new('r', new float[] { x0, y0, x1, y1 }, c, Over: over, WhenUsed: used);
    static Shape O(float x0, float y0, float x1, float y1, float r, int c, bool over = false) => new('o', new float[] { x0, y0, x1, y1, r }, c, Over: over);
    static Shape E(float cx, float cy, float rx, float ry, int c, bool over = false) => new('e', new[] { cx, cy, rx, ry }, c, Over: over);
    static Shape P(int c, params float[] xy) => new('p', xy, c);
    static Shape POver(int c, params float[] xy) => new('p', xy, c, Over: true);
    static Shape L(float x0, float y0, float x1, float y1, int c, float w = 1.6f) => new('l', new[] { x0, y0, x1, y1 }, c, w);
    static Shape C(int c, float w, params float[] xy) => new('c', xy, c, w);

    public static readonly ItemDef[] All = WithDetails(Build());

    /// <summary>Adds each object's detailed-art extras and its material.</summary>
    static ItemDef[] WithDetails(ItemDef[] defs)
    {
        foreach (var d in defs)
        {
            d.Material = ItemDetails.MaterialOf(d);
            if (ItemDetails.Extra.TryGetValue(d.Key, out var extra)) d.Shapes = d.Shapes.Concat(extra).ToArray();
        }
        return defs;
    }

    static ItemDef[] Build()
    {
        var list = new List<ItemDef>();
        void Add(ItemDef d) => list.Add(d);

        // ---------------- seats ----------------
        Add(new ItemDef
        {
            Key = "chair", Name = "Chair", Words = new[] { "chair", "seat", "dining chair", "wooden chair" }, W = 22, H = 40, Color = M.Hex(0xA0703C),
            Shapes = new[] { L(-8, 0, -8, 16, 1, 2), L(7, 0, 7, 16, 1, 2), R(-9, 15, 9, 18, 0), R(6, 16, 9, 40, 0), L(6.5f, 24, 8.5f, 24, 1), L(6.5f, 32, 8.5f, 32, 1) },
            Verbs = new[] { Verb.Sit }, Seats = new[] { -1f }, SeatY = 18, Facing = SeatFacing.Side, Comfort = 0.4f, Likes = new[] { Thing.Sitting },
        });
        Add(new ItemDef
        {
            Key = "armchair", Name = "Armchair", Words = new[] { "armchair", "arm chair", "recliner", "comfy chair", "easy chair" }, W = 34, H = 34, Color = M.Hex(0x8E24AA),
            Shapes = new[] { O(-15, 18, 15, 34, 5, 0), R(-12, 3, 12, 16, 1), O(-17, 3, -10, 22, 3, 0, true), O(10, 3, 17, 22, 3, 0, true), L(-14, 0, -14, 3, 8, 2), L(14, 0, 14, 3, 8, 2) },
            Verbs = new[] { Verb.Sit }, Seats = new[] { 0f }, SeatY = 15, Facing = SeatFacing.Out, Comfort = 0.85f, Likes = new[] { Thing.Sitting, Thing.Napping },
        });
        Add(new ItemDef
        {
            Key = "couch", Name = "Couch", Words = new[] { "couch", "sofa", "settee", "loveseat", "love seat" }, W = 76, H = 32, Color = M.Hex(0x1E88E5),
            Shapes = new[] { O(-36, 16, 36, 32, 5, 0), R(-32, 3, 32, 15, 1), L(-11, 4, -11, 15, 0, 1), L(11, 4, 11, 15, 0, 1),
                             O(-38, 3, -31, 22, 3, 0, true), O(31, 3, 38, 22, 3, 0, true), L(-34, 0, -34, 3, 8, 2), L(34, 0, 34, 3, 8, 2) },
            Verbs = new[] { Verb.Sit, Verb.Lie }, Seats = new[] { -21f, 0, 21 }, SeatY = 15, Facing = SeatFacing.Out, Comfort = 0.8f, Surface = 15, SurfX1 = -30, SurfX2 = 30,
            Likes = new[] { Thing.Sitting, Thing.Napping, Thing.Chatting }, Mass = 3,
        });
        Add(new ItemDef
        {
            Key = "tvchair", Name = "Watching chair", Words = new[] { "watching chair", "tv chair", "movie chair", "cinema seat", "theater seat", "theatre seat", "gaming chair" }, W = 30, H = 34, Color = M.Hex(0xE53935),
            // Seen from behind: the occupant sits facing into the screen, watching whatever window it's on.
            Shapes = new[] { R(-11, 0, -9, 10, 6), R(9, 0, 11, 10, 6), R(-13, 9, 13, 14, 1), O(-14, 12, 14, 34, 6, 0, true), L(-8, 18, 8, 18, 1), L(-8, 26, 8, 26, 1) },
            Verbs = new[] { Verb.Sit }, Seats = new[] { 0f }, SeatY = 14, Facing = SeatFacing.In, Comfort = 0.8f, Likes = new[] { Thing.Sitting },
        });
        Add(new ItemDef
        {
            Key = "stool", Name = "Stool", Words = new[] { "stool", "bar stool", "barstool", "footstool" }, W = 16, H = 22, Color = M.Hex(0xA0703C),
            Shapes = new[] { L(-6, 0, -4, 20, 1, 2), L(6, 0, 4, 20, 1, 2), L(-5, 8, 5, 8, 1, 1.2f), E(0, 21, 8, 2, 0) },
            Verbs = new[] { Verb.Sit, Verb.Stand }, Seats = new[] { 0f }, SeatY = 22, Facing = SeatFacing.Out, Comfort = 0.3f, Surface = 22, SurfX1 = -7, SurfX2 = 7, Likes = new[] { Thing.Sitting },
        });
        Add(new ItemDef
        {
            Key = "beanbag", Name = "Beanbag", Words = new[] { "beanbag", "bean bag", "beanbag chair", "pouf", "pouffe" }, W = 32, H = 16, Color = M.Hex(0xFB8C00),
            Shapes = new[] { P(0, -15, 0, -16, 6, -10, 14, 2, 16, 12, 12, 16, 5, 15, 0), C(1, 1, -6, 8, 0, 10, 6, 8) },
            Verbs = new[] { Verb.Sit }, Seats = new[] { 0f }, SeatY = 10, Facing = SeatFacing.Out, Comfort = 1f, Likes = new[] { Thing.Sitting, Thing.Napping },
        });
        Add(new ItemDef
        {
            Key = "bench", Name = "Bench", Words = new[] { "bench", "park bench", "pew" }, W = 56, H = 30, Color = M.Hex(0x43A047),
            Shapes = new[] { L(-24, 0, -24, 14, 6, 2), L(24, 0, 24, 14, 6, 2), R(-27, 13, 27, 16, 0), R(-27, 20, 27, 23, 0), R(-27, 26, 27, 29, 0), L(-24, 16, -24, 29, 6, 1.6f), L(24, 16, 24, 29, 6, 1.6f) },
            Verbs = new[] { Verb.Sit }, Seats = new[] { -13f, 13 }, SeatY = 16, Facing = SeatFacing.Out, Comfort = 0.4f, Likes = new[] { Thing.Sitting, Thing.Chatting }, Mass = 2,
        });
        Add(new ItemDef
        {
            Key = "throne", Name = "Throne", Words = new[] { "throne", "royal chair", "king's chair", "kings chair" }, W = 34, H = 54, Color = M.Hex(0xE53935),
            Shapes = new[] { P(15, -14, 0, 14, 0, 14, 44, 10, 54, 0, 48, -10, 54, -14, 44), O(-10, 18, 10, 44, 4, 0), R(-12, 5, 12, 16, 1),
                             O(-17, 3, -11, 26, 2, 15, true), O(11, 3, 17, 26, 2, 15, true), E(0, 49, 2.5f, 2.5f, 9) },
            Verbs = new[] { Verb.Sit }, Seats = new[] { 0f }, SeatY = 16, Facing = SeatFacing.Out, Comfort = 0.9f, Likes = new[] { Thing.Sitting, Thing.HighPlaces },
        });

        // ---------------- beds ----------------
        Add(new ItemDef
        {
            Key = "bed", Name = "Bed", Words = new[] { "bed", "double bed", "single bed", "cot", "four poster" }, W = 80, H = 34, Color = M.Hex(0x1E88E5),
            Shapes = new[] { R(-40, 0, -36, 34, 4), R(36, 0, 40, 22, 4), R(-37, 5, 37, 9, 3), O(-36, 9, 36, 15, 2, 7), O(-34, 14, -20, 19, 3, 7),
                             new('o', new float[] { -18, 9, 37, 17, 2 }, 0, Over: true, WhenUsed: true), O(-18, 13, 37, 16, 2, 2) },
            Verbs = new[] { Verb.Lie }, Surface = 15, SurfX1 = -33, SurfX2 = 34, Comfort = 1f, Likes = new[] { Thing.Napping }, Mass = 3,
        });
        Add(new ItemDef
        {
            Key = "hammock", Name = "Hammock", Words = new[] { "hammock" }, W = 96, H = 46, Color = M.Hex(0xFB8C00),
            Shapes = new[] { L(-46, 0, -44, 46, 4, 3), L(46, 0, 44, 46, 4, 3) },     // the swinging net is drawn by Item
            Verbs = new[] { Verb.Hammock }, Comfort = 1f, Likes = new[] { Thing.Napping, Thing.Sitting }, Mass = 2,
        });
        Add(new ItemDef
        {
            Key = "sleepingbag", Name = "Sleeping bag", Words = new[] { "sleeping bag", "sleepingbag", "bedroll", "futon", "mattress", "air mattress" }, W = 64, H = 8, Color = M.Hex(0x43A047),
            Shapes = new[] { O(-32, 0, 32, 7, 3, 0), O(-30, 1, -18, 7, 3, 2), new('o', new float[] { -16, 0, 32, 9, 3 }, 1, Over: true, WhenUsed: true) },
            Verbs = new[] { Verb.Lie }, Surface = 6, SurfX1 = -30, SurfX2 = 30, Comfort = 0.7f, Likes = new[] { Thing.Napping },
        });

        // ---------------- play ----------------
        Add(new ItemDef
        {
            Key = "trampoline", Name = "Trampoline", Words = new[] { "trampoline", "tramp", "bouncy castle", "bounce house" }, W = 60, H = 14, Color = M.Hex(0x1E88E5),
            Shapes = new[] { L(-26, 0, -22, 11, 8, 2), L(26, 0, 22, 11, 8, 2), L(-3, 0, 0, 11, 8, 2), O(-30, 10, 30, 14, 2, 0), R(-26, 11.5f, 26, 13, 8) },
            Verbs = new[] { Verb.Bounce }, Surface = 14, SurfX1 = -26, SurfX2 = 26, Bounce = 0.92f, Likes = new[] { Thing.Tricks, Thing.HighPlaces }, Mass = 2,
        });
        Add(new ItemDef
        {
            Key = "radio", Name = "Radio", Words = new[] { "radio", "boombox", "boom box", "stereo", "speaker", "jukebox", "music" }, W = 22, H = 15, Color = M.Hex(0xE53935),
            Shapes = new[] { L(-7, 12, -11, 19, 5, 1), O(-11, 0, 11, 12, 2, 0), E(-5.5f, 6, 3.5f, 3.5f, 8), E(5.5f, 6, 3.5f, 3.5f, 8), E(-5.5f, 6, 1.4f, 1.4f, 5), E(5.5f, 6, 1.4f, 1.4f, 5), R(-2, 8, 2, 10, 11) },
            Verbs = new[] { Verb.Dance }, Carry = false, Likes = new[] { Thing.Dancing },
        });
        Add(new ItemDef
        {
            Key = "book", Name = "Book", Words = new[] { "book", "novel", "comic", "comic book", "manga", "magazine", "storybook" }, W = 9, H = 11, Color = M.Hex(0xE53935),
            Shapes = new[] { R(-4.5f, 0, 4.5f, 11, 0), R(-4.5f, 0, -3.3f, 11, 1), L(-2, 8, 3, 8, 7, 0.8f) },
            Verbs = new[] { Verb.Read }, Carry = true, Likes = new[] { Thing.Exploring },
        });
        Add(new ItemDef
        {
            Key = "campfire", Name = "Campfire", Words = new[] { "campfire", "camp fire", "fire", "bonfire", "fireplace", "fire pit" }, W = 28, H = 14, Color = M.Hex(0xFF7043),
            Shapes = new[] { L(-12, 1, 12, 5, 4, 3.4f), L(-12, 5, 12, 1, 3, 3.4f) },     // flames are drawn live by Item
            Verbs = new[] { Verb.Warm }, Likes = new[] { Thing.Chatting, Thing.Sitting },
        });

        Add(new ItemDef
        {
            Key = "lasso", Name = "Lasso", Words = new[] { "lasso", "lariat", "rope", "cowboy rope", "lasso rope" }, W = 14, H = 14, Color = M.Hex(0xC29A5B),
            Shapes = new[] { new Shape('e', new[] { 0f, 9, 6, 5 }, 0, 1.6f, NoOutline: true), C(1, 1.6f, 0, 4, -1, 2, 1, 0), C(0, 1.4f, -6, 9, -4, 13, 0, 14, 4, 13, 6, 9, 4, 5, 0, 4, -4, 5, -6, 9) },
            Verbs = new[] { Verb.Lasso }, Carry = true, Damage = 0.15f, Reach = 10, Likes = new[] { Thing.Tricks },
        });

        // ---------------- trinkets (collectibles) ----------------
        ItemDef Trinket(string key, string name, string[] words, float w, float h, uint col, Shape[] shapes) => new()
        {
            Key = key, Name = name, Words = words, W = w, H = h, Color = M.Hex(col), Shapes = shapes, Verbs = new[] { Verb.Collect }, Carry = true, Mass = 0.2f, Likes = new[] { Thing.Exploring },
        };
        Add(Trinket("marble", "Marble", new[] { "marble", "marbles" }, 5, 5, 0x29B6F6, new[] { E(0, 2.5f, 2.5f, 2.5f, 0), C(7, 0.7f, -1.5f, 2.5f, 0, 3.5f, 1.5f, 2) }));
        Add(Trinket("button", "Button", new[] { "button", "shiny button" }, 6, 2, 0xE53935, new[] { E(0, 1, 3, 1.1f, 0), E(-0.8f, 1, 0.4f, 0.3f, 1), E(0.8f, 1, 0.4f, 0.3f, 1) }));
        Add(Trinket("seashell", "Seashell", new[] { "seashell", "shell", "sea shell" }, 7, 5, 0xF8BBD0, new[] { P(0, -3.5f, 0, 3.5f, 0, 0, 5), L(0, 0.2f, -2, 3.5f, 1, 0.6f), L(0, 0.2f, 0, 4.5f, 1, 0.6f), L(0, 0.2f, 2, 3.5f, 1, 0.6f) }));
        Add(Trinket("feather", "Feather", new[] { "feather", "pretty feather" }, 9, 3, 0x7E57C2, new[] { P(0, -4.5f, 0.8f, 0, 2.6f, 4.5f, 1.2f, 0, 0.2f), L(-4.5f, 0.6f, 4.5f, 1.4f, 7, 0.5f) }));
        Add(Trinket("bottlecap", "Bottle cap", new[] { "bottle cap", "bottlecap", "cap" }, 5, 2, 0xFDD835, new[] { R(-2.5f, 0, 2.5f, 1.8f, 0), L(-2.5f, 1.8f, 2.5f, 1.8f, 1, 0.6f) }));
        Add(Trinket("coin", "Shiny coin", new[] { "coin", "shiny coin", "penny", "gold coin" }, 5, 5, 0xF2C14E, new[] { E(0, 2.5f, 2.5f, 2.5f, 0), E(0, 2.5f, 1.5f, 1.5f, 2) }));
        Add(Trinket("gem", "Gem", new[] { "gem", "jewel", "crystal", "diamond", "ruby" }, 5, 5, 0x26C6DA, new[] { P(0, -2.5f, 3, 0, 5, 2.5f, 3, 0, 0), L(-2.5f, 3, 2.5f, 3, 2, 0.5f) }));

        // ---------------- garden ----------------
        Add(new ItemDef
        {
            Key = "seedpatch", Name = "Seed patch", Words = new[] { "seed", "seeds", "plant a seed", "garden", "flower bed", "seed patch" }, W = 18, H = 4, Color = M.Hex(0x6D4C41),
            Shapes = new[] { E(0, 1.5f, 9, 2.2f, 0), E(-3, 2.6f, 1, 0.6f, 1), E(2.5f, 2.8f, 1, 0.6f, 1) }, Verbs = new[] { Verb.Tend }, Mass = 1,
        });
        Add(new ItemDef
        {
            Key = "sprout", Name = "Sprout", Words = new[] { "sprout", "seedling" }, W = 18, H = 10, Color = M.Hex(0x6D4C41),
            Shapes = new[] { E(0, 1.5f, 9, 2.2f, 0), L(0, 2, 0, 8, 18, 1.2f), E(-2.2f, 8.2f, 2.2f, 1.1f, 18), E(2.2f, 8.2f, 2.2f, 1.1f, 18) }, Verbs = new[] { Verb.Tend }, Mass = 1,
        });
        Add(new ItemDef
        {
            Key = "bud", Name = "Budding plant", Words = new[] { "bud", "young plant", "plant" }, W = 18, H = 20, Color = M.Hex(0x6D4C41),
            Shapes = new[] { E(0, 1.5f, 9, 2.2f, 0), L(0, 2, 0, 17, 10, 1.6f), E(-3.5f, 9, 3.5f, 1.4f, 18), E(3.5f, 12, 3.5f, 1.4f, 18), E(0, 18.5f, 1.8f, 2.6f, 10) }, Verbs = new[] { Verb.Tend }, Mass = 1,
        });
        Add(new ItemDef
        {
            Key = "tulip", Name = "Tulip", Words = new[] { "tulip", "flower", "flowers", "rose", "daisy" }, W = 18, H = 26, Color = M.Hex(0xE53935),
            Shapes = new[] { E(0, 1.5f, 9, 2.2f, 13), L(0, 2, 0, 19, 10, 1.6f), E(-3.5f, 9, 3.5f, 1.4f, 18), E(3.5f, 13, 3.5f, 1.4f, 18), P(0, -3.5f, 19, 3.5f, 19, 4, 25, 1.5f, 23, 0, 26, -1.5f, 23, -4, 25) }, Verbs = new[] { Verb.Tend }, Mass = 1, Likes = new[] { Thing.Sitting },
        });
        Add(new ItemDef
        {
            Key = "sunflower", Name = "Sunflower", Words = new[] { "sunflower", "sunflowers" }, W = 22, H = 40, Color = M.Hex(0xFDD835),
            Shapes = new[] { E(0, 1.5f, 9, 2.2f, 13), L(0, 2, 0, 33, 10, 2), E(-4.5f, 14, 4.5f, 1.8f, 18), E(4.5f, 21, 4.5f, 1.8f, 18), E(0, 34, 8, 8, 0), E(0, 34, 4, 4, 13) }, Verbs = new[] { Verb.Tend }, Mass = 1,
        });
        Add(new ItemDef
        {
            Key = "tomatoplant", Name = "Tomato plant", Words = new[] { "tomato plant", "tomato", "tomatoes", "vegetable", "veggie patch" }, W = 22, H = 26, Color = M.Hex(0xE53935),
            Shapes = new[] { E(0, 1.5f, 9, 2.2f, 13), L(0, 2, 0, 24, 10, 1.6f), L(0, 12, -6, 18, 10, 1.2f), L(0, 15, 6, 21, 10, 1.2f), E(-5, 9, 4, 2, 18), E(5, 14, 4, 2, 18), E(-6, 16, 2.2f, 2.2f, 0), E(6, 19, 2.2f, 2.2f, 0), E(2, 25, 2.2f, 2.2f, 0) }, Verbs = new[] { Verb.Tend }, Mass = 1,
        });

        // ---------------- seasonal ----------------
        Add(new ItemDef
        {
            Key = "pumpkin", Name = "Jack-o'-lantern", Words = new[] { "pumpkin", "jack o lantern", "jack-o'-lantern", "jackolantern" }, W = 18, H = 15, Color = M.Hex(0xFB8C00),
            Shapes = new[] { E(-4.5f, 7, 5, 7, 0), E(4.5f, 7, 5, 7, 0), E(0, 7, 6, 7.5f, 0), L(0, 14, 1, 17, 10, 1.6f), P(11, -5, 9, -2, 9, -3.5f, 11), P(11, 2, 9, 5, 9, 3.5f, 11), P(11, -5, 4, 5, 4, 3, 2, 0, 3.4f, -3, 2) }, Mass = 1.5f,
        });
        Add(new ItemDef
        {
            Key = "xmastree", Name = "Christmas tree", Words = new[] { "christmas tree", "xmas tree", "tree", "fir tree", "pine tree" }, W = 34, H = 52, Color = M.Hex(0x2E7D32),
            Shapes = new[] { R(-3, 0, 3, 7, 4), P(0, -17, 7, 17, 7, 0, 26), P(0, -14, 19, 14, 19, 0, 38), P(0, -10, 30, 10, 30, 0, 48), P(15, 0, 52.5f, 1.4f, 48.5f, -1.4f, 48.5f), E(-8, 12, 1.8f, 1.8f, 9), E(6, 15, 1.8f, 1.8f, 16), E(-4, 25, 1.8f, 1.8f, 11), E(5, 32, 1.8f, 1.8f, 9), E(-3, 38, 1.6f, 1.6f, 16) },
            Mass = 3,
        });

        Add(new ItemDef
        {
            Key = "fishtank", Name = "Fish tank", Words = new[] { "fish tank", "aquarium", "fishbowl", "fish bowl", "fish", "goldfish" }, W = 44, H = 30, Color = M.Hex(0x4FC3F7),
            Shapes = new[] { R(-22, 0, 22, 3, 6), R(-21, 3, 21, 6, 17), L(-21, 29, 21, 29, 6, 1.4f), L(-21, 3, -21, 29, 6, 1.2f), L(21, 3, 21, 29, 6, 1.2f) },
            Mass = 3, Surface = 30, SurfX1 = -20, SurfX2 = 20,
        });

        Add(new ItemDef
        {
            Key = "hamsterwheel", Name = "Hamster wheel", Words = new[] { "hamster wheel", "exercise wheel", "running wheel", "wheel" }, W = 22, H = 26, Color = M.Hex(0x26C6DA),
            Shapes = new[] { R(-11, 0, 11, 2.5f, 0), L(-8, 2, 0, 13, 5, 1.6f), L(8, 2, 0, 13, 5, 1.6f) },
            Mass = 1,
        });

        // ---------------- town: work, school and building ----------------
        Add(new ItemDef
        {
            Key = "shopstall", Name = "Shop stall", Words = new[] { "shop", "shop stall", "store", "market stall", "stall", "kiosk" }, W = 50, H = 44, Color = M.Hex(0xE53935),
            Shapes = new[] { L(-22, 0, -22, 40, 4, 2), L(22, 0, 22, 40, 4, 2), R(-25, 18, 25, 22, 3, true), R(-24, 0, 24, 18, 13, true),
                             P(0, -27, 38, 27, 38, 23, 46, -23, 46), L(-20, 38, -17, 34, 7, 2), L(-10, 38, -7, 34, 7, 2), L(0, 38, 3, 34, 7, 2), L(10, 38, 13, 34, 7, 2), L(20, 38, 23, 34, 7, 2),
                             R(-18, 22, -12, 27, 16, true), R(-8, 22, -1, 29, 11, true), R(4, 22, 9, 26, 10, true) },
            Mass = 3, Likes = new[] { Thing.Chatting },
        });
        Add(new ItemDef
        {
            Key = "foodcart", Name = "Food cart", Words = new[] { "food cart", "food truck", "hot dog stand", "cafe", "café", "restaurant", "kitchen" }, W = 44, H = 40, Color = M.Hex(0xFDD835),
            Shapes = new[] { E(-14, 4, 4.5f, 4.5f, 8), E(14, 4, 4.5f, 4.5f, 8), E(-14, 4, 1.6f, 1.6f, 5), E(14, 4, 1.6f, 1.6f, 5), O(-22, 6, 22, 26, 2, 0), R(-22, 24, 22, 27, 7),
                             L(-18, 27, -18, 38, 5, 1.4f), L(18, 27, 18, 38, 5, 1.4f), P(9, -22, 36, 22, 36, 18, 41, -18, 41), R(-8, 12, 8, 20, 7), L(-6, 16, 6, 16, 9, 1.4f) },
            Verbs = new[] { Verb.Stand }, Surface = 27, SurfX1 = -21, SurfX2 = 21, Mass = 3, Likes = new[] { Thing.Eating },
        });
        Add(new ItemDef
        {
            Key = "stage", Name = "Stage", Words = new[] { "stage", "bandstand", "theatre", "theater", "performance stage" }, W = 80, H = 14, Color = M.Hex(0x8E24AA),
            Shapes = new[] { R(-40, 0, 40, 10, 3), R(-40, 10, 40, 14, 4), L(-38, 14, -38, 46, 0, 2.6f), L(38, 14, 38, 46, 0, 2.6f), P(0, -40, 46, 40, 46, 30, 40, 0, 43, -30, 40),
                             E(-24, 6, 2, 2, 11), E(0, 6, 2, 2, 11), E(24, 6, 2, 2, 11) },
            Verbs = new[] { Verb.Stand }, Surface = 14, SurfX1 = -39, SurfX2 = 39, Mass = 4, Likes = new[] { Thing.Dancing },
        });
        Add(new ItemDef
        {
            Key = "schoolboard", Name = "School chalkboard", Words = new[] { "school", "chalkboard", "blackboard", "classroom", "whiteboard" }, W = 46, H = 40, Color = M.Hex(0x2E5D4B),
            Shapes = new[] { L(-18, 0, -14, 14, 3, 2), L(18, 0, 14, 14, 3, 2), R(-23, 12, 23, 40, 3), R(-21, 14, 21, 38, 0), L(-15, 31, -2, 31, 7, 0.8f), L(-15, 26, 8, 26, 7, 0.8f), L(-15, 21, 4, 21, 7, 0.8f), R(-8, 12.5f, 8, 14, 7) },
            Mass = 2,
        });
        Add(new ItemDef
        {
            Key = "buildsite", Name = "Building site", Words = new[] { "building site", "construction site", "scaffolding" }, W = 60, H = 50, Color = M.Hex(0xA0703C),
            Shapes = new[] { R(-30, 0, 30, 2.5f, 13) }, Mass = 3,
        });
        Add(new ItemDef
        {
            Key = "fort", Name = "Blanket fort", Words = new[] { "fort", "blanket fort", "pillow fort", "den" }, W = 64, H = 40, Color = M.Hex(0x1E88E5),
            Shapes = new[] { L(-28, 0, -28, 30, 3, 2.4f), L(28, 0, 28, 30, 3, 2.4f), P(0, -32, 30, 0, 40, 32, 30, 30, 4, 18, 0, -18, 0, -30, 4), POver(1, -10, 0, -12, 18, 12, 18, 10, 0),
                             L(-30, 30, -30, 26, 2, 3), L(0, 40, 0, 50, 4, 1.2f), P(9, 0, 50, 9, 47, 0, 44) },
            Verbs = new[] { Verb.Hide, Verb.Lie }, Comfort = 0.8f, Mass = 2, Likes = new[] { Thing.Napping, Thing.Exploring }, Material = Material.Fabric,
        });
        Add(new ItemDef
        {
            Key = "treehouse", Name = "Treehouse", Words = new[] { "treehouse", "tree house", "tree fort" }, W = 76, H = 110, Color = M.Hex(0xA0703C),
            Shapes = new[] { R(-7, 0, 7, 62, 4), L(-6, 30, -24, 50, 4, 4), E(-18, 92, 22, 18, 18), E(18, 96, 22, 16, 18), E(0, 104, 24, 14, 18), R(-30, 60, 30, 64, 4),
                             R(-26, 64, 26, 86, 0), P(13, -30, 86, 0, 100, 30, 86), R(-6, 64, 6, 78, 4), R(14, 72, 22, 80, 7), L(26, 0, 26, 62, 3, 1.4f), L(31, 0, 31, 62, 3, 1.4f),
                             L(26, 12, 31, 12, 3, 1.2f), L(26, 24, 31, 24, 3, 1.2f), L(26, 36, 31, 36, 3, 1.2f), L(26, 48, 31, 48, 3, 1.2f) },
            Verbs = new[] { Verb.Stand, Verb.Hide }, Surface = 64, SurfX1 = -28, SurfX2 = 28, Mass = 6, Likes = new[] { Thing.HighPlaces, Thing.Climbing }, Material = Material.Wood,
        });

        // ---------------- vehicles, water and lights ----------------
        Add(new ItemDef
        {
            Key = "bike", Name = "Bike", Words = new[] { "bike", "bicycle", "cycle", "push bike" }, W = 40, H = 26, Color = M.Hex(0xE53935),
            Shapes = new[] { L(-13, 6, -3, 7, 0, 2), L(-3, 7, -6, 18, 0, 2), L(-6, 18, -13, 6, 0, 2), L(-3, 7, 10, 16, 0, 2), L(-6, 17, 10, 17, 0, 2), L(10, 17, 13, 6, 0, 2),
                             L(10, 17, 9, 22, 6, 1.6f), L(7, 22, 12, 23, 8, 1.8f), L(-9, 19, -3, 19, 8, 2.6f) },
            Verbs = new[] { Verb.Ride }, Mass = 2, Likes = new[] { Thing.Exploring, Thing.Tricks }, Material = Material.Metal,
        });
        Add(new ItemDef
        {
            Key = "skateboard", Name = "Skateboard", Words = new[] { "skateboard", "skate board", "longboard", "skate" }, W = 26, H = 5, Color = M.Hex(0x1E88E5),
            Shapes = new[] { O(-12, 2, 12, 3.8f, 1.6f, 0), L(-12, 3, -14, 4.6f, 0, 1.6f), L(12, 3, 14, 4.6f, 0, 1.6f), R(-9, 1.2f, -6, 2, 6), R(6, 1.2f, 9, 2, 6) },
            Verbs = new[] { Verb.Ride }, Mass = 1, Likes = new[] { Thing.Tricks }, Material = Material.Wood,
        });
        Add(new ItemDef
        {
            Key = "gokart", Name = "Go-kart", Words = new[] { "go-kart", "go kart", "gokart", "kart", "race car", "car", "soapbox" }, W = 40, H = 16, Color = M.Hex(0x43A047),
            Shapes = new[] { R(-18, 3, 18, 5.5f, 6), R(-13, 6, -10, 16, 8), L(6, 11, 9, 15, 8, 1.6f), E(9.5f, 15.5f, 2.2f, 0.9f, 8),
                             POver(0, -16, 5, -14, 11, 4, 11, 12, 8, 18, 5), POver(1, 10, 5, 18, 5, 18, 7, 12, 8), new Shape('e', new[] { 0f, 8.5f, 2.2f, 2.2f }, 7, Over: true) },
            Verbs = new[] { Verb.Ride }, Mass = 4, Likes = new[] { Thing.Tricks, Thing.Exploring }, Material = Material.Metal,
        });
        Add(new ItemDef
        {
            Key = "pond", Name = "Pond", Words = new[] { "pond", "lake", "duck pond", "ducks", "duck", "fishing", "fishing pond" }, W = 150, H = 10, Color = M.Hex(0x4FA3D9),
            Shapes = new[] { E(0, 4, 76, 5.5f, 13), E(0, 4.6f, 72, 4.2f, 0), E(-40, 6.4f, 7, 1.5f, 10), E(32, 6, 6, 1.3f, 10), L(-70, 4, -73, 22, 10, 1.2f), L(-66, 4, -65, 18, 10, 1.2f), L(-68, 4, -69, 25, 10, 1.2f),
                             E(-73, 21, 0.9f, 2.6f, 13), E(-69, 24, 0.9f, 2.6f, 13), L(68, 4, 70, 16, 10, 1.2f) },
            Verbs = new[] { Verb.Swim, Verb.Fish }, Mass = 20, Likes = new[] { Thing.Exploring },
        });
        Add(new ItemDef
        {
            Key = "pool", Name = "Swimming pool", Words = new[] { "pool", "swimming pool", "paddling pool", "swimming", "swim" }, W = 110, H = 12, Color = M.Hex(0x29B6F6),
            Shapes = new[] { R(-55, 0, 55, 12, 7), R(-51, 2, 51, 10.5f, 0), L(44, 10, 44, 24, 5, 1.4f), L(49, 10, 49, 24, 5, 1.4f), L(44, 14, 49, 14, 5, 1.2f), L(44, 18, 49, 18, 5, 1.2f), L(44, 22, 49, 22, 5, 1.2f) },
            Verbs = new[] { Verb.Swim }, Mass = 20, Likes = new[] { Thing.Exploring },
        });
        Add(new ItemDef
        {
            Key = "lamp", Name = "Lamp", Words = new[] { "lamp", "floor lamp", "street lamp", "lamppost", "lamp post", "light" }, W = 18, H = 62, Color = M.Hex(0xFFE082),
            Shapes = new[] { E(0, 1.5f, 7, 1.6f, 6), L(0, 2, 0, 50, 5, 1.6f), E(0, 47, 2.2f, 2.2f, 7), P(0, -8, 48, 8, 48, 5, 60, -5, 60) },
            Mass = 2, Material = Material.Metal,
        });
        Add(new ItemDef
        {
            Key = "fairylights", Name = "Fairy lights", Words = new[] { "fairy lights", "string lights", "christmas lights", "lights", "bunting" }, W = 150, H = 50, Color = M.Hex(0xFFD54F),
            Shapes = new[] { L(-72, 0, -72, 48, 3, 2), L(72, 0, 72, 48, 3, 2), C(8, 0.7f, -72, 46, -48, 37, -24, 33, 0, 32, 24, 33, 48, 37, 72, 46),
                             E(-48, 35.5f, 1.8f, 2.2f, 0), E(-24, 31.5f, 1.8f, 2.2f, 9), E(0, 30.5f, 1.8f, 2.2f, 10), E(24, 31.5f, 1.8f, 2.2f, 16), E(48, 35.5f, 1.8f, 2.2f, 22) },
            Mass = 2,
        });
        Add(new ItemDef
        {
            Key = "lantern", Name = "Lantern", Words = new[] { "lantern", "paper lantern", "oil lamp", "candle" }, W = 10, H = 18, Color = M.Hex(0xFF7043),
            Shapes = new[] { R(-4.5f, 0, 4.5f, 2, 6), O(-4, 2, 4, 13, 2, 0), R(-2.5f, 13, 2.5f, 15, 6), C(6, 1, -2, 15, 0, 18, 2, 15), L(-2, 3, -2, 12, 1, 0.6f), L(2, 3, 2, 12, 1, 0.6f) },
            Carry = true, Mass = 0.6f,
        });
        Add(new ItemDef
        {
            Key = "fish", Name = "Fish", Words = new[] { "fish", "trout", "a fish" }, W = 12, H = 5, Color = M.Hex(0x90A4AE),
            Shapes = new[] { P(0, 3, 2.5f, 7, 5, 7, 0), E(-1, 2.5f, 5, 2.4f, 0), E(-4, 3, 0.7f, 0.7f, 8), L(-1, 4.5f, 1, 1, 1, 0.6f) },
            Verbs = new[] { Verb.Eat }, Carry = true, Bites = 3, Mass = 0.4f, Likes = new[] { Thing.Eating },
        });
        Add(new ItemDef
        {
            Key = "finishflag", Name = "Finish flag", Words = new[] { "finish line", "finish flag", "chequered flag", "checkered flag" }, W = 18, H = 42, Color = M.Hex(0x2A2A2A),
            Shapes = new[] { L(-7, 0, -7, 42, 6, 1.6f), R(-7, 30, 9, 41, 7), R(-7, 36, -3, 41, 8), R(1, 36, 5, 41, 8), R(-3, 30, 1, 36, 8), R(5, 30, 9, 36, 8) },
            Mass = 1,
        });

        // ---------------- pet care ----------------
        Add(new ItemDef
        {
            Key = "foodbowl", Name = "Food bowl", Words = new[] { "food bowl", "pet bowl", "dog bowl", "cat bowl", "pet food", "dog food", "cat food", "kibble", "bird seed", "seed dish", "feeder", "bowl" }, W = 18, H = 6, Color = M.Hex(0xE53935),
            Shapes = new[] { P(0, -9, 6, 9, 6, 6.5f, 0, -6.5f, 0), L(-9, 6, 9, 6, 1, 1.4f) },
            Mass = 1,
        });
        Add(new ItemDef
        {
            Key = "waterbowl", Name = "Water bowl", Words = new[] { "water bowl", "water dish", "pet water", "drinking bowl", "water" }, W = 18, H = 6, Color = M.Hex(0x1E88E5),
            Shapes = new[] { P(0, -9, 6, 9, 6, 6.5f, 0, -6.5f, 0), L(-9, 6, 9, 6, 1, 1.4f) },
            Mass = 1,
        });
        Add(new ItemDef
        {
            Key = "litterbox", Name = "Litter box", Words = new[] { "litter box", "litter tray", "kitty litter", "cat litter", "litterbox", "cat toilet" }, W = 34, H = 9, Color = M.Hex(0x78909C),
            Shapes = new[] { O(-17, 0, 17, 9, 2, 0), R(-15, 2, 15, 7, 1), L(-17, 9, 17, 9, 1, 1.2f) },
            Mass = 2,
        });
        Add(new ItemDef
        {
            Key = "peepad", Name = "Pee pad", Words = new[] { "pee pad", "puppy pad", "training pad", "wee pad", "potty pad" }, W = 30, H = 2, Color = M.Hex(0xF7F5EF),
            Shapes = new[] { R(-15, 0, 15, 1.6f, 0), R(-10, 0.4f, 10, 1.5f, 16) },
            Mass = 0.5f,
        });
        Add(new ItemDef
        {
            Key = "petbed", Name = "Pet bed", Words = new[] { "pet bed", "dog bed", "cat bed", "pet basket", "dog basket", "cat basket", "cushion" }, W = 34, H = 9, Color = M.Hex(0x8E24AA),
            Shapes = new[] { O(-17, 0, 17, 9, 4, 0), E(0, 6.5f, 13, 3, 2) },
            Mass = 1, Material = Material.Fabric,
        });
        Add(new ItemDef
        {
            Key = "perch", Name = "Bird perch", Words = new[] { "perch", "bird perch", "bird stand", "birdcage", "bird cage", "parrot perch", "play stand" }, W = 22, H = 40, Color = M.Hex(0xA0703C),
            Shapes = new[] { R(-11, 0, 11, 2.5f, 6), L(0, 2, 0, 38, 4, 2.2f), L(-10, 38, 10, 38, 3, 2.4f), E(7, 22, 4, 1.4f, 7), L(3, 22, 0, 22, 4, 1.2f) },
            Surface = 39, SurfX1 = -9, SurfX2 = 9, Mass = 2,
        });
        Add(new ItemDef
        {
            Key = "chewtoy", Name = "Chew toy", Words = new[] { "chew toy", "bone", "dog toy", "squeaky toy", "squeaky", "dog bone", "toy bone" }, W = 14, H = 5, Color = M.Hex(0xF7F5EF),
            Shapes = new[] { E(-5.2f, 2.5f, 2.4f, 2.4f, 0), E(5.2f, 2.5f, 2.4f, 2.4f, 0), R(-5, 1.3f, 5, 3.7f, 0) },
            Carry = true, Mass = 0.4f,
        });
        Add(new ItemDef
        {
            Key = "yarn", Name = "Ball of yarn", Words = new[] { "yarn", "ball of yarn", "wool", "ball of wool", "cat toy", "string" }, W = 9, H = 9, Color = M.Hex(0xE53935),
            Shapes = new[] { E(0, 4.5f, 4.5f, 4.5f, 0), C(1, 0.8f, -3, 6.5f, 0, 2, 3, 6.5f), C(1, 0.8f, -3.5f, 3, 0, 7.5f, 3.5f, 3), C(1, 0.8f, 3.5f, 1, 7, -0.5f) },
            Carry = true, Mass = 0.3f, Bounce = 0.3f,
        });
        Add(new ItemDef
        {
            Key = "scratchpost", Name = "Scratching post", Words = new[] { "scratching post", "scratch post", "cat tree", "cat tower", "scratcher" }, W = 18, H = 34, Color = M.Hex(0x8E24AA),
            Shapes = new[] { R(-10, 0, 10, 3, 0), R(-3.5f, 3, 3.5f, 30, 17), L(-3.5f, 9, 3.5f, 11, 4, 0.8f), L(-3.5f, 16, 3.5f, 18, 4, 0.8f), L(-3.5f, 23, 3.5f, 25, 4, 0.8f), O(-9, 30, 9, 34, 1.5f, 0) },
            Surface = 34, SurfX1 = -8, SurfX2 = 8, Mass = 2, Material = Material.Fabric,
        });
        // Messes (click them to clean up).
        Add(new ItemDef
        {
            Key = "puddle", Name = "Puddle", Words = new[] { "puddle", "pee" }, W = 20, H = 1, Color = M.Hex(0xF3E58A),
            Shapes = new[] { new Shape('e', new[] { 0f, 0.5f, 10, 1.1f }, 0, NoOutline: true), new Shape('e', new[] { -4f, 0.7f, 3, 0.5f }, 2, NoOutline: true) },
            Mass = 0.1f,
        });
        Add(new ItemDef
        {
            Key = "poop", Name = "Poop", Words = new[] { "poop", "poo", "dog poop", "doo doo", "mess" }, W = 7, H = 5, Color = M.Hex(0x7B5134),
            Shapes = new[] { E(0, 1.2f, 3.5f, 1.4f, 0), E(0, 2.8f, 2.5f, 1.2f, 0), E(0.3f, 4.2f, 1.4f, 1, 0) },
            Mass = 0.2f,
        });
        Add(new ItemDef
        {
            Key = "dropping", Name = "Bird dropping", Words = new[] { "bird dropping", "bird poop" }, W = 4, H = 2, Color = M.Hex(0xF7F5EF),
            Shapes = new[] { E(0, 0.8f, 1.8f, 0.9f, 0), E(0.4f, 1, 0.6f, 0.5f, 8) },
            Mass = 0.1f,
        });

        // ---------------- hide / stand ----------------
        Add(new ItemDef
        {
            Key = "box", Name = "Box", Words = new[] { "box", "cardboard box", "crate", "wooden crate", "chest", "toy box" }, W = 34, H = 30, Color = M.Hex(0xC8A26B),
            Shapes = new[] { R(-17, 0, 17, 30, 1), new('r', new float[] { -17, 0, 17, 30 }, 0, Over: true), L(-17, 30, -22, 36, 1, 1.8f), L(17, 30, 22, 36, 1, 1.8f), L(-10, 15, 10, 15, 1, 1) },
            Verbs = new[] { Verb.Hide, Verb.Stand }, Surface = 30, SurfX1 = -16, SurfX2 = 16, Likes = new[] { Thing.Exploring }, Mass = 2,
        });
        Add(new ItemDef
        {
            Key = "barrel", Name = "Barrel", Words = new[] { "barrel", "keg", "drum", "bin", "trash can", "dustbin", "garbage can" }, W = 24, H = 32, Color = M.Hex(0xA0703C),
            Shapes = new[] { R(-11, 0, 11, 32, 1), new('o', new float[] { -12, 0, 12, 32, 6 }, 0, Over: true), L(-12, 7, 12, 7, 6, 2), L(-12, 25, 12, 25, 6, 2) },
            Verbs = new[] { Verb.Hide, Verb.Stand }, Surface = 32, SurfX1 = -10, SurfX2 = 10, Likes = new[] { Thing.Exploring }, Mass = 2,
        });
        Add(new ItemDef
        {
            Key = "tent", Name = "Tent", Words = new[] { "tent", "camping tent", "teepee", "tipi" }, W = 64, H = 42, Color = M.Hex(0x43A047),
            Shapes = new[] { P(1, -30, 0, 0, 42, 30, 0), POver(0, -32, 0, -6, 0, 0, 40, -2, 40), POver(2, 6, 0, 32, 0, 2, 40, 0, 40), L(0, 40, 0, 46, 6, 1.4f) },
            Verbs = new[] { Verb.Hide, Verb.Lie }, Comfort = 0.6f, Likes = new[] { Thing.Napping, Thing.Exploring }, Mass = 2,
        });
        Add(new ItemDef
        {
            Key = "table", Name = "Table", Words = new[] { "table", "desk", "coffee table", "picnic table", "workbench" }, W = 56, H = 24, Color = M.Hex(0xA0703C),
            Shapes = new[] { R(-28, 21, 28, 24, 0), L(-24, 0, -24, 21, 1, 2.4f), L(24, 0, 24, 21, 1, 2.4f) },
            Verbs = new[] { Verb.Stand }, Surface = 24, SurfX1 = -27, SurfX2 = 27, Likes = new[] { Thing.HighPlaces }, Mass = 2,
        });

        // ---------------- food ----------------
        ItemDef Food(string key, string name, string[] words, float w, float h, uint col, Shape[] shapes, int bites = 4) => new()
        {
            Key = key, Name = name, Words = words, W = w, H = h, Color = M.Hex(col), Shapes = shapes, Verbs = new[] { Verb.Eat }, Carry = true, Bites = bites, Likes = new[] { Thing.Eating },
        };
        Add(Food("apple", "Apple", new[] { "apple", "apples" }, 8, 9, 0xE53935, new[] { E(0, 4, 4, 4, 0), L(0, 7.5f, 0.6f, 9.5f, 13, 0.9f), E(2, 8.6f, 1.6f, 0.8f, 10) }));
        Add(Food("banana", "Banana", new[] { "banana", "bananas" }, 12, 5, 0xFDD835, new[] { P(0, -6, 3, -3, 0.8f, 3, 0, 6, 3, 5, 4, 2, 2, -3, 2.4f), L(6, 3, 6.6f, 4, 13, 1) }));
        Add(Food("pizza", "Pizza", new[] { "pizza", "pizza slice", "slice of pizza" }, 14, 10, 0xFFCA28, new[] { P(17, -7, 10, 7, 10, 0, 0), P(0, -6, 8.6f, 6, 8.6f, 0, 1.4f), E(-1, 6, 1.2f, 1.2f, 9), E(2, 4.5f, 1.2f, 1.2f, 9), E(-2.5f, 3.5f, 1, 1, 9) }));
        Add(Food("burger", "Burger", new[] { "burger", "hamburger", "cheeseburger", "sandwich", "sub" }, 12, 10, 0xE3B26B, new[] { O(-6, 0, 6, 3, 1.2f, 17), R(-6, 3, 6, 4.5f, 13), R(-6.3f, 4.5f, 6.3f, 5.5f, 18), P(0, -6, 5.5f, -4, 9, 4, 9, 6, 5.5f) }));
        Add(Food("cake", "Cake", new[] { "cake", "birthday cake", "cupcake", "pie" }, 16, 14, 0xF48FB1, new[] { R(-8, 0, 8, 9, 21), R(-8, 7, 8, 10, 0), L(0, 10, 0, 14, 11, 1.2f), E(0, 14.5f, 0.8f, 1.2f, 19) }, 6));
        Add(Food("cookie", "Cookie", new[] { "cookie", "cookies", "biscuit", "donut", "doughnut" }, 8, 8, 0xC8A26B, new[] { E(0, 4, 4, 4, 0), E(-1.5f, 5, 0.7f, 0.7f, 13), E(1.5f, 3, 0.7f, 0.7f, 13), E(0.5f, 5.5f, 0.6f, 0.6f, 13) }, 2));
        Add(Food("icecream", "Ice cream", new[] { "ice cream", "icecream", "ice-cream", "ice cream cone", "gelato" }, 8, 14, 0xF48FB1, new[] { P(17, -3, 7, 3, 7, 0, 0), E(0, 9, 3.6f, 3.6f, 0), E(0, 12.5f, 2.6f, 2.6f, 21) }, 3));
        Add(Food("watermelon", "Watermelon", new[] { "watermelon", "melon" }, 16, 9, 0xE53935, new[] { P(10, -8, 9, 8, 9, 0, 0), P(0, -6.6f, 9, 6.6f, 9, 0, 1.6f), E(-2, 6.4f, 0.5f, 0.7f, 8), E(2, 6.4f, 0.5f, 0.7f, 8) }, 5));

        // ---------------- comfort & toys ----------------
        Add(new ItemDef
        {
            Key = "pillow", Name = "Pillow", Words = new[] { "pillow", "cushion" }, W = 16, H = 7, Color = M.Hex(0xF7F5EF),
            Shapes = new[] { O(-8, 0, 8, 7, 3, 0) }, Verbs = new[] { Verb.Lie, Verb.Wield }, Surface = 7, SurfX1 = -7, SurfX2 = 7, Comfort = 0.6f, Carry = true,
            Reach = 10, Damage = 0.1f, Knock = 0.7f, Likes = new[] { Thing.Napping, Thing.Sparring },
        });

        // ---------------- weapons (grip at the origin, pointing up +y) ----------------
        Add(new ItemDef
        {
            Key = "sword", Name = "Sword", Words = new[] { "sword", "katana", "blade", "saber", "sabre", "lightsaber", "light saber" }, W = 10, H = 40, Color = M.Hex(0x9AA3AD),
            Shapes = new[] { L(0, -2, 0, 4, 4, 2.6f), L(-4.5f, 4, 4.5f, 4, 15, 2.2f), P(0, -1.6f, 4.5f, 1.6f, 4.5f, 1.2f, 36, 0, 40, -1.2f, 36), L(0, 7, 0, 34, 2, 0.6f) },
            Verbs = new[] { Verb.Wield }, Carry = true, Reach = 40, Damage = 1.6f, Knock = 1.2f, Likes = new[] { Thing.Fighting },
        });
        Add(new ItemDef
        {
            Key = "bat", Name = "Baseball bat", Words = new[] { "bat", "baseball bat", "club", "cricket bat" }, W = 6, H = 34, Color = M.Hex(0xA0703C),
            Shapes = new[] { P(0, -1, -2, 1, -2, 2.4f, 30, 0, 33, -2.4f, 30), L(-1, 0, 1, 0, 8, 1.6f) },
            Verbs = new[] { Verb.Wield }, Carry = true, Reach = 33, Damage = 1.3f, Knock = 1.9f, Likes = new[] { Thing.Fighting, Thing.PlayingBall },
        });
        Add(new ItemDef
        {
            Key = "pan", Name = "Frying pan", Words = new[] { "frying pan", "pan", "skillet", "wok" }, W = 15, H = 27, Color = M.Hex(0x2A2A2A),
            Shapes = new[] { L(0, -2, 0, 12, 8, 2.2f), E(0, 19, 7.5f, 7.5f, 0), E(0, 19, 5.6f, 5.6f, 6) },
            Verbs = new[] { Verb.Wield }, Carry = true, Reach = 26, Damage = 1.2f, Knock = 1.5f, Likes = new[] { Thing.Fighting, Thing.Eating },
        });
        Add(new ItemDef
        {
            Key = "stick", Name = "Stick", Words = new[] { "stick", "branch", "twig", "staff", "pole", "broom" }, W = 6, H = 32, Color = M.Hex(0x7B5134),
            Shapes = new[] { C(0, 2, 0, -2, 0.5f, 14, -0.5f, 32), C(0, 1.2f, 0.3f, 18, 3.5f, 23) },
            Verbs = new[] { Verb.Wield }, Carry = true, Reach = 30, Damage = 0.8f, Knock = 1, Likes = new[] { Thing.Exploring, Thing.Sparring },
        });
        Add(new ItemDef
        {
            Key = "umbrella", Name = "Umbrella", Words = new[] { "umbrella", "brolly", "parasol" }, W = 7, H = 34, Color = M.Hex(0xE53935),
            Shapes = new[] { L(0, 0, 0, 33, 13, 1.4f), C(13, 1.4f, 0, 0, -2.5f, -1.2f, -3.5f, 1.5f), P(0, -3.4f, 10, 3.4f, 10, 0.8f, 32, -0.8f, 32), L(-1, 12, -0.4f, 30, 1, 0.6f) },
            Verbs = new[] { Verb.Shelter }, Carry = true, Reach = 32, Damage = 0.35f, Knock = 0.5f, Likes = new[] { Thing.Exploring },
        });
        Add(new ItemDef
        {
            Key = "snowman", Name = "Snowman", Words = new[] { "snowman", "snow man", "snowwoman", "frosty" }, W = 26, H = 47, Color = M.Hex(0xF7F5EF),
            Shapes = new[] { E(0, 9, 13, 9, 7), E(0, 25.5f, 10, 8, 7), E(0, 39, 7.5f, 7.5f, 7), E(-2.6f, 40.5f, 1, 1, 8), E(2.6f, 40.5f, 1, 1, 8),
                             P(12, 0, 39.6f, 0, 37.6f, 7, 38.4f), E(0, 27, 0.9f, 0.9f, 8), E(0, 23, 0.9f, 0.9f, 8),
                             L(-9, 26, -19, 33, 13, 1.3f), L(9, 26, 19, 33, 13, 1.3f), R(-6, 45.5f, 6, 47, 8), R(-4, 47, 4, 53, 8) },
            Verbs = Array.Empty<Verb>(), Likes = new[] { Thing.Tricks },
        });
        Add(new ItemDef
        {
            // The Creator's Pencil: a figure holding it draws itself whatever it wants.
            Key = "pencil", Name = "Creator's Pencil", Words = new[] { "creator's pencil", "creators pencil", "creator pencil", "magic pencil", "pencil", "the pencil", "drawing pencil" },
            W = 5, H = 30, Color = M.Hex(0xFDD835),
            Shapes = new[] { R(-2.2f, -4, 2.2f, -1, 14), R(-2.5f, -1.4f, 2.5f, 1, 5), R(-2.2f, 1, 2.2f, 23, 0), L(-0.7f, 2, -0.7f, 22, 1, 0.7f),
                             P(17, -2.2f, 23, 2.2f, 23, 0, 28.5f), P(8, -0.8f, 26.6f, 0.8f, 26.6f, 0, 30) },
            Verbs = new[] { Verb.Create }, Carry = true, Reach = 29, Damage = 0.15f, Knock = 0.3f, Likes = new[] { Thing.Tricks, Thing.Exploring, Thing.Reading },
        });
        Add(new ItemDef
        {
            Key = "blaster", Name = "Toy blaster", Words = new[] { "blaster", "toy blaster", "gun", "pistol", "nerf gun", "toy gun", "ray gun", "laser gun", "dart gun", "revolver" }, W = 8, H = 19, Color = M.Hex(0xFB8C00),
            Shapes = new[] { O(-2, -2, 2.5f, 5, 1, 1), O(-3, 4, 3.5f, 11, 1.5f, 0), R(-1.4f, 10, 1.4f, 19, 11), R(-1.6f, 17, 1.6f, 19, 8), L(2.5f, 3, 4, 5, 8, 1) },
            Verbs = new[] { Verb.Shoot }, Carry = true, Reach = 19, Damage = 0.5f, Knock = 0.6f, FireRate = 0.38f, Ammo = Ammo.Dart, Likes = new[] { Thing.Fighting, Thing.Tricks },
        });
        Add(new ItemDef
        {
            Key = "watergun", Name = "Water gun", Words = new[] { "water gun", "watergun", "squirt gun", "super soaker", "water pistol" }, W = 10, H = 20, Color = M.Hex(0x00ACC1),
            Shapes = new[] { O(-2, -2, 2.5f, 6, 1, 1), O(-4, 5, 4, 13, 3, 0), E(0, 9, 2.5f, 2.5f, 11), R(-1, 12, 1, 20, 1) },
            Verbs = new[] { Verb.Shoot }, Carry = true, Reach = 20, Damage = 0.05f, Knock = 0.3f, FireRate = 0.1f, Ammo = Ammo.Water, Likes = new[] { Thing.PlayingBall, Thing.Tricks },
        });
        // ---------------- sports ----------------
        // Side-on soccer goal: the open mouth faces -x (the field), the net slopes back to +x.
        Add(new ItemDef
        {
            Key = "goal", Name = "Soccer goal", Words = new[] { "goal", "soccer goal", "football goal", "goalpost", "goal post", "soccer net", "football net" }, W = 44, H = 34, Color = M.Hex(0xF4F4F4),
            Shapes = new[] { C(5, 0.5f, -18, 32, -14, 0), C(5, 0.5f, -10, 31, -4, 0), C(5, 0.5f, -2, 29, 6, 0), C(5, 0.5f, 6, 27, 14, 0),
                             C(5, 0.5f, -18, 22, 15, 18), C(5, 0.5f, -18, 12, 17, 9), L(-18, 0, 18, 0, 5, 1), L(12, 25, 18, 0, 0, 1.8f), L(-18, 32, 12, 25, 0, 2.2f), L(-18, 0, -18, 32, 0, 2.6f) },
            Verbs = new[] { Verb.Play }, Sport = "Soccer", Mass = 2, Likes = new[] { Thing.PlayingBall, Thing.SoccerBalls },
            Colliders = new float[] { -18, 32, 12, 25, 1, 12, 25, 18, 0, 0.25f, -18, 30, -18, 33, 1 },
        });
        Add(new ItemDef
        {
            Key = "hoop", Name = "Basketball hoop", Words = new[] { "hoop", "basketball hoop", "basket", "basketball net", "net hoop" }, W = 30, H = 92, Color = M.Hex(0xFB8C00),
            Shapes = new[] { R(10, 0, 22, 3, 6), L(16, 0, 16, 82, 6, 2.6f), L(16, 80, 9, 80, 6, 2), R(6, 62, 9, 92, 7), R(6.6f, 66, 8.4f, 73, 9),
                             C(7, 0.5f, -14, 72, -9, 60), C(7, 0.5f, -8, 72, -5, 60), C(7, 0.5f, -2, 72, -1, 60), C(7, 0.5f, 4, 72, 3, 60), C(7, 0.5f, -9, 60, 3, 60), C(7, 0.5f, -11.5f, 66, 3.5f, 66),
                             L(-14, 72, 6, 72, 0, 1.6f) },
            Verbs = new[] { Verb.Play }, Sport = "Basketball", Mass = 3, Likes = new[] { Thing.PlayingBall, Thing.Basketballs },
            Colliders = new float[] { 7, 62, 7, 92, 1, -14.5f, 71.5f, -13.5f, 72.5f, 1 },
        });
        Add(new ItemDef
        {
            Key = "tennisnet", Name = "Tennis net", Words = new[] { "tennis net", "tennis", "tennis court" }, W = 6, H = 18, Color = M.Hex(0xF4F4F4),
            Shapes = new[] { L(0, 0, 0, 18, 6, 1.8f), C(5, 0.4f, -1, 4, 1, 4), C(5, 0.4f, -1, 8, 1, 8), C(5, 0.4f, -1, 12, 1, 12), R(-1.2f, 15.5f, 1.2f, 17.5f, 0) },
            Verbs = new[] { Verb.Play }, Sport = "Tennis", Likes = new[] { Thing.PlayingBall },
            Colliders = new float[] { 0, 0, 0, 17.5f, 0.3f },
        });
        Add(new ItemDef
        {
            Key = "badmintonnet", Name = "Badminton net", Words = new[] { "badminton net", "badminton", "volleyball net", "volleyball" }, W = 6, H = 32, Color = M.Hex(0xF4F4F4),
            Shapes = new[] { L(0, 0, 0, 32, 6, 1.6f), R(-1.2f, 18, 1.2f, 31, 8), C(7, 0.4f, -1, 22, 1, 22), C(7, 0.4f, -1, 26, 1, 26), R(-1.4f, 30, 1.4f, 32, 7) },
            Verbs = new[] { Verb.Play }, Sport = "Badminton", Likes = new[] { Thing.PlayingBall },
            Colliders = new float[] { 0, 18, 0, 32, 0.3f },
        });
        Add(new ItemDef
        {
            Key = "racket", Name = "Tennis racket", Words = new[] { "racket", "tennis racket", "racquet", "tennis racquet" }, W = 12, H = 26, Color = M.Hex(0x1E88E5),
            Shapes = new[] { L(0, -2, 0, 12, 8, 2.2f), C(0, 1.3f, 0, 12, -5, 17, -5.5f, 22, -3, 26, 3, 26, 5.5f, 22, 5, 17, 0, 12),
                             C(7, 0.3f, -4, 16, 4, 16), C(7, 0.3f, -5, 20, 5, 20), C(7, 0.3f, -4, 24, 4, 24), C(7, 0.3f, -2, 13, -2, 26), C(7, 0.3f, 2, 13, 2, 26) },
            Verbs = new[] { Verb.Wield }, Carry = true, Reach = 26, Damage = 0.2f, Knock = 0.8f, Likes = new[] { Thing.PlayingBall },
        });
        Add(new ItemDef
        {
            Key = "badmintonracket", Name = "Badminton racket", Words = new[] { "badminton racket", "badminton racquet" }, W = 10, H = 28, Color = M.Hex(0xE53935),
            Shapes = new[] { L(0, -2, 0, 16, 5, 1.4f), C(0, 1.0f, 0, 16, -4, 20, -4.2f, 25, -2, 28.5f, 2, 28.5f, 4.2f, 25, 4, 20, 0, 16),
                             C(7, 0.3f, -3, 20, 3, 20), C(7, 0.3f, -3.5f, 24, 3.5f, 24), C(7, 0.3f, 0, 17, 0, 28) },
            Verbs = new[] { Verb.Wield }, Carry = true, Reach = 28, Damage = 0.1f, Knock = 0.6f, Likes = new[] { Thing.PlayingBall },
        });
        return list.ToArray();
    }

    // ---------------- words → objects ----------------

    static readonly Dictionary<string, float> SizeWords = new()
    {
        ["tiny"] = 0.45f, ["mini"] = 0.55f, ["little"] = 0.7f, ["small"] = 0.7f, ["big"] = 1.4f, ["large"] = 1.4f,
        ["huge"] = 2f, ["giant"] = 2.6f, ["gigantic"] = 3f, ["enormous"] = 3f, ["massive"] = 2.6f,
    };

    static readonly Dictionary<string, uint> ColourWords = new()
    {
        ["red"] = 0xE53935, ["blue"] = 0x1E88E5, ["green"] = 0x43A047, ["orange"] = 0xFB8C00, ["purple"] = 0x8E24AA,
        ["yellow"] = 0xFDD835, ["cyan"] = 0x00ACC1, ["pink"] = 0xEC407A, ["black"] = 0x2E2E2E, ["white"] = 0xF4F4F4,
        ["brown"] = 0x7B5134, ["grey"] = 0x9E9E9E, ["gray"] = 0x9E9E9E, ["gold"] = 0xF2C14E, ["golden"] = 0xF2C14E,
        ["silver"] = 0xC0C6CC, ["teal"] = 0x00897B, ["navy"] = 0x283593, ["lime"] = 0xC0CA33, ["maroon"] = 0x8E2430,
    };

    /// <summary>What "a giant red couch" means: the object, a size multiplier and maybe a colour.</summary>
    public static (ItemDef? def, float size, Color4? colour, string noun) Parse(string text)
    {
        string t = Regex.Replace(text.ToLowerInvariant(), "[^a-z' -]", " ").Trim();
        t = Regex.Replace(t, @"^(an?|the|some|one)\s+", "");
        float size = 1;
        Color4? colour = null;
        var words = t.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        for (int i = 0; i < words.Count;)
        {
            if (SizeWords.TryGetValue(words[i], out float s)) { size *= s; words.RemoveAt(i); continue; }
            if (ColourWords.TryGetValue(words[i], out uint c)) { colour = M.Hex(c); words.RemoveAt(i); continue; }
            if (words[i] is "very" or "really" or "super") { size *= words.Count > i + 1 && SizeWords.ContainsKey(words[i + 1]) ? 1.2f : 1; words.RemoveAt(i); continue; }
            i++;
        }
        string noun = string.Join(' ', words);
        if (noun.Length == 0) return (null, size, colour, noun);
        var def = Find(noun) ?? (noun.EndsWith("es") ? Find(noun[..^2]) : null) ?? (noun.EndsWith('s') ? Find(noun[..^1]) : null)
                  ?? Find(words[^1]);   // "comfy old couch" → couch
        return (def, Math.Clamp(size, 0.3f, 3.5f), colour, noun);
    }

    public static ItemDef? Find(string noun)
    {
        foreach (var d in All)
            if (d.Key == noun || d.Words.Contains(noun)) return d;
        return null;
    }

    /// <summary>Ball words map to the existing ball props.</summary>
    public static PropKind? BallFor(string noun) => noun switch
    {
        "ball" or "balls" or "rubber ball" or "bouncy ball" => PropKind.Ball,
        "soccer ball" or "football" or "soccerball" => PropKind.SoccerBall,
        "basketball" or "basket ball" => PropKind.Basketball,
        "beach ball" or "beachball" => PropKind.BeachBall,
        "tennis ball" or "tennisball" => PropKind.TennisBall,
        "shuttlecock" or "shuttle" or "birdie" or "shuttlecock birdie" => PropKind.Shuttlecock,
        _ => null,
    };
}
