using System.Numerics;

namespace Doodlefolk;

/// <summary>Hobbies in action: collectors pick up trinkets they find (and show them off), gardeners plant seeds and
/// keep them watered until they bloom.</summary>
sealed partial class Brain
{
    public readonly List<string> Collection = new();
    float _gardenCd = 30, _showTreasureCd = 60;

    /// <summary>Picked up a trinket: into the pocket it goes.</summary>
    void Collected(Item it, World w)
    {
        Collection.Add(it.Def.Name);
        w.RemoveItem(it);
        int n = Collection.Count;
        f.Emote(V($"ooh, a {it.Def.Name.ToLowerInvariant()}!", $"A {it.Def.Name.ToUpperInvariant()}!!! MINE!", $"a {it.Def.Name.ToLowerInvariant()}. I'll keep it.", $"oh… a little {it.Def.Name.ToLowerInvariant()}", $"a {it.Def.Name.ToLowerInvariant()}, treasure"), 1.5f);
        Cheered(Hobby == Hobby.Collecting ? 0.25f : 0.1f);
        Boredom = MathF.Max(0, Boredom - 0.15f);
        Write("found:" + it.Def.Key, V($"Found a {it.Def.Name.ToLowerInvariant()}! That makes {n} treasures.", $"FOUND A {it.Def.Name.ToUpperInvariant()}!!! {n} treasures now!", $"Picked up a {it.Def.Name.ToLowerInvariant()}. {n} now.", $"Found a little {it.Def.Name.ToLowerInvariant()}. I'm keeping it safe. ({n})", $"A {it.Def.Name.ToLowerInvariant()} joined my collection. {n} treasures."), "★", 120);
        if (n == 5) { w.Sticker("collector"); w.News("collector", $"{f.Name}'s collection reaches five treasures", 1, f); }
    }

    void GardenOptions(World w, OptionList opts)
    {
        // Gardeners plant (a few plants each).
        if (Hobby == Hobby.Gardening && _gardenCd < _t0 && w.Items.Count(i => i.IsPlant && i.PlanterId == f.Id) < 3 && w.Items.Count < 55
            && w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd) is { Item: null } seg && seg.X2 - seg.X1 > 80 * S)
            opts.Add(0.45f + Boredom * 0.5f, () => PlantSeed(w), "Plant some seeds");
        // A fish tank is a calming thing to watch.
        if (w.Items.FirstOrDefault(i => i.Def.Key == "fishtank" && Vector2.Distance(i.Pos, f.Base) < 1500 * S) is { } tank && (Sadness > 0.2f || Annoyance > 0.2f || Boredom > 0.4f))
            opts.Add(0.3f + Sadness * 0.8f + Annoyance * 0.6f, () =>
            {
                float side = MathF.Sign(f.Base.X - tank.Pos.X); if (side == 0) side = 1;
                Navigate(() => w.Items.Contains(tank) ? new Vector2(tank.Pos.X + side * (tank.Def.W * tank.Sc * 0.5f + 14 * S), tank.Pos.Y) : null, 8 * S, false, () =>
                {
                    FaceTo(tank.Pos.X);
                    Go(G.SitFloor, rng.Range(10, 20));
                    Sadness = MathF.Max(0, Sadness - 0.15f); Annoyance = MathF.Max(0, Annoyance - 0.2f); Boredom = MathF.Max(0, Boredom - 0.2f);
                    Write("fish", V("Watched the fish for a while. Calming.", "THE FISH!! They're so swishy!", "Stared at fish. It helped. Don't tell anyone.", "I watched the fish. They don't judge.", "The fish drift, and so did my thoughts."), "♪", 1800);
                }, WalkPurpose.Look);
            }, "Watch the fish");
        // Collectors show off their finds.
        if (Collection.Count >= 3 && _showTreasureCd < _t0)
        {
            var friend = w.Figures.FirstOrDefault(o => o != f && o.Mode == Mode.Control && !o.Brain.Asleep && Vector2.Distance(o.Base, f.Base) < 500 * S && AffinityWith(o) > 0.1f);
            if (friend != null) opts.Add(0.3f + P.Sociability * 0.4f, () => ShowOff(friend, w), $"Show {friend.Name} their collection");
        }
    }

    void PlantSeed(World w)
    {
        _gardenCd = _t0 + rng.Range(120, 300);
        if (w.MakeItem?.Invoke("seedpatch") is not { } patch) return;
        patch.Pos = f.Base + new Vector2(f.Facing * 28 * S, -2 * S);
        patch.Vel = Vector2.Zero;
        patch.OnGround = false;
        patch.PlantKind = new[] { "tulip", "sunflower", "tomatoplant" }[rng.Next(3)];
        patch.PlanterId = f.Id;
        patch.Fill = 1;
        f.SetAction(Act.Tap);
        Go(G.Idle, 2);
        string what = patch.PlantKind == "tomatoplant" ? "tomato" : patch.PlantKind;
        f.Emote(V("planting!", $"{what.ToUpperInvariant()} SEEDS!", "planting. quietly.", "a little seed…", "a seed, a promise"), 1.4f);
        Write("plant", V($"Planted some {what} seeds.", $"PLANTED {what.ToUpperInvariant()} SEEDS!! Grow grow grow!", $"Planted {what} seeds. We'll see.", $"I planted some {what} seeds. I hope they like it here.", $"Put {what} seeds in the earth."), "★", 300);
    }

    void ShowOff(Figure o, World w)
    {
        _showTreasureCd = _t0 + rng.Range(180, 400);
        string thing = Collection[rng.Next(Collection.Count)].ToLowerInvariant();
        float side = MathF.Sign(f.Base.X - o.Base.X);
        Navigate(() => w.Figures.Contains(o) ? o.Base + new Vector2(side * 36 * S, 0) : null, 12 * S, false, () =>
        {
            FaceTo(o.Base.X);
            f.Emote(V($"look, my {thing}!", $"LOOK AT MY {thing.ToUpperInvariant()}!!", $"behold. a {thing}.", $"um… want to see my {thing}?", $"my {thing}, see how it shines"), 1.6f);
            o.Emote(o.Brain.V("ooh!", "WOW!!", "…neat.", "pretty!", "a fine treasure"), 1.3f);
            AddAffinity(o, 0.03f); o.Brain.AddAffinity(f, 0.02f);
            Go(G.Idle, 2);
        }, WalkPurpose.Social);
    }

    /// <summary>Something it planted came into bloom.</summary>
    public void PlantBloomed(Item plant, World w)
    {
        f.Emote(V("it bloomed!", "IT BLOOMED!!!", "it grew. good.", "oh… it bloomed ♥", "a flower, at last"), 1.8f);
        Cheered(0.35f);
        Write("bloom:" + plant.Id, V($"My {plant.Def.Name.ToLowerInvariant()} bloomed!", $"MY {plant.Def.Name.ToUpperInvariant()} BLOOMED!!!", $"The {plant.Def.Name.ToLowerInvariant()} bloomed. As planned.", $"My little {plant.Def.Name.ToLowerInvariant()} grew up. I'm so proud.", $"The {plant.Def.Name.ToLowerInvariant()} opened today."), "★", 0);
    }
}
