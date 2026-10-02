using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Each animal's temperament: it colours everything from how quickly it bonds to how hard it is to train.</summary>
enum Temperament { Easygoing, Shy, Bold, Lazy, Mischievous, Affectionate, Playful }

/// <summary>Temperament, families (a bonded pair can have a litter; the young take after their parents and follow
/// their mother about), tricks (taught with treats, like everything else) and clothes (bandanas, sweaters, bows, and a
/// raincoat that goes on by itself for a rainy walk).</summary>
sealed partial class Pet
{
    public Temperament Temper;
    public bool Female;
    public string Wear = "", WearColour = "#E53935";
    /// <summary>A rare coat (about one animal in forty): golden, rainbow (it shifts through the colours), starry or
    /// silver. Kittens and puppies can inherit it.</summary>
    public string Rare = "";
    public static readonly string[] RareCoats = { "golden", "rainbow", "starry", "silver" };

    public void GiveRareCoat(string coat)
    {
        Rare = coat;
        switch (coat)
        {
            case "golden": Color = M.Hex(0xF2C14E); Accent = M.Hex(0xFFF3C4); break;
            case "starry": Color = M.Hex(0x283593); Accent = M.Hex(0xE8EAF6); break;
            case "silver": Color = M.Hex(0xC9CED6); Accent = M.Hex(0xF4F6F8); break;
        }
    }

    /// <summary>Rainbow coats shift; golden ones glint now and then.</summary>
    void RareCoatStep(World w, float dt)
    {
        if (Rare == "rainbow")
        {
            float h = (float)((World.Now * 0.08 + Id * 0.13) % 1);
            Color = M.Hsv(h, 0.55f, 0.95f);
        }
        else if (Rare == "golden" && _rng.NextDouble() < dt * 0.3) w.Fx.Spark(Pos + new Vector2(_rng.Range(-8, 8), -_rng.Range(4, 14)) * _s, _s * 0.5f, _rng, 0.5f);
    }
    /// <summary>Expecting: seconds until the young arrive (0: not expecting).</summary>
    public float Pregnant;
    public int MotherId, FatherId;
    public string MotherName = "";
    public DateTime? LastLitter;
    float _breedCheck = 120;
    int _mateId;
    string _trick = "";

    public static bool Breeding = true;
    public static int MaxPets = 18;

    void InitSpecies()
    {
        Temper = (Temperament)_rng.Next(Enum.GetValues<Temperament>().Length);
        Female = _rng.NextDouble() < 0.5;
    }

    /// <summary>A multiplier that only applies to one temperament.</summary>
    float Tm(Temperament t, float k) => Temper == t ? k : 1;

    public string TemperWord => Temper switch
    {
        Temperament.Shy => "shy", Temperament.Bold => "bold", Temperament.Lazy => "lazy", Temperament.Mischievous => "mischievous",
        Temperament.Affectionate => "affectionate", Temperament.Playful => "playful", _ => "easygoing",
    };

    // ---------------- families ----------------

    void UpdateBreeding(World w, float dt)
    {
        if (Pregnant > 0)
        {
            Pregnant -= dt;
            if (Pregnant <= 0) { Pregnant = 0; GiveBirth(w); }
            return;
        }
        _breedCheck -= dt;
        if (_breedCheck > 0) return;
        _breedCheck = 90;
        if (!Breeding || !Female || Young || Health < 0.7f || Happiness < 0.55f || w.Pets.Count >= MaxPets - 1) return;
        if (LastLitter is DateTime last && (DateTime.Now - last).TotalHours < 6) return;
        var mate = w.Pets.FirstOrDefault(o => o != this && o.Kind == Kind && !o.Female && !o.Young && PetBond(o) > 0.55f && o.PetBond(this) > 0.55f && Vector2.Distance(o.Pos, Pos) < 500 * _s);
        if (mate == null || _rng.NextDouble() > 0.1) return;
        _mateId = mate.Id;
        Pregnant = 1500;
        Log($"Expecting! ({mate.Name} is the father)");
        w.News("pets", $"{Name} and {mate.Name} are expecting {(Kind == PetKind.Parrot ? "eggs" : "a litter")}!", 3);
    }

    void GiveBirth(World w)
    {
        var dad = w.Pets.FirstOrDefault(o => o.Id == _mateId);
        int n = Kind switch { PetKind.Cat => _rng.Next(2, 5), PetKind.Dog => _rng.Next(2, 6), PetKind.Rabbit => _rng.Next(3, 7), PetKind.Hamster => _rng.Next(3, 7), _ => _rng.Next(1, 3) };
        // The cap is soft: a pregnancy only starts with room to spare, but if others arrived meanwhile one baby still comes.
        n = Math.Max(1, Math.Min(n, MaxPets - w.Pets.Count));
        var names = new List<string>();
        for (int i = 0; i < n; i++)
        {
            if (w.MakePet?.Invoke(Kind) is not { } baby) break;
            baby.Age = 0.03f;
            baby.Color = _rng.NextDouble() < 0.4 || dad == null ? Color : _rng.NextDouble() < 0.6 ? dad.Color : Color4.Lerp(Color, dad.Color, 0.5f);
            baby.Accent = dad != null && _rng.NextDouble() < 0.5 ? dad.Accent : Accent;
            baby.Temper = _rng.NextDouble() < 0.2 ? (Temperament)_rng.Next(7) : _rng.NextDouble() < 0.5 || dad == null ? Temper : dad.Temper;
            // Rare coats run in families.
            string inherited = Rare.Length > 0 && _rng.NextDouble() < 0.4 ? Rare : dad != null && dad.Rare.Length > 0 && _rng.NextDouble() < 0.4 ? dad.Rare : _rng.NextDouble() < 0.025 ? RareCoats[_rng.Next(RareCoats.Length)] : "";
            if (inherited.Length > 0) { baby.GiveRareCoat(inherited); w.RareSeen?.Invoke(inherited); }
            baby.MotherId = Id; baby.MotherName = Name; baby.FatherId = dad?.Id ?? 0;
            baby.Pos = Pos + new Vector2((i - n / 2f) * 10 * _s, -4 * _s);
            baby.ResetTraining();
            baby.SetPetBond(this, 0.9f); SetPetBond(baby, 0.95f);
            if (dad != null) { baby.SetPetBond(dad, 0.6f); dad.SetPetBond(baby, 0.6f); }
            baby.Log($"Born to {Name}{(dad != null ? $" and {dad.Name}" : "")}");
            names.Add(baby.Name);
        }
        LastLitter = DateTime.Now;
        string young = Kind switch { PetKind.Cat => "kittens", PetKind.Dog => "puppies", PetKind.Rabbit => "kits", PetKind.Hamster => "pups", _ => "chicks" };
        Log($"Had {names.Count} {young}: {string.Join(", ", names)}");
        w.News("pets", $"{Name} had {names.Count} {young}! Welcome {string.Join(", ", names)}", 4);
        w.Sticker("litter");
    }

    Pet? Mother(World w) => MotherId == 0 ? null : w.Pets.FirstOrDefault(o => o.Id == MotherId);

    // ---------------- tricks ----------------

    public static readonly string[] TrickNames = { "roll over", "high five", "play dead", "spin" };

    static PetSkill TrickSkill(string t) => t switch { "roll over" => PetSkill.RollOver, "high five" => PetSkill.HighFive, "play dead" => PetSkill.PlayDead, _ => PetSkill.Spin };

    public bool CanTrick(string t) => Kind switch
    {
        PetKind.Dog => true,
        PetKind.Cat => t is "high five" or "spin",
        PetKind.Parrot => t is "high five" or "spin",
        _ => t == "spin",
    };

    /// <summary>Ask for a trick. Whether it happens depends on how well it's learned (and the animal's mood).</summary>
    public string DoTrick(string t, World w)
    {
        if (!CanTrick(t)) return $"{Name} isn't the type for \"{t}\".";
        var sk = TrickSkill(t);
        if (!Grounded && Kind != PetKind.Parrot) return $"{Name} is busy.";
        float chance = (0.1f + 0.86f * Sk(sk)) * (Kind == PetKind.Cat ? 0.6f : 1) * Tm(Temperament.Lazy, 0.7f) * Tm(Temperament.Mischievous, 0.8f) * (Stress > 0.6f ? 0.5f : 1);
        if (_rng.NextDouble() > chance)
        {
            Skills[sk] = MathF.Min(1, Sk(sk) + 0.01f);
            Shout("?");
            Log($"Didn't quite get \"{t}\" yet");
            return $"{Name} tilts their head. ({Sk(sk) * 100:0}% learned: keep asking, and treat it when it works.)";
        }
        _trick = t;
        Go(State.Trick, t switch { "play dead" => 3.5f, "roll over" => 1.8f, _ => 1.5f });
        if (t == "spin" && Kind != PetKind.Parrot) { Vel = new Vector2(0, -260 * S); Grounded = false; }
        GoodDeed(sk);
        if (Sk(sk) > 0.5f) w.Sticker("trick");
        Log($"Did a trick: {t}");
        return $"{Name} did it: {t}! Give a treat now to make it stick.";
    }

    void DoTrickPose(float dt)
    {
        Vel.X = Grounded ? 0 : Vel.X;
        switch (_trick)
        {
            case "roll over": _pose = Pose.Belly; break;
            case "play dead": _pose = Pose.Belly; break;
            case "high five": _pose = Kind == PetKind.Parrot ? Pose.Perch : Pose.Upright; break;
            case "spin": _pose = Grounded ? Pose.Stand : Pose.Air; if ((int)(_t / 0.15f) != (int)((_t - dt) / 0.15f)) Facing = -Facing; break;
            default: _pose = Grounded ? Pose.Sit : Pose.Air; break;
        }
    }

    // ---------------- clothes ----------------

    public static readonly string[] Outfits = { "", "bandana", "sweater", "bow", "raincoat" };

    string WearNow => Wear.Length > 0 ? Wear : World.Current?.Weather.Raining == true && Kind == PetKind.Dog && (Leashed || _st == State.Walk) ? "raincoat" : "";

    Color4 WearCol => WearNow == "raincoat" && Wear != "raincoat" ? M.Hex(0xFDD835) : Lit(WearColour.Length == 7 ? Settings.ParseHex(WearColour) : M.Hex(0xE53935));

    /// <summary>Clothes on a four-legged body (hip and chest are the body's ends, head and its radius).</summary>
    void DrawOutfit(Renderer r, Vector2 hip, Vector2 chest, float thick, Vector2 head, float hr, float f)
    {
        string wear = WearNow;
        if (wear.Length == 0) return;
        var c = WearCol;
        var ink = new Color4(0.12f, 0.12f, 0.12f, 0.6f);
        float s = S;
        switch (wear)
        {
            case "sweater":
            case "raincoat":
            {
                Vector2 a = Vector2.Lerp(hip, chest, 0.22f), b = Vector2.Lerp(hip, chest, 0.95f);
                r.Line(a, b, c, thick * 1.04f);
                r.Line(Vector2.Lerp(a, b, 0.05f) + new Vector2(0, -thick * 0.3f), Vector2.Lerp(a, b, 0.05f) + new Vector2(0, thick * 0.3f), Gfx.Darker(c, 0.25f), 1.1f * s);
                if (wear == "raincoat")
                {
                    // A hood up over the head.
                    r.FillPolygon(stackalloc Vector2[] { head + new Vector2(-f * hr * 1.1f, hr * 0.2f), head + new Vector2(-f * hr * 0.9f, -hr * 1.05f), head + new Vector2(f * hr * 0.5f, -hr * 1.1f), head + new Vector2(f * hr * 0.2f, -hr * 0.4f) }, c);
                }
                break;
            }
            case "bandana":
                r.FillPolygon(stackalloc Vector2[] { head + new Vector2(-f * hr * 0.75f, hr * 0.6f), head + new Vector2(f * hr * 0.5f, hr * 0.85f), head + new Vector2(-f * hr * 0.1f, hr * 1.6f) }, c);
                r.Line(head + new Vector2(-f * hr * 0.75f, hr * 0.6f), head + new Vector2(f * hr * 0.5f, hr * 0.85f), ink, 0.8f * s);
                break;
            case "bow":
            {
                Vector2 b = head + new Vector2(-f * hr * 0.35f, -hr * 0.95f);
                r.FillPolygon(stackalloc Vector2[] { b, b + new Vector2(-hr * 0.55f, -hr * 0.3f), b + new Vector2(-hr * 0.55f, hr * 0.3f) }, c);
                r.FillPolygon(stackalloc Vector2[] { b, b + new Vector2(hr * 0.55f, -hr * 0.3f), b + new Vector2(hr * 0.55f, hr * 0.3f) }, c);
                r.Disc(b, hr * 0.14f, Gfx.Darker(c, 0.2f));
                break;
            }
        }
    }
}
