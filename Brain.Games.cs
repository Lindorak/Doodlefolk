using System.Numerics;

namespace Doodlefolk;

enum GameKind { HideSeek, Tag, Catch }

/// <summary>A game with you: hide-and-seek (you seek), tag (you against them) or catch (you and one of them).</summary>
sealed class UserGame
{
    public GameKind Kind;
    public readonly List<Figure> Players = new();
    public float T;
    /// <summary>Hide-and-seek: seconds of counting left (they hide while you "close your eyes").</summary>
    public float Count;
    public readonly List<Figure> Found = new();
    /// <summary>Tag: who's it (null: you are).</summary>
    public Figure? It;
    public float Grace;
    /// <summary>Catch: the ball, who you're playing with, and how many catches in a row.</summary>
    public Prop? Ball;
    public Figure? Thrower;
    public int Streak, Best;
    public int Flight;          // 0 resting, 1 flying to you, 2 flying to them
    public float Quiet;         // seconds without anyone touching the ball
    public bool Over;

    public string Title => Kind switch { GameKind.HideSeek => "hide-and-seek", GameKind.Tag => "tag", _ => "catch" };
    public bool Hidden(Figure f) => Kind == GameKind.HideSeek && Players.Contains(f) && !Found.Contains(f);
}

sealed partial class Brain
{
    Item? _hideBehind;
    bool _hidden;
    int _hideTries;
    float _peekAt, _gameNoteAt;

    /// <summary>Asked to play: most say yes; someone who can't stand you won't.</summary>
    public bool WillPlay => f.Mode == Mode.Control && !f.Hunter && !f.Dead && !InFight && Match == null && UserFondness > -0.3f && _g != G.Sleep;

    public void JoinGame(UserGame g, World w)
    {
        _hideTries = 0;
        _hidden = false;
        _hideBehind = null;
        f.HidingBehind = false;
        f.Camo = 0;
        switch (g.Kind)
        {
            case GameKind.HideSeek:
                f.Emote(V("I'll hide!", "hiding!! don't look!", "fine. hiding.", "eep! hiding...", "into the shadows I go"), 1.4f);
                PlanHide(w);
                break;
            case GameKind.Tag:
                f.Emote(V("you're it!", "TAG! you're it!!", "catch me. if you can.", "eek! run!", "whee!"), 1.3f);
                Go(G.Game, 600);
                break;
            default:
                f.Emote(V("catch!", "CATCH!! yes!", "ok, catch.", "um, ok!", "a game of catch..."), 1.2f);
                Go(G.Game, 600);
                break;
        }
    }

    // ---------------- hide-and-seek ----------------

    void PlanHide(World w)
    {
        _hideTries++;
        var env = w.Env;
        var seg = env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        // Behind something big enough to cover it, nobody else hiding there, not too far.
        Item? best = null;
        float bestScore = float.MaxValue;
        foreach (var it in w.Items)
        {
            if (it.Holder != null || !it.OnGround || it.Def.Key == "hammock") continue;
            var b = it.Bounds();
            if (b.Height < f.Height * 0.45f || b.Width < f.Height * 0.5f) continue;   // big enough to actually cover them
            if (w.Figures.Any(o => o != f && o.Brain._hideBehind == it)) continue;
            float d = Vector2.Distance(new Vector2(b.X + b.Width / 2, b.Bottom), f.Base);
            if (d > 2400 * S) continue;
            float score = d + rng.Range(0, 700) * S;
            if (score < bestScore) { bestScore = score; best = it; }
        }
        if (best != null)
        {
            var b = best.Bounds();
            float x = b.X + b.Width * rng.Range(0.3f, 0.7f);
            if (env.Below(x, b.Bottom - 14 * S) is { } p && (seg == null || SameSegment(p, seg) || w.Nav.FindPath(seg, f.Base.X, p, x, MyMover, MoveCost, 300) != null))
            {
                _hideBehind = best;
                var a = Anchor.On(env, p, x);
                Navigate(() => a.Resolve(env), 6 * S, true, HideNow, WalkPurpose.Other);
                return;
            }
        }
        // Nothing to hide behind: somewhere out of the way, keeping very still (and very faint).
        _hideBehind = null;
        if (seg != null)
            foreach (var p in env.Platforms.Where(p => p.X2 - p.X1 > 70 * S).OrderBy(_ => rng.Next()).Take(6))
            {
                float x = rng.Range(p.X1 + 20 * S, p.X2 - 20 * S);
                if (MathF.Abs(x - w.Cursor.X) < 200 * S) continue;
                if (SameSegment(p, seg) || w.Nav.FindPath(seg, f.Base.X, p, x, MyMover, MoveCost, 300) != null)
                {
                    var a = Anchor.On(env, p, x);
                    Navigate(() => a.Resolve(env), 8 * S, true, HideNow, WalkPurpose.Other);
                    return;
                }
            }
        HideNow();
    }

    void HideNow()
    {
        _hidden = true;
        f.HidingBehind = _hideBehind != null;
        _peekAt = _t0 + rng.Range(6, 14);
        Go(G.Game, 600);
    }

    public void FoundByUser(World w, UserGame g)
    {
        _hidden = false;
        f.HidingBehind = false;
        f.Camo = 0;
        _hideBehind = null;
        int n = g.Found.Count;
        f.Emote(n == 1 ? V("you found me!", "AWW you found me first!", "...lucky.", "eep! found!", "the shadows betrayed me") : V("found!", "you got me!!", "hmph. found.", "oh no, found!", "found... again"), 1.6f);
        World.Play(Sfx.Laugh, f.Jt[J.Head], 0.35f, 1.1f);
        FeelUser(0.02f, "Played hide-and-seek");
        Go(G.Cheer, 1.1f);
    }

    void DoHideSeek(World w, UserGame g)
    {
        if (g.Found.Contains(f))
        {
            // Found already: watch you hunt for the others.
            f.DesiredVX = 0;
            f.LookAt = w.Cursor;
            FaceTo(w.Cursor.X);
            if (rng.NextDouble() < World.Dt * 0.08) f.Emote(V("warmer!", "you're SO close!", "...nope.", "hehe", "cold, cold..."), 1);
            return;
        }
        if (!_hidden)
        {
            // Lost the way, or got knocked out of the hiding place: try again while there's time, otherwise right here.
            if (g.Count > 0 && _hideTries < 3) PlanHide(w);
            else HideNow();
            return;
        }
        f.DesiredVX = 0;
        if (_hideBehind is { } it)
        {
            if (!w.Items.Contains(it) || it.Holder != null || MathF.Abs(it.Pos.X - f.Base.X) > it.Bounds().Width) { _hidden = false; _hideBehind = null; f.HidingBehind = false; return; }
            // Duck down behind it; every so often (when you're far off) peek over the top.
            bool peek = _t0 > _peekAt && Vector2.Distance(w.Cursor, f.Base) > 450 * S;
            if (_t0 > _peekAt + 1.1f) _peekAt = _t0 + rng.Range(7, 15);
            float h = it.Bounds().Height / f.Height;
            f.SetAction(peek ? Act.Stand : h > 0.75f ? Act.Stand : h > 0.45f ? Act.SitFloor : Act.Curl);
            f.LookAt = w.Cursor;
        }
        else
        {
            f.SetAction(Act.SitFloor);
            f.Camo = MathF.Min(1, f.Camo + World.Dt * 1.5f);
        }
        // Giggles when you get close: a hint (playful ones can't help it).
        float d = Vector2.Distance(w.Cursor, f.Jt[J.Pelvis]);
        if (g.Count <= 0 && d < 300 * S && rng.NextDouble() < World.Dt * (0.15 + P.Playfulness * 0.5)) f.Emote(V("hehe", "hehehe!", "...", "*giggle*", "shh"), 0.8f);
    }

    // ---------------- tag ----------------

    public void Tagged(UserGame g)
    {
        f.Emote(V("aw, I'm it!", "I'M IT! here I come!!", "you'll regret that.", "oh no... I'm it", "the hunt begins"), 1.4f);
        World.Play(Sfx.Pip, f.Jt[J.Head], 0.35f, 1.2f);
        _huntPlanT = 0.6f;
        Go(G.Game, 600);
    }

    void DoTag(World w, UserGame g)
    {
        Vector2 cur = w.Cursor;
        Stamina = MathF.Max(0, Stamina - World.Dt * 0.003f);
        if (Stamina < 0.15f && g.It != f)
        {
            g.Players.Remove(f);
            f.Emote(V("I'm pooped", "so... tired... best game EVER", "I'm done.", "need a rest...", "rest now"), 1.6f);
            Go(G.SitFloor, rng.Range(8, 15));
            return;
        }
        if (g.It == f)
        {
            // Chasing you: the hunter's route-finding, but friendly.
            f.LookAt = cur;
            if (g.Grace <= 0 && (f.DistanceTo(cur, out _) <= 6 * S || Vector2.Distance(f.Jt[J.HandN], cur) < 30 * S || Vector2.Distance(f.Jt[J.HandF], cur) < 30 * S))
            {
                g.It = null;
                g.Grace = 1.5f;
                f.Emote(V("tag! you're it!", "TAG!!! YOU'RE IT!", "tag. obviously.", "got you! you're it", "tag."), 1.4f);
                World.Play(Sfx.Laugh, f.Jt[J.Head], 0.4f, 1.1f);
                Go(G.Cheer, 0.9f);
                return;
            }
            _huntPlanT -= World.Dt;
            _tagChase = true;
            if (_huntPlanT <= 0) _huntPlanT = 0.4f;
            HuntStep(w, cur);
            _tagChase = false;
            return;
        }
        // You're it: keep away from the cursor.
        float d = Vector2.Distance(cur, f.Jt[J.Pelvis]);
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (seg == null) return;
        if (d < 380 * S)
        {
            float away = MathF.Sign(f.Base.X - cur.X);
            if (away == 0) away = 1;
            float x = M.ClampIn(f.Base.X + away * 260 * S, seg.X1 + 6 * S, seg.X2 - 6 * S);
            bool cornered = MathF.Abs(x - f.Base.X) < 40 * S;
            _run = true;
            if (cornered && d < 170 * S && f.Grounded)
            {
                // Backed into a corner: leap over you!
                f.RequestJump(new Vector2(-away * 520 * S, -760 * S), 0.05f);
                if (rng.NextDouble() < 0.5) f.Emote(V("nope!", "WHEEE", "too slow", "eek!", "ha"), 0.8f);
            }
            else MoveToward(x, 8 * S);
            if (rng.NextDouble() < World.Dt * 0.25) f.Emote(V("can't catch me!", "nyah nyah!!", "too slow.", "eek!", "catch me if you can"), 1);
        }
        else
        {
            f.DesiredVX = 0;
            FaceTo(cur.X);
            f.LookAt = cur;
            if (f.Action != Act.Fidget || f.ActionT >= f.FidgetDur) f.StartFidget(P.Playfulness > 0.5f ? Fidget.Groove : Fidget.Stretch);
        }
    }

    bool _tagChase;

    // ---------------- catch ----------------

    void DoCatch(World w, UserGame g)
    {
        var b = g.Ball;
        if (b == null || !w.Props.Contains(b)) { g.Over = true; return; }
        if (g.Thrower != f) { f.DesiredVX = 0; f.LookAt = b.Pos; FaceTo(b.Pos.X); return; }
        if (f.Carrying == b) { _bringToUser = true; Go(G.Carry, 8); return; }
        f.LookAt = b.Pos;
        if (b.Holder != null || b.Pinned)
        {
            // You've got it: wait to catch it back.
            f.DesiredVX = 0;
            FaceTo(w.Cursor.X);
            f.SetAction(Act.Ready);
            if (g.Quiet > 6 && _t0 > _gameNoteAt) { _gameNoteAt = _t0 + 8; f.Emote(V("throw it!", "THROW IT! THROW IT!", "any day now.", "um... throw?", "toss it back..."), 1.2f); }
            return;
        }
        // Coming at it through the air: catch it.
        if (!b.OnGround && Vector2.Distance(b.Pos, f.HoldPoint) < b.Radius + 32 * S)
        {
            PickUp(b, null);
            _bringToUser = true;
            if (g.Flight == 2) { g.Streak++; g.Best = Math.Max(g.Best, g.Streak); }
            g.Flight = 0;
            f.Emote(g.Streak >= 3 ? $"x{g.Streak}!" : V("got it!", "GOT IT!!", "caught.", "got it!", "mine"), 1);
            World.Play(Sfx.BounceBall, b.Pos, 0.4f);
            Practice(SkillKind.Ball, 0.01f);
            return;
        }
        // Run under where it'll come down, or go and fetch it.
        if (!b.OnGround && b.Vel.LengthSquared() > 90000 * S * S)
        {
            float targetY = f.Base.Y - f.Height * 0.6f, dy = targetY - b.Pos.Y;
            float disc = b.Vel.Y * b.Vel.Y + 2 * b.Grav * dy;
            float t = disc > 0 ? (b.Vel.Y + MathF.Sqrt(disc)) / b.Grav : 0.5f;
            _run = true;
            MoveToward(b.Pos.X + b.Vel.X * Math.Clamp(t, 0, 2.5f), 10 * S);
            return;
        }
        ScoopThen(b, w, () => { _bringToUser = true; g.Flight = 0; });
    }

    // ---------------- shared ----------------

    void DoGame(World w)
    {
        if (w.Game is not { Over: false } g || !g.Players.Contains(f)) { EndGameForMe(); Go(G.Idle, 1); return; }
        switch (g.Kind)
        {
            case GameKind.HideSeek: DoHideSeek(w, g); break;
            case GameKind.Tag: DoTag(w, g); break;
            default: DoCatch(w, g); break;
        }
    }

    void EndGameForMe()
    {
        _hidden = false;
        _hideBehind = null;
        f.HidingBehind = false;
        f.Camo = 0;
    }

    /// <summary>The game's over: how it went, and how they feel about it.</summary>
    public void GameOver(UserGame g, bool won)
    {
        bool hidingStill = g.Kind == GameKind.HideSeek && !g.Found.Contains(f);
        EndGameForMe();
        if (f.Mode != Mode.Control) return;
        Cheered(0.3f);
        Loneliness = MathF.Max(0, Loneliness - 0.3f);
        Boredom = MathF.Max(0, Boredom - 0.4f);
        FeelUser(0.05f, $"Played {g.Title} with them");
        string say = g.Kind switch
        {
            GameKind.HideSeek when hidingStill => V("here I am! I win!", "YOU NEVER FOUND ME!!", "best hider. obviously.", "I was here all along...", "I was one with the shadows"),
            GameKind.HideSeek => V("again!", "AGAIN AGAIN!", "that was okay.", "that was fun!", "a fine game"),
            GameKind.Tag => V("that was fun!", "BEST GAME EVER!", "I let you win.", "phew... fun!", "good game"),
            _ => g.Best >= 5 ? $"{g.Best} in a row!" : V("good game!", "more catch later!!", "not bad.", "thanks for playing!", "a good game"),
        };
        f.Emote(say, 2);
        if (g.Kind != GameKind.Catch || g.Thrower == f)
            Write("game:" + g.Kind, g.Kind switch
            {
                GameKind.HideSeek when hidingStill => V("Played hide-and-seek with you. You never found me!", "HIDE AND SEEK!!! You NEVER found me!!", "Hide-and-seek. Undefeated.", "We played hide-and-seek. I hid so well you gave up.", "I hid in the quiet places. You didn't find me."),
                GameKind.HideSeek => V("Played hide-and-seek with you.", "HIDE AND SEEK with you!! You found me but it was SO fun!", "Played hide-and-seek. You found me. Whatever.", "We played hide-and-seek! My heart was pounding.", "Hide-and-seek with you, under the windows."),
                GameKind.Tag => V("Played tag with you!", "TAG!!! Ran SO fast!", "Played tag. I was faster.", "We played tag! I squeaked a lot.", "We chased each other across the screen."),
                _ => V($"Played catch with you. Best streak: {g.Best}.", $"CATCH with you!! {g.Best} in a row!!", $"Played catch. {g.Best} in a row. Could do better.", $"We played catch! {g.Best} in a row!", $"Back and forth, back and forth. {g.Best} in a row."),
            }, "★", 600);
        if (_g == G.Game) Go(G.Cheer, 1.2f);
    }
}

sealed partial class Brain
{
    bool _cheesed;

    /// <summary>Photo time: stop, face the camera and pose (the first one to notice tells everyone).</summary>
    double _photoAt;

    /// <summary>Photo time: squeeze in next to the group (if it's not far), face the camera and pose.</summary>
    public void PosePhoto(bool first, World w, Vector2 spot, double at)
    {
        if (f.Mode != Mode.Control || !f.Grounded || InFight || _g is G.Sleep or G.Sport or G.Game || Match != null) return;
        if (first) f.Emote(V("photo! say cheese!", "PHOTO!!! everyone squeeze in!", "ugh, a photo.", "o-oh, a photo!", "a moment, captured"), 2);
        _cheesed = false;
        _photoAt = at;
        float left = (float)(at - World.Now) + 0.6f;
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (Vector2.Distance(spot, f.Base) > 60 * S && w.Env.Below(spot.X, spot.Y - 30 * S) is { } p && seg != null
            && (SameSegment(p, seg) || w.Nav.FindPath(seg, f.Base.X, p, spot.X, MyMover, MoveCost, 200) != null))
        {
            var a = Anchor.On(w.Env, p, spot.X);
            Navigate(() => a.Resolve(w.Env), 10 * S, true, () => Go(G.Pose, MathF.Max(1, (float)(_photoAt - World.Now) + 0.6f)), WalkPurpose.Social);
            return;
        }
        Go(G.Pose, left);
    }

    void DoPose(World w)
    {
        f.DesiredVX = 0;
        double toGo = _photoAt - World.Now;
        if (toGo < 1.1 && !_cheesed) { _cheesed = true; f.Emote(P.Aggression > 0.65f ? "…" : "cheese!", 1.6f); }
        f.SetAction(toGo > 1.6 ? Act.Stand : P.Playfulness > 0.6f ? Act.Cheer : P.Sociability > 0.5f ? Act.Wave : Act.HandsHips);
        f.LookAt = w.Cursor;
        if (_t > _dur) Go(G.Idle, 1);
    }

    public void PhotoTaken()
    {
        if (_g != G.Pose) { if (_g == G.Walk && _photoAt > 0) { _photoAt = 0; f.Emote(V("wait for me!", "WAIT!! I wasn't in it!", "…whatever.", "oh no, I missed it…", "next time"), 1.2f); } return; }
        _photoAt = 0;
        Write("photo", V("You took our picture!", "PHOTO!!! I hope I looked cool!", "You took a photo. I blinked. Probably.", "You took a photo of us. I was shy.", "A photo. A moment, kept."), "★", 300);
    }
}
