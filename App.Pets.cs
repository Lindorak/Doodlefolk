using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>The app's side of pets: pet-only mode, adopting (with a starter kit of supplies), care by clicking (fill a
/// bowl, scoop the litter box, clean up a mess), the spray bottle, leashes, and saving everything.</summary>
sealed partial class App
{
    // ---------------- pet-only mode ----------------

    readonly List<Figure> _stash = new();
    public bool PetMode => _settings.PetMode;

    /// <summary>Just pets: the figures step aside (kept safe, not lost) until you switch back.</summary>
    void SetPetMode(bool on)
    {
        if (on == _settings.PetMode && (on ? _stash.Count > 0 || _w.Figures.Count == 0 : _stash.Count == 0)) return;
        _settings.PetMode = on;
        EndPress();
        if (on)
        {
            StopGame(false);
            _w.Tourney = null;
            foreach (var f in _w.Figures.ToList()) { f.DropCarried(Vector2.Zero); _stash.Add(f); }
            _w.Figures.Clear();
            foreach (var p in _w.Pets) p.Owner = null;
            _w.Sticker("petmode");
            if (_w.Pets.Count == 0) { var cat = SpawnPet(PetKind.Cat); StarterKit(cat); }
        }
        else
        {
            foreach (var f in _stash) { _w.Figures.Add(f); f.SpawnT = 0.999f; }
            _stash.Clear();
        }
        ForceFullRedraw();
        _settings.Save();
    }

    // ---------------- adopting ----------------

    static (PetKind kind, bool young)? PetFor(string noun)
    {
        foreach (var w in noun.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (w is "kitten" or "kittens" or "kitty" or "kitties") return (PetKind.Cat, true);
            if (w is "cat" or "cats" or "pussycat" or "moggy") return (PetKind.Cat, noun.Contains("baby"));
            if (w is "puppy" or "puppies" or "pup" or "pupper") return (PetKind.Dog, true);
            if (w is "dog" or "dogs" or "doggy" or "doggo" or "hound") return (PetKind.Dog, noun.Contains("baby"));
            if (w is "chick" or "fledgling") return (PetKind.Parrot, true);
            if (w is "bunny" or "bunnies" or "kit") return (PetKind.Rabbit, true);
            if (w is "rabbit" or "rabbits" or "hare") return (PetKind.Rabbit, noun.Contains("baby"));
            if (w is "hamster" or "hamsters" or "gerbil" or "gerbils") return (PetKind.Hamster, noun.Contains("baby"));
            if (w is "parrot" or "parrots" or "bird" or "birds" or "budgie" or "parakeet" or "cockatiel" or "macaw" or "lovebird" or "cockatoo") return (PetKind.Parrot, noun.Contains("baby"));
        }
        return null;
    }

    Pet AdoptPet(PetKind kind, bool young)
    {
        var pet = SpawnPet(kind);
        if (young) { pet.Age = 0.08f; pet.ResetTraining(); }
        pet.Log(young ? $"Arrived as a little {pet.Species()}" : $"Adopted. Welcome home, {pet.Name}!");
        _w.News("pets", young ? $"A new {pet.Species()} arrives: welcome, {pet.Name}!" : $"{pet.Name} the {pet.Species(false)} joins the household", 3);
        StarterKit(pet);
        return pet;
    }

    /// <summary>Whatever this kind of pet needs and isn't here yet, set down on the floor near it.</summary>
    void StarterKit(Pet pet)
    {
        var need = new List<string> { "foodbowl", "waterbowl" };
        need.Add(pet.Kind switch { PetKind.Cat or PetKind.Rabbit => "litterbox", PetKind.Dog => "petbed", PetKind.Hamster => "hamsterwheel", _ => "perch" });
        if (pet.Kind is PetKind.Cat or PetKind.Rabbit or PetKind.Hamster) need.Add("petbed");
        if (pet.Kind == PetKind.Dog && pet.Young) need.Add("peepad");
        var floor = _w.Env.Platforms.Where(p => p.Solid).OrderBy(p => MathF.Abs(M.ClampIn(pet.Pos.X, p.X1, p.X2) - pet.Pos.X) + MathF.Abs(p.Y - pet.Pos.Y)).FirstOrDefault();
        if (floor == null) return;
        float x = M.ClampIn(pet.Pos.X, floor.X1 + 200 * _w.Scale, floor.X2 - 200 * _w.Scale) - 120 * _w.Scale;
        foreach (var key in need)
        {
            if (_w.Items.Any(i => i.Def.Key == key)) continue;
            if (ItemCatalog.Find(key) is not { } def || SpawnItem(def) is not { } it) continue;
            it.Pos = new Vector2(x, floor.Y - 2);
            it.Vel = Vector2.Zero;
            it.OnGround = false;
            x += (def.W * it.Sc + 22 * _w.Scale);
        }
    }

    // ---------------- care by clicking ----------------

    /// <summary>Clicked a care item or a mess. True if that was a care action (not a grab).</summary>
    bool CareClick(Item it)
    {
        float s = _w.Scale;
        if (it.IsMess)
        {
            _w.Fx.Spark(it.Pos + new Vector2(0, -4 * s), s, _w.Rng, 0.6f, new Color4(1, 1, 1, 0.9f));
            World.Play(Sfx.Swish, it.Pos, 0.35f, 1.3f);
            _w.RemoveItem(it);
            _w.Sticker("cleanup");
            foreach (var p in _w.Pets) if (Vector2.Distance(p.Pos, it.Pos) < 400 * s) p.Stress = MathF.Max(0, p.Stress - 0.03f);
            return true;
        }
        if (it.IsPlant && it.Fill < 0.6f)
        {
            it.Fill = 1;
            World.Play(Sfx.Splat, it.Pos, 0.25f, 1.6f);
            _w.Fx.Spark(it.Pos + new Vector2(0, -6 * s), s * 0.6f, _w.Rng, 0.4f, new Color4(0.5f, 0.75f, 1, 1));
            return true;
        }
        if (it.Def.Key == "fishtank" && it.Fill < 0.8f)
        {
            it.Fill = 1;
            World.Play(Sfx.Bubble, it.Pos, 0.3f);
            return true;
        }
        switch (it.Def.Key)
        {
            case "foodbowl" when it.Fill < 0.6f:
            case "waterbowl" when it.Fill < 0.6f:
                it.Fill = 1;
                World.Play(it.Def.Key == "foodbowl" ? Sfx.Munch : Sfx.Splat, it.Pos, 0.3f, it.Def.Key == "foodbowl" ? 0.8f : 1.4f);
                _w.Fx.Spark(it.Pos + new Vector2(0, -8 * s), s * 0.6f, _w.Rng, 0.4f, it.Def.Key == "foodbowl" ? new Color4(0.7f, 0.45f, 0.2f, 1) : new Color4(0.5f, 0.75f, 1, 1));
                foreach (var p in _w.Pets.Where(p => Vector2.Distance(p.Pos, it.Pos) < 1200 * s)) p.Log(it.Def.Key == "foodbowl" ? "You filled the food bowl" : "You filled the water bowl");
                return true;
            case "litterbox" or "peepad" when it.Dirt > 0:
                it.Dirt = 0;
                World.Play(Sfx.Scribble, it.Pos, 0.3f, 1.2f);
                _w.Fx.Dust(it.Pos, s * 0.8f, 5, 0.4f, _w.Rng);
                _w.Sticker("cleanup");
                return true;
        }
        return false;
    }

    // ---------------- spray bottle ----------------

    bool _sprayTool;
    double _sprayIdleUntil, _sprayAt = -10;
    string? _sprayNote;
    double _sprayNoteUntil;

    void PickUpSpray(bool on)
    {
        _sprayTool = on;
        _sprayIdleUntil = _clock.Elapsed.TotalSeconds + 90;
        _overlay.SetClickThrough(!on);
        ForceFullRedraw();
    }

    /// <summary>Squirt at whatever's under the cursor.</summary>
    void Spray()
    {
        double now = _clock.Elapsed.TotalSeconds;
        _sprayAt = now;
        _sprayIdleUntil = now + 90;
        World.Play(Sfx.Spray, _w.Cursor, 0.45f);
        var c = _w.Cursor;
        var hit = _w.Pets.Where(p => Vector2.Distance(p.Centre, c) < MathF.Max(70 * _w.Scale, p.Length)).OrderBy(p => Vector2.Distance(p.Centre, c)).ToList();
        if (hit.Count == 0) { _sprayNote = "Missed (point right at a pet)"; _sprayNoteUntil = now + 1.6; return; }
        // A scuffle: both get a soaking.
        var notes = new List<string>();
        foreach (var p in hit.Take(2)) notes.Add(p.Sprayed(_w));
        _sprayNote = notes[0];
        _sprayNoteUntil = now + 4;
        PostAll(new { t = "toast", text = notes[0] });
    }

    RectangleF? SprayRect()
    {
        if (!_sprayTool && _clock.Elapsed.TotalSeconds > _sprayNoteUntil) return null;
        float s = _w.Scale;
        var c = _w.Cursor;
        return RectangleF.FromLTRB(c.X - 200 * s, c.Y - 70 * s, c.X + 200 * s, c.Y + 60 * s);
    }

    void DrawSprayTool()
    {
        if (SprayRect() is not RectangleF b || !Dirty(b)) return;
        double now = _clock.Elapsed.TotalSeconds;
        float s = _w.Scale;
        var c = _w.Cursor;
        if (_sprayTool)
        {
            // The bottle, held just below and right of the pointer, nozzle at the tip.
            Vector2 o = c + new Vector2(10 * s, 6 * s);
            var bottle = new Color4(0.35f, 0.65f, 0.95f, 0.85f);
            var ink = Ui.Ink;
            r_RoundBody(o, s, bottle, ink);
            float since = (float)(now - _sprayAt);
            if (since < 0.35f)
            {
                for (int i = 0; i < 12; i++)
                {
                    float a = -2.4f + i * 0.08f + MathF.Sin(i * 7.3f) * 0.15f, d = (6 + since * 90 + (i % 4) * 4) * s;
                    _r.Disc(c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * d * 0.4f, 1.1f * s, new Color4(0.55f, 0.78f, 1, 0.8f * (1 - since / 0.35f)));
                }
            }
            _r.Text("click to spray · right-click to put it down", c + new Vector2(0, 40 * s), 9 * s, Ui.Pencil, true);
        }
        if (_sprayNote != null && now < _sprayNoteUntil)
        {
            float w = MathF.Min(380 * s, (_sprayNote.Length * 5.6f + 20) * s);
            Ui.Card(_r, c + new Vector2(0, -42 * s), w, 22 * s, 8 * s, s, M.Clamp01((float)(_sprayNoteUntil - now) / 0.4f));
            _r.Text(_sprayNote, c + new Vector2(0, -42 * s), 9.5f * s, Ui.Ink, true);
        }
    }

    void r_RoundBody(Vector2 o, float s, Color4 bottle, Color4 ink)
    {
        _r.FillPolygon(stackalloc Vector2[] { o + new Vector2(-4 * s, 4 * s), o + new Vector2(4 * s, 4 * s), o + new Vector2(5 * s, 22 * s), o + new Vector2(-5 * s, 22 * s) }, bottle);
        _r.Line(o + new Vector2(-4 * s, 4 * s), o + new Vector2(-5 * s, 22 * s), ink, 1 * s);
        _r.Line(o + new Vector2(4 * s, 4 * s), o + new Vector2(5 * s, 22 * s), ink, 1 * s);
        _r.Line(o + new Vector2(-5 * s, 22 * s), o + new Vector2(5 * s, 22 * s), ink, 1 * s);
        _r.FillPolygon(stackalloc Vector2[] { o + new Vector2(-3 * s, 4 * s), o + new Vector2(3 * s, 4 * s), o + new Vector2(2 * s, -1 * s), o + new Vector2(-8 * s, -4 * s), o + new Vector2(-9 * s, -2 * s), o + new Vector2(-3 * s, 0) }, new Color4(0.95f, 0.95f, 0.95f, 1));
        _r.Line(o + new Vector2(2 * s, 1 * s), o + new Vector2(5 * s, 6 * s), ink, 1.2f * s);   // trigger
    }

    // ---------------- leashes ----------------

    RectangleF? LeashRect()
    {
        RectangleF? all = null;
        foreach (var p in _w.Pets)
        {
            if (!p.Leashed) continue;
            var a = p.Collar;
            var b = _w.Cursor;
            var r = RectangleF.FromLTRB(MathF.Min(a.X, b.X) - 20, MathF.Min(a.Y, b.Y) - 20, MathF.Max(a.X, b.X) + 20, MathF.Max(a.Y, b.Y) + 60 * _w.Scale);
            all = all is { } x ? RectangleF.Union(x, r) : r;
        }
        return all;
    }

    void DrawLeashes()
    {
        if (LeashRect() is not RectangleF b || !Dirty(b)) return;
        var red = new Color4(0.85f, 0.2f, 0.2f, 1);
        foreach (var p in _w.Pets)
        {
            if (!p.Leashed) continue;
            Vector2 a = p.Collar, h = _w.Cursor;
            float d = Vector2.Distance(a, h), slack = MathF.Max(0, p.LeashLength - d);
            // A sagging curve (taut when pulled).
            Vector2 mid = (a + h) / 2 + new Vector2(0, slack * 0.45f);
            Vector2 prev = a;
            for (int i = 1; i <= 12; i++)
            {
                float t = i / 12f;
                Vector2 q = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * mid + t * t * h;
                _r.Line(prev, q, red, 1.8f * _w.Scale);
                prev = q;
            }
            _r.Ring(h + new Vector2(0, 5 * _w.Scale), 4 * _w.Scale, red, 1.6f * _w.Scale);   // the loop on your "hand"
        }
    }

    // ---------------- saving ----------------

    List<SavedPet> SavePets() => _w.Pets.Select(p => new SavedPet
    {
        Kind = p.Kind, Name = p.Name, Color = Settings.Hex(p.Color), Accent = Settings.Hex(p.Accent), Size = p.SizeMul, Owner = p.Owner?.Name,
        Age = p.Age, Born = p.Born, Weight = p.Weight, UserBond = p.UserBond,
        Needs = new[] { p.Hunger, p.Thirst, p.Bladder, p.Bowel, p.Energy, p.Attention, p.Boredom, p.Stress, p.Stamina, p.Frustration },
        Restraint = p.Restraint.ToDictionary(k => k.Key.ToString(), k => MathF.Round(k.Value, 3)),
        Skills = p.Skills.ToDictionary(k => k.Key.ToString(), k => MathF.Round(k.Value, 3)),
        Vocabulary = p.Vocabulary.ToList(),
        PetBonds = _w.Pets.Where(o => o != p).GroupBy(o => o.Name).ToDictionary(g => g.Key, g => MathF.Round(p.PetBond(g.First()), 3)),
        Log = p.CareLog.TakeLast(40).Select(l => new SavedLog { When = l.When, Text = l.Text }).ToList(),
        Sprays = p.Sprays, Treats = p.TreatsGiven,
        Health = p.Health, Clean = p.Clean, Sick = p.Sick.ToString(),
        Temper = p.Temper.ToString(), Female = p.Female, Wear = p.Wear, WearColour = p.WearColour, Rare = p.Rare, Pregnant = p.Pregnant, Mother = p.MotherName, LastLitter = p.LastLitter,
    }).ToList();

    void RestorePets()
    {
        double away = _settings.LastSeen is DateTime ls ? Math.Clamp((DateTime.Now - ls).TotalHours, 0, 72) : 0;
        var made = new List<(Pet p, SavedPet s)>();
        foreach (var sp in _settings.Pets)
        {
            var pet = SpawnPet(sp.Kind, quiet: true);
            pet.Name = sp.Name.Length > 0 ? sp.Name : pet.Name;
            if (sp.Color.Length == 7) pet.Color = Settings.ParseHex(sp.Color);
            if (sp.Accent.Length == 7) pet.Accent = Settings.ParseHex(sp.Accent);
            pet.SizeMul = Math.Clamp(sp.Size, 0.5f, 2.5f);
            pet.Age = Math.Clamp(sp.Age, 0, 1);
            if (sp.Born is DateTime b) pet.Born = b;
            pet.Weight = Math.Clamp(sp.Weight, 0, 1);
            pet.UserBond = Math.Clamp(sp.UserBond, -1, 1);
            if (sp.Needs is { Length: >= 10 } n)
                (pet.Hunger, pet.Thirst, pet.Bladder, pet.Bowel, pet.Energy, pet.Attention, pet.Boredom, pet.Stress, pet.Stamina, pet.Frustration) = (n[0], n[1], n[2], n[3], n[4], n[5], n[6], n[7], n[8], n[9]);
            foreach (var (k, v) in sp.Restraint) if (Enum.TryParse<Habit>(k, out var h)) pet.Restraint[h] = Math.Clamp(v, 0, 1);
            foreach (var (k, v) in sp.Skills) if (Enum.TryParse<PetSkill>(k, out var sk)) pet.Skills[sk] = Math.Clamp(v, 0, 1);
            if (sp.Vocabulary.Count > 0) { pet.Vocabulary.Clear(); pet.Vocabulary.AddRange(sp.Vocabulary); }
            foreach (var l in sp.Log) pet.CareLog.Add((l.When, l.Text));
            pet.Sprays = sp.Sprays; pet.TreatsGiven = sp.Treats;
            pet.Health = Math.Clamp(sp.Health, 0, 1); pet.Clean = Math.Clamp(sp.Clean, 0, 1);
            if (Enum.TryParse<Illness>(sp.Sick, out var ill)) pet.Sick = ill;
            if (Enum.TryParse<Temperament>(sp.Temper, out var tm)) pet.Temper = tm;
            if (sp.Female is bool fem) pet.Female = fem;
            pet.Wear = sp.Wear; if (sp.WearColour.Length == 7) pet.WearColour = sp.WearColour;
            if (Pet.RareCoats.Contains(sp.Rare)) pet.GiveRareCoat(sp.Rare);
            pet.Pregnant = sp.Pregnant; pet.MotherName = sp.Mother; pet.LastLitter = sp.LastLitter;
            if (sp.Owner != null && _w.Figures.FirstOrDefault(f => f.Name == sp.Owner) is { } owner) { pet.Owner = owner; pet.Bond[owner.Id] = 0.5f; }
            pet.TimeAway(away);
            made.Add((pet, sp));
        }
        foreach (var (p, sp) in made)
        {
            if (sp.Mother.Length > 0 && made.FirstOrDefault(m => m.p.Name == sp.Mother).p is { } mum) p.MotherId = mum.Id;
            foreach (var (name, v) in sp.PetBonds)
                if (made.FirstOrDefault(m => m.p.Name == name).p is { } o) p.SetPetBond(o, v);
        }
    }
}
