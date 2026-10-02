using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Babies arriving and growing up, home flags, and the tournament board.</summary>
sealed partial class App
{
    static readonly string[] BabyNames =
    {
        "Pip", "Bean", "Dot", "Tiny", "Squeak", "Button", "Pebble", "Sprout", "Nugget", "Bubba", "Peanut", "Mo", "Kiki",
        "Pixel", "Doodle", "Scribble", "Smudge", "Tippy", "Bitsy", "Noodle",
    };

    /// <summary>Two sweethearts' little one: their colours and natures mixed, drawn in right between them.</summary>
    void MakeBaby(Figure a, Figure b)
    {
        var rng = _w.Rng;
        var c = Color4.Lerp(a.Color, b.Color, 0.5f);
        c = new Color4(Math.Clamp(c.R + rng.Range(-0.06f, 0.06f), 0, 1), Math.Clamp(c.G + rng.Range(-0.06f, 0.06f), 0, 1), Math.Clamp(c.B + rng.Range(-0.06f, 0.06f), 0, 1), 1);
        float Mix(float x, float y) => Math.Clamp((x + y) / 2 + rng.Range(-0.18f, 0.18f), 0, 1);
        var t = new Personality
        {
            Energy = Mix(a.Traits.Energy, b.Traits.Energy), Curiosity = Mix(a.Traits.Curiosity, b.Traits.Curiosity), Bravery = Mix(a.Traits.Bravery, b.Traits.Bravery),
            Playfulness = Mix(a.Traits.Playfulness, b.Traits.Playfulness), Aggression = Mix(a.Traits.Aggression, b.Traits.Aggression) * 0.8f, Sociability = Mix(a.Traits.Sociability, b.Traits.Sociability),
        };
        float adult = (a.SizeMul + b.SizeMul) / 2;
        float size = adult * 0.5f;
        var free = BabyNames.Where(n => _w.Figures.All(f => f.Name != n)).ToList();
        string name = UniqueName(free.Count > 0 ? free[rng.Next(free.Count)] : BabyNames[rng.Next(BabyNames.Length)]);
        var plat = _w.Env.SupportAt(a.Base.X, a.Base.Y, a.GroundHwnd) ?? _w.Env.Below(a.Base.X, a.Base.Y - 2);
        if (plat == null) return;
        var nf = new Figure(c, name, _w.Scale * size, t, rng) { SizeMul = size };
        nf.PlaceAt(plat, M.ClampIn((a.Base.X + b.Base.X) / 2, plat.X1 + 6, plat.X2 - 6));
        nf.Gender = (Gender)rng.Next(3);
        nf.Look = Look.Generate(t, rng.Next(), nf.Gender);
        nf.Look.Hat = "";
        // Likes and dislikes: somewhere between the two of them.
        var tastes = a.Tastes.Clone();
        foreach (var (k, v) in b.Tastes.Opinions) tastes.Opinions[k] = Math.Clamp((tastes.Opinions.GetValueOrDefault(k) + v) / 2 + rng.Range(-0.25f, 0.25f), -1, 1);
        if (rng.NextDouble() < 0.5) tastes.FavoriteColour = b.Tastes.FavoriteColour;
        nf.Tastes = tastes;
        nf.Brain.ParentIds.Add(a.Id); nf.Brain.ParentIds.Add(b.Id);
        nf.Brain.ParentNames.Add(a.Name); nf.Brain.ParentNames.Add(b.Name);
        nf.Brain.Grown = 0;
        nf.Brain.AdultSize = adult;
        _w.Figures.Add(nf);
        nf.Brain.WasBorn(a, b);
        a.Brain.BabyArrived(nf, b);
        b.Brain.BabyArrived(nf, a);
        World.Play(Sfx.Chime, nf.Base, 0.5f, 1.3f);
        World.Log($"baby {nf.Name} born to {a.Name} and {b.Name}");
    }

    double _growAt;

    /// <summary>Little ones get bigger as they grow up (rebuilt at the new size every so often).</summary>
    void FamilyFrame(double now)
    {
        if (now < _growAt) return;
        _growAt = now + 30;
        foreach (var f in _w.Figures.ToArray())
        {
            if (f.Brain.ParentIds.Count == 0 || f.Mode != Mode.Control || !f.Grounded) continue;
            float target = f.Brain.AdultSize * (0.5f + 0.5f * f.Brain.Grown);
            if (MathF.Abs(target - f.SizeMul) > 0.04f || (f.Brain.Grown >= 1 && MathF.Abs(target - f.SizeMul) > 0.001f)) ResizeFigure(f, target);
        }
    }

    /// <summary>A little flag on someone's home, in their colour with their name.</summary>
    void DrawHomeFlags(bool onlyStatic = false)
    {
        foreach (var it in _w.Items)
        {
            if (IsStatic(it) != onlyStatic) continue;
            if (it.OwnerId == 0 || it.Holder != null || it.FlagRect() is not { } fr || !Dirty(fr)) continue;
            float s = it.Scale;
            Vector2 pole = new(fr.Left + 2 * s, fr.Bottom);
            Vector2 top = new(pole.X, fr.Top + 2 * s);
            _r.Line(pole, top, Ui.Ink.A(0.85f), 1.3f * s);
            var flagCol = it.OwnerColour;
            _r.FillPolygon(stackalloc Vector2[] { top, top + new Vector2(16 * s, 4.5f * s), top + new Vector2(0, 9 * s) }, flagCol);
            _r.Text(it.OwnerName + (it.OwnerName.EndsWith('s') ? "'" : "'s"), top + new Vector2(20 * s + (it.OwnerName.Length + 2) * 2.6f * s, 4.5f * s), 9.5f * s, Ui.Ink.A(0.8f), true);
        }
    }

    // ---------------- tournament ----------------

    double _tourneyLast;

    string StartTourney()
    {
        if (World.Calm) return "Calm mode is on: no tournaments";
        if (_w.Tourney is { Over: false }) return "A tournament's already on";
        var t = Tournament.Start(_w, out string why);
        if (t == null) return why;
        StopGame(false);
        _w.Tourney = t;
        foreach (var f in t.Entrants) f.Emote(f.Traits.Aggression > 0.6f ? "bring it!" : "tournament!", 1.4f);
        World.Log($"tournament: {string.Join(", ", t.Entrants.Select(f => f.Name))}");
        return $"Tournament! {t.Entrants.Count} fighters, sparring for the crown.";
    }

    void TourneyFrame(double now)
    {
        float dt = (float)Math.Clamp(now - _tourneyLast, 0, 0.1);
        _tourneyLast = now;
        if (_w.Tourney is not { } t) return;
        t.Step(dt, _w);
        if (t.Over) _w.Tourney = null;
    }

    RectangleF? TourneyRect()
    {
        if (_w.Tourney is not { } t) return null;
        float s = _w.Scale;
        var c = t.Arena + new Vector2(0, -250 * s);
        return RectangleF.FromLTRB(c.X - 170 * s, c.Y - 46 * s, c.X + 170 * s, c.Y + 46 * s);
    }

    void DrawTourney()
    {
        if (_w.Tourney is not { } t || TourneyRect() is not RectangleF b || !Dirty(b)) return;
        float s = _w.Scale;
        Vector2 c = new(b.Left + b.Width / 2, b.Top + b.Height / 2);
        Ui.Card(_r, c, b.Width - 4 * s, b.Height - 4 * s, 12 * s, s, 1);
        string title = t.Phase is "done" ? "Champion!" : t.Phase == "gather" ? "Tournament" : $"Tournament · {t.RoundName}";
        _r.Text(title, c + new Vector2(0, -24 * s), 13 * s, Ui.Pencil, true);
        bool big = t.Headline.Length <= 3 || t.Headline == "FIGHT!";
        _r.Text(t.Headline, c + new Vector2(0, big ? 2 * s : 0), (big ? 30 : 19) * s, t.Phase is "result" or "done" ? Ui.Accent : Ui.Ink, true);
        var still = t.Round.Concat(t.Next).Concat(new[] { t.A, t.B }.Where(x => x != null)!).Distinct().Select(x => x!.Name);
        _r.Text(t.Phase == "done" ? $"{t.Entrants.Count} fought · {t.Bouts} bouts" : $"Still in: {string.Join(", ", still)}", c + new Vector2(0, 28 * s), 10 * s, Ui.Pencil, true);
    }
}
