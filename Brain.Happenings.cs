using System.Numerics;

namespace Doodlefolk;

/// <summary>Taking part in a town event: dancing and chatting at the festival; performing (or watching) at the talent
/// show; racing (or cheering at the finish line) on race day.</summary>
sealed partial class Brain
{
    public string HapRole = "";
    float _hapX, _hapNext, _stumbleT, _pace = 1;
    int _hapRejoins;
    string _hapAct = "";

    public void JoinHappening(string role, World w)
    {
        if (f.Riding != null) Dismount();
        HapRole = role;
        _hapRejoins = 0;
        _hapNext = 0;
        var h = w.Happening;
        _hapX = h != null ? rng.Range(h.Left, h.Right) : f.Base.X;
        _pace = 0.95f + P.Energy * 0.15f + rng.Range(-0.06f, 0.06f);
        if (role == "act") { _stageSong = w.SongFor?.Invoke(f); _songLine = 0; _hapAct = _stageSong != null ? "song" : BestAct(); }
        Go(G.Happening, 1e6f);
        f.Emote(role switch
        {
            "racer" => V("race day!", "I'M GONNA WIN!!!", "fine. I'll race.", "um, I'll try…", "to the starting line"),
            "act" => V("my big moment!", "SHOWTIME!!!", "fine. I'll perform.", "I'm so nervous…", "the stage calls"),
            "reveller" => V("festival!", "PARTY!!!", "a festival. okay.", "ooh, lights…", "the town celebrates"),
            _ => V("ooh!", "LET'S GOOO!", "fine.", "oh, fun…", "let us watch"),
        }, 1.4f);
    }

    public void LeaveHappening()
    {
        HapRole = "";
        if (_g == G.Happening) Go(G.Idle, 1);
    }

    /// <summary>The app checks in now and then: back to the event if something pulled it away.</summary>
    public void KeepInHappening(World w)
    {
        if (HapRole.Length == 0 || f.Mode != Mode.Control) return;
        if (_g is G.Happening or G.Walk) return;
        if (++_hapRejoins > 8) { HapRole = ""; return; }
        Go(G.Happening, 1e6f);
    }

    string BestAct()
    {
        var skills = new (string act, float v)[]
        {
            ("dance", Sk(SkillKind.Dancing) + f.Tastes.Of(Thing.Dancing) * 0.3f),
            ("juggle", Sk(SkillKind.Juggling) + f.Tastes.Of(Thing.Juggling) * 0.3f),
            ("acrobatics", Sk(SkillKind.Climbing) + P.Energy * 0.3f),
            ("comedy", P.Playfulness * 0.6f + P.Sociability * 0.3f),
            ("song", P.Sociability * 0.3f + Sk(SkillKind.Singing) * 0.8f),
        };
        return skills.OrderByDescending(s => s.v + rng.NextDouble() * 0.2).First().act;
    }

    /// <summary>Walk (or navigate, if it's somewhere else) to x on the event's floor; true once there.</summary>
    bool HapGoTo(World w, Happening h, float x, float within, bool run = false)
    {
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        // On the event's floor already: go as near as this stretch of it allows (a spot past its end isn't worth
        // walking off for, and trying again and again looks like dithering).
        if (f.Grounded && seg != null && MathF.Abs(seg.Y - h.Y) < 3 && seg.X2 - seg.X1 > 20 * S) x = M.ClampIn(x, seg.X1 + 6 * S, seg.X2 - 6 * S);
        if (f.Grounded && seg != null && MathF.Abs(seg.Y - h.Y) < 3 && x >= seg.X1 && x <= seg.X2)
        {
            _run = run;
            return MoveToward(x, within);
        }
        if (!f.Grounded) return false;
        // Getting back to the event from somewhere else: give a route a few seconds before trying another.
        if (_t0 < _hapNavRetry) { f.DesiredVX = 0; return false; }
        _hapNavRetry = _t0 + 4;
        Navigate(() => new Vector2(x, h.Y), within, run, () => Go(G.Happening, 1e6f), WalkPurpose.Other);
        return false;
    }

    float _hapNavRetry;

    void DoHappening(World w)
    {
        var h = w.Happening;
        if (h == null || HapRole.Length == 0) { LeaveHappening(); return; }
        switch (HapRole)
        {
            case "reveller": Revel(w, h); break;
            case "act": Perform(w, h); break;
            case "audience": Audience(w, h); break;
            case "racer": Race(w, h); break;
            case "fan": Fan(w, h); break;
        }
    }

    void Revel(World w, Happening h)
    {
        if (!HapGoTo(w, h, _hapX, 10 * S)) return;
        if (_t < _hapNext)
        {
            if (f.Action == Act.Fidget || f.Action == Act.Talk) return;
            if (_hapAct == "dance" && (f.Action != Act.Fidget || f.ActionT >= f.FidgetDur)) f.StartFidget(Fidget.Groove);
            return;
        }
        _hapNext = _t + rng.Range(5, 11);
        double roll = rng.NextDouble();
        var friend = w.Figures.Where(o => o != f && o.Brain.HapRole == "reveller" && Vector2.Distance(o.Base, f.Base) < 250 * S).OrderBy(_ => rng.Next()).FirstOrDefault();
        if (roll < 0.45) { _hapAct = "dance"; f.StartFidget(Fidget.Groove); if (rng.NextDouble() < 0.4) f.Emote("♪", 1); }
        else if (roll < 0.7 && friend != null)
        {
            _hapAct = "chat"; FaceTo(friend.Base.X); f.SetAction(Act.Talk);
            f.Emote(V("love the lights!", "BEST FESTIVAL EVER!!", "it's… fine.", "isn't it pretty?", "the night sparkles"), 1.6f);
            AddAffinity(friend, 0.02f);
        }
        else if (roll < 0.85) { _hapAct = "cheer"; f.SetAction(Act.Cheer); Cheered(0.05f); }
        else { _hapAct = ""; _hapX = rng.Range(h.Left, h.Right); }
        Cheered(0.03f); Boredom = MathF.Max(0, Boredom - 0.05f); Loneliness = MathF.Max(0, Loneliness - 0.05f);
    }

    void Perform(World w, Happening h)
    {
        var stage = h.Stage;
        if (stage == null) return;
        int idx = h.Who.IndexOf(f);
        bool myTurn = h.Phase == 1 && h.Turn == idx;
        float wing = stage.Pos.X - stage.Def.W * stage.Sc * 0.5f - (24 + idx * 16) * S;
        if (!myTurn)
        {
            if (!HapGoTo(w, h, wing, 8 * S)) return;
            FaceTo(stage.Pos.X);
            f.SetAction(h.Phase == 2 && h.Winner != f && h.PhaseT < 1 ? Act.Cheer : Act.Stand);
            return;
        }
        // On stage: up the steps (navigation does the hop), then perform.
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (seg == null || seg.Item != stage)
        {
            if (f.Grounded) Navigate(() => w.Items.Contains(stage) ? StageSpot(stage, w) : null, 8 * S, false, () => Go(G.Happening, 1e6f), WalkPurpose.Other);
            return;
        }
        MoveToward(stage.Pos.X, 6 * S);
        if (h.PhaseT < 1.2f) { FaceTo(stage.Pos.X + 300); f.SetAction(Act.Wave); return; }
        switch (_hapAct)
        {
            case "dance": if (f.Action != Act.Fidget || f.ActionT >= f.FidgetDur) f.StartFidget(Fidget.Groove); break;
            case "acrobatics": if (f.Grounded && rng.NextDouble() < World.Dt * 0.7) f.RequestFlip(60 * S); break;
            case "comedy":
                f.SetAction(Act.Talk);
                if (rng.NextDouble() < World.Dt * 0.35) f.Emote(new[] { "why did the chicken…", "knock knock!", "so a pencil walks in…", "ba-dum tss!", "I'm on a roll!", "ha… ha?" }[rng.Next(6)], 1.8f);
                break;
            case "song" when _stageSong != null:
                if (_song == null && _songLine == 0) StartSinging(_stageSong);
                SingStep(w);
                if (_song == null) _stageSong = null;   // sung; just bow from here
                break;
            case "song":
                f.SetAction(_t % 2 < 1.3f ? Act.Cheer : Act.Stand);
                if (rng.NextDouble() < World.Dt * 0.6) f.Emote(rng.NextDouble() < 0.5 ? "♪ la la la ♪" : "♫", 1.2f);
                break;
            default:
                f.SetAction(Act.Tap);
                if (rng.NextDouble() < World.Dt * 0.3) f.Emote("juggle juggle!", 1);
                break;
        }
    }

    void Audience(World w, Happening h)
    {
        var stage = h.Stage;
        if (stage == null) return;
        int idx = h.Crowd.IndexOf(f);
        float x = stage.Pos.X + stage.Def.W * stage.Sc * 0.5f + (30 + idx * 22) * S;
        if (!HapGoTo(w, h, x, 8 * S)) return;
        FaceTo(stage.Pos.X);
        if (h.Phase == 2) { f.SetAction(h.PhaseT < 3 ? Act.Cheer : Act.SitFloor); return; }
        f.SetAction(Act.SitFloor);
        if (h.Phase == 1 && rng.NextDouble() < World.Dt * 0.08) f.Emote(rng.NextDouble() < 0.5 ? "*clap*" : V("woo!", "WOOO!!!", "hm.", "ooh…", "how fine"), 1.1f);
    }

    void Race(World w, Happening h)
    {
        float dir = MathF.Sign(h.FinishX - h.StartX);
        int lane = h.Who.IndexOf(f);
        switch (h.Phase)
        {
            case 0:
                if (!HapGoTo(w, h, h.StartX - dir * lane * 7 * S, 4 * S)) return;
                FaceTo(h.FinishX);
                f.SetAction(Act.Ready);
                break;
            case 1:
                f.DesiredVX = 0; FaceTo(h.FinishX); f.SetAction(Act.Ready);
                break;
            case 2:
                if (h.Finished.Contains(f))
                {
                    f.DesiredVX = 0;
                    f.SetAction(h.Winner == f ? Act.Cheer : Act.HandsHips);
                    return;
                }
                if (_stumbleT > 0) { _stumbleT -= World.Dt; f.DesiredVX = 0; return; }
                if (rng.NextDouble() < World.Dt * 0.02 * (1.2f - P.Bravery)) { _stumbleT = 0.7f; f.Emote(V("oof!", "WHOA!", "ugh.", "eep!", "a stumble"), 0.8f); return; }
                FaceTo(h.FinishX);
                float tired = World.StaminaOn ? 0.75f + Stamina * 0.25f : 1;
                f.DesiredVX = dir * f.RunSpeed * _pace * tired * (1 + MathF.Sin(_t * 1.3f + f.Id) * 0.04f);
                f.SetAction(Act.Stand);
                if (World.StaminaOn) Stamina = MathF.Max(0, Stamina - World.Dt * 0.012f);
                break;
            default:
                f.DesiredVX = 0;
                f.SetAction(h.Winner == f ? Act.Cheer : Act.Stand);
                break;
        }
    }

    void Fan(World w, Happening h)
    {
        float dir = MathF.Sign(h.FinishX - h.StartX);
        int idx = h.Crowd.IndexOf(f);
        float x = h.FinishX + dir * (40 + idx * 18) * S;
        if (!HapGoTo(w, h, x, 8 * S)) return;
        var lead = h.Who.Where(o => !h.Finished.Contains(o)).OrderByDescending(o => (o.Base.X - h.StartX) * dir).FirstOrDefault();
        FaceTo(lead?.Base.X ?? h.StartX);
        f.SetAction(h.Phase == 2 && _t % 3 < 1.2f ? Act.Cheer : Act.Stand);
        if (h.Phase == 2 && lead != null && rng.NextDouble() < World.Dt * 0.2) f.Emote(AffinityWith(lead) > 0.3f ? $"go {lead.Name}!" : "go go go!", 1.1f);
    }

    public void WonHappening(Happening h, World w)
    {
        Trophies++;
        Cheered(0.6f);
        f.Emote(V("I won!!", "I WON!!! I WON!!!", "obviously.", "I… I won?!", "victory"), 2.2f);
        string what = h.Kind == "talent" ? "the talent show" : "the race";
        Write("won:" + h.Kind, V($"I won {what}!", $"I WON {what.ToUpperInvariant()}!!! TROPHY!", $"Won {what}. As expected.", $"I won {what}… I can't believe it!", $"Victory at {what}."), "★", 0);
    }

    /// <summary>A diary line for having been there.</summary>
    public void Remember(string kind, World w)
    {
        switch (kind)
        {
            case "festival": Write("event:festival", V("The town festival! Lights, music, dancing.", "FESTIVAL!!! I danced ALL NIGHT!", "There was a festival. Lots of noise.", "The festival was so pretty… I danced a little.", "Lanterns glowed and the town sang."), "♪", 0); break;
            case "talent": Write("event:talent", V($"Went to the talent show. {World.Current?.Happening?.Winner?.Name ?? "Someone"} won.", "TALENT SHOW!! So many acts!", "Talent show. Some talent.", "The talent show was lovely…", "Performers took the stage tonight."), "♪", 0); break;
            case "race": Write("event:race", V($"Race day! {World.Current?.Happening?.Winner?.Name ?? "Someone"} won.", "RACE DAY!!! SO FAST!", "Race. Didn't win. Whatever.", "I took part in race day… I tried my best.", "Feet drummed the ground on race day."), "★", 0); break;
        }
    }
}
