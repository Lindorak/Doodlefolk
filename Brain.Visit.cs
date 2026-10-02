using System.Numerics;

namespace Doodlefolk;

/// <summary>What a visitor does while it's here (see App.Visitors), and how the locals react.</summary>
sealed partial class Brain
{
    VisitorKind _visit;
    int _visitStep;
    float _visitX, _visitLeaveAt;
    Figure? _visitTarget;
    public bool VisitOver { get; private set; }

    public void StartVisit(VisitorKind kind, World w, float towardX)
    {
        _visit = kind;
        _visitStep = 0;
        _visitX = towardX;
        _visitLeaveAt = _t0 + rng.Range(150, 260);
        Go(G.Visit, 1e6f);
    }

    void DoVisit(World w)
    {
        if (_t0 > _visitLeaveAt && _visitStep < 9) { _visitStep = 9; _t = 0; f.Emote(Gestures ? "👋" : "bye, everyone!", 2); f.SetAction(Act.Wave); }
        switch (_visitStep)
        {
            case 0:   // arrive
                if (!MoveToward(_visitX, 40 * S)) return;
                f.Emote(Gestures ? "👋" : _visit switch
                {
                    VisitorKind.Bard => "hello, good folk! a song?",
                    VisitorKind.MailCarrier => "special delivery!",
                    VisitorKind.Knight => "hail, friends!",
                    VisitorKind.Artist => "ooh, such faces to paint!",
                    VisitorKind.Ghost => "boo!",
                    VisitorKind.Explorer => "what a desktop!",
                    VisitorKind.Chef => "who's hungry?!",
                    _ => "what lovely soil!",
                }, 2.4f);
                if (_visit == VisitorKind.Ghost) foreach (var o in w.Figures.Where(o => o != f && Vector2.Distance(o.Base, f.Base) < 400 * S)) { o.Emote("!", 1); o.Brain.Fear = M.Clamp01(o.Brain.Fear + 0.25f); }
                _visitStep = 1; _t = 0;
                return;
            case 1:   // the visit itself
                Visiting(w);
                return;
            case 9:   // goodbye, then leave
                f.DesiredVX = 0;
                if (_t > 2.2f) { LeaveKeepsake(w); VisitOver = true; }
                return;
        }
    }

    void Visiting(World w)
    {
        switch (_visit)
        {
            case VisitorKind.Bard:
                if (f.Action != Act.Fidget || f.ActionT >= f.FidgetDur) f.StartFidget(Fidget.Groove);
                if (rng.NextDouble() < World.Dt * 0.5) f.Emote(rng.NextDouble() < 0.5 ? "♪ la la ♪" : "♫", 1.2f);
                break;
            case VisitorKind.MailCarrier:
                if (_visitStep == 1 && _t > 1.5f && w.MakeItem?.Invoke("giftcrate") is { } crate)
                {
                    crate.Pos = f.Base + new Vector2(f.Facing * 30 * S, -20 * S); crate.Vel = new Vector2(f.Facing * 80 * S, -150 * S); crate.OnGround = false;
                    w.CrateDelivered?.Invoke(crate);
                    f.Emote(Gestures ? "🎁" : "for you all!", 2);
                    _visitLeaveAt = MathF.Min(_visitLeaveAt, _t0 + 25);
                    _visitStep = 2;   // done: just stand about
                }
                break;
            case VisitorKind.Knight:
                // Salute each local in turn.
                if (_visitTarget == null || _t > 4)
                {
                    _visitTarget = w.Figures.Where(o => o != f && o.Visitor == VisitorKind.None && o.Mode == Mode.Control).OrderBy(_ => rng.Next()).FirstOrDefault();
                    _t = 0;
                    if (_visitTarget != null) { FaceTo(_visitTarget.Base.X); f.SetAction(Act.Wave); f.Emote(Gestures ? "🫡" : $"hail, {_visitTarget.Name}!", 1.6f); _visitTarget.Emote(Gestures ? "🙌" : "hail!", 1.2f); }
                }
                break;
            case VisitorKind.Artist:
                if (_visitTarget == null) _visitTarget = w.Figures.Where(o => o != f && o.Visitor == VisitorKind.None).OrderBy(_ => rng.Next()).FirstOrDefault();
                if (_visitTarget != null) FaceTo(_visitTarget.Base.X);
                f.SetAction(Act.Tap);
                if (rng.NextDouble() < World.Dt * 0.2) f.Emote(Gestures ? "🎨" : rng.NextDouble() < 0.5 ? "hold still…" : "*scritch scratch*", 1.2f);
                break;
            case VisitorKind.Ghost:
                if (rng.NextDouble() < World.Dt * 0.15) f.Emote(Gestures ? "👻" : rng.NextDouble() < 0.5 ? "ooOOoo" : "boo!", 1.2f);
                if (rng.NextDouble() < World.Dt * 0.3) _visitX = rng.Range(f.Base.X - 300 * S, f.Base.X + 300 * S);
                MoveToward(_visitX, 10 * S);
                break;
            case VisitorKind.Explorer:
                f.SetAction(_t % 6 < 3 ? Act.Talk : Act.Stand);
                if (rng.NextDouble() < World.Dt * 0.15)
                    f.Emote(Gestures ? "🗺" : new[] { "I once saw nine monitors!", "a desktop with no windows at all!", "a taskbar at the TOP!", "a whole city of icons!", "the wallpaper was a mountain!", "a recycle bin, overflowing…" }[rng.Next(6)], 2.6f);
                break;
            case VisitorKind.Chef:
                f.SetAction(Act.Tap);
                if (_t > 6 && w.Items.Count < 55)
                {
                    _t = 0;
                    if (w.MakeItem?.Invoke(new[] { "pizza", "burger", "cake", "cookie", "fish" }[rng.Next(5)]) is { } dish) { dish.Pos = f.Base + new Vector2(rng.Range(-40, 40) * S, -30 * S); dish.Vel = Vector2.Zero; dish.OnGround = false; f.Emote(Gestures ? "🍲" : "bon appétit!", 1.4f); }
                }
                break;
            case VisitorKind.Gardener:
                if (_t > 9 && w.Items.Count(i => i.IsPlant) < 12 && w.MakeItem?.Invoke("seedpatch") is { } patch)
                {
                    _t = 0;
                    patch.Pos = f.Base + new Vector2(f.Facing * 24 * S, -2 * S); patch.Vel = Vector2.Zero; patch.OnGround = false;
                    patch.PlantKind = new[] { "tulip", "sunflower", "tomatoplant" }[rng.Next(3)]; patch.Fill = 1;
                    f.SetAction(Act.Tap);
                    _visitX = f.Base.X + rng.Range(-200, 200) * S;
                }
                else MoveToward(_visitX, 8 * S);
                break;
        }
    }

    void LeaveKeepsake(World w)
    {
        string? key = _visit switch
        {
            VisitorKind.Bard => "lute", VisitorKind.Knight => "pennant", VisitorKind.Artist => "portrait", VisitorKind.Ghost => "lantern",
            VisitorKind.Explorer => "map", _ => null,
        };
        if (key == null || w.Items.Count >= 58 || w.MakeItem?.Invoke(key) is not { } it) return;
        it.Pos = f.Base + new Vector2(0, -10 * S); it.Vel = Vector2.Zero; it.OnGround = false;
        if (key == "portrait" && _visitTarget != null) it.Color = _visitTarget.Color;
    }

    /// <summary>Locals: a bard's playing draws a crowd.</summary>
    void VisitorOptions(World w, OptionList opts)
    {
        if (f.Visitor != VisitorKind.None) return;
        var bard = w.Figures.FirstOrDefault(o => o.Visitor == VisitorKind.Bard && o.Brain._visitStep == 1 && Vector2.Distance(o.Base, f.Base) < 1500 * S);
        if (bard != null)
            opts.Add(0.6f + P.Sociability * 0.6f + f.Tastes.Of(Thing.Dancing) * 0.4f, () =>
            {
                float side = MathF.Sign(f.Base.X - bard.Base.X); if (side == 0) side = 1;
                Navigate(() => w.Figures.Contains(bard) ? bard.Base + new Vector2(side * rng.Range(60, 140) * S, 0) : null, 20 * S, false, () =>
                {
                    FaceTo(bard.Base.X); Go(G.Groove, rng.Range(8, 16));
                    Write("bard", V($"A travelling bard called {bard.Name} played for us!", $"{bard.Name} THE BARD PLAYED FOR US!!", $"Some bard played. {bard.Name}. Catchy.", $"A bard called {bard.Name} sang… it was lovely.", $"{bard.Name}'s song drifted over the desktop."), "♪", 900);
                }, WalkPurpose.Watch);
            }, $"Listen to {bard.Name} the bard");
        // A delivered crate: open it!
        if (w.Items.FirstOrDefault(i => i.Def.Key == "giftcrate" && i.OnGround && Vector2.Distance(i.Pos, f.Base) < 1200 * S) is { } crate)
            opts.Add(2.5f + P.Curiosity, () => Navigate(() => w.Items.Contains(crate) ? crate.Pos : null, 14 * S, false, () =>
            {
                if (!w.Items.Contains(crate)) { Go(G.Idle, 1); return; }
                string what = w.OpenCrate?.Invoke(crate, f) ?? "";
                Write("crate", V($"Opened a gift crate! {what}", $"GIFT CRATE!!! {what}", $"Opened a crate. {what}", $"I opened the gift crate… {what}", $"The crate gave up its secret. {what}"), "★", 0);
                Go(G.Cheer, 1.5f);
            }, WalkPurpose.Other), "Open the gift crate");
    }
}
