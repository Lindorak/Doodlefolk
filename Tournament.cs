using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>A sparring tournament: everyone willing gathers at an arena on the floor, one bout at a time (knock the
/// other down to win; a draw goes to whoever's less hurt), winners go through until there's a champion, who wears a
/// crown for the rest of the day. Everyone else watches and cheers.</summary>
sealed class Tournament
{
    public readonly List<Figure> Entrants = new();
    public List<Figure> Round = new(), Next = new();
    public Figure? A, B, Champion;
    public string Phase = "gather";
    public float T;
    public int Bouts, RoundSize;
    public Platform Floor = null!;
    public float ArenaX;
    public bool Over;
    public string Headline = "";
    int _restarts;

    public Vector2 Arena => new(ArenaX, Floor.Y);
    public bool InBout(Figure f) => (f == A || f == B) && Phase is "intro" or "fight";
    public Figure? Opponent(Figure f) => f == A ? B : f == B ? A : null;

    public string RoundName => RoundSize <= 2 ? "Final" : RoundSize <= 4 ? "Semi-final" : RoundSize <= 8 ? "Quarter-final" : "Round 1";

    /// <summary>Where each entrant stands: the two fighting face off in the middle, everyone else lines the sides.</summary>
    public float SlotX(Figure f)
    {
        float s = f.S;
        if (f == A && Phase is "intro" or "fight") return ArenaX - 75 * s;
        if (f == B && Phase is "intro" or "fight") return ArenaX + 75 * s;
        int i = Entrants.IndexOf(f);
        float side = i % 2 == 0 ? -1 : 1;
        float x = ArenaX + side * (230 + (i / 2) * 42) * s;
        return M.ClampIn(x, Floor.X1 + 10 * s, Floor.X2 - 10 * s);
    }

    public static Tournament? Start(World w, out string why)
    {
        why = "";
        if (!w.Fight.Enabled) { why = "Turn fights on (Settings) to hold a tournament"; return null; }
        var ents = w.Figures.Where(f => f.Mode == Mode.Control && !f.Dead && !f.Hunter && f.Brain.Match == null && f.Brain.Baby == false && !f.Brain.Asleep).ToList();
        if (ents.Count < 2) { why = "Need at least two figures who are up for it"; return null; }
        // The floor (taskbar) most of them are close to.
        var floors = w.Env.Platforms.Where(p => p.Solid && p.X2 - p.X1 > 600 * w.Scale).ToList();
        if (floors.Count == 0) { why = "No floor to fight on"; return null; }
        var floor = floors.OrderBy(p => ents.Sum(f => MathF.Abs(M.ClampIn(f.Base.X, p.X1, p.X2) - f.Base.X) + MathF.Abs(p.Y - f.Base.Y))).First();
        float cx = ents.Average(f => f.Base.X);
        var t = new Tournament { Floor = floor, ArenaX = M.ClampIn(cx, floor.X1 + 420 * w.Scale, floor.X2 - 420 * w.Scale) };
        t.Entrants.AddRange(ents.OrderBy(_ => w.Rng.Next()));
        t.Round.AddRange(t.Entrants);
        t.RoundSize = t.Round.Count;
        t.Headline = $"{t.Entrants.Count} fighters";
        return t;
    }

    public void Step(float dt, World w)
    {
        T += dt;
        Entrants.RemoveAll(f => !w.Figures.Contains(f) || f.Dead);
        Round.RemoveAll(f => !Entrants.Contains(f));
        Next.RemoveAll(f => !Entrants.Contains(f));
        if (A != null && !Entrants.Contains(A)) { if (B != null) Report(B, A, w); A = null; }
        if (B != null && !Entrants.Contains(B)) { if (A != null) Report(A, B, w); B = null; }
        switch (Phase)
        {
            case "gather":
                Headline = "Gathering…";
                if (T > 12 || Entrants.All(f => MathF.Abs(f.Base.X - SlotX(f)) < 120 * f.S && MathF.Abs(f.Base.Y - Floor.Y) < 10 * f.S)) NextBout(w);
                break;
            case "intro":
                Headline = T < 0.8f ? $"{A!.Name} vs {B!.Name}" : T < 1.6f ? "3" : T < 2.4f ? "2" : T < 3.2f ? "1" : "FIGHT!";
                if (T > 3.2f) { Phase = "fight"; T = 0; _restarts = 0; A!.Brain.TourneyFight(B!); B!.Brain.TourneyFight(A); World.Play(Sfx.Whistle, Arena, 0.4f); }
                break;
            case "fight":
                Headline = $"{A!.Name} vs {B!.Name}";
                // Nobody fighting any more (a truce, or they wandered off): go again, then judge on who's less hurt.
                bool fighting = A.Brain.InFight || B.Brain.InFight || A.Mode != Mode.Control || B.Mode != Mode.Control;
                if (!fighting && T > 2)
                {
                    if (_restarts++ < 1 && T < 40) { A.Brain.TourneyFight(B); B.Brain.TourneyFight(A); T = 1; }
                    else Report(A.HP >= B.HP ? A : B, A.HP >= B.HP ? B : A, w);
                }
                else if (T > 50) Report(A.HP >= B.HP ? A : B, A.HP >= B.HP ? B : A, w);
                break;
            case "result":
                if (T > 3) NextBout(w);
                break;
            case "done":
                if (Champion != null && w.Rng.NextDouble() < dt * 6) w.Fx.Spark(Champion.Jt[J.Head] + new Vector2(w.Rng.Range(-60, 60), w.Rng.Range(-80, 0)) * Champion.S, Champion.S * 1.2f, w.Rng, 1.2f, new[] { new Color4(1, 0.85f, 0.2f, 1), new Color4(0.3f, 0.8f, 1, 1), new Color4(1, 0.35f, 0.5f, 1) }[w.Rng.Next(3)]);
                if (T > 8) Over = true;
                break;
        }
    }

    public void Report(Figure winner, Figure loser, World w)
    {
        if (Phase != "fight" || !((winner == A && loser == B) || (winner == B && loser == A))) return;
        Next.Add(winner);
        Bouts++;
        Phase = "result";
        T = 0;
        Headline = $"{winner.Name} wins!";
        winner.Brain.TourneyResult(true, loser, RoundSize <= 2);
        loser.Brain.TourneyResult(false, winner, RoundSize <= 2);
        World.Play(Sfx.Chime, Arena, 0.4f);
        World.Log($"tournament: {winner.Name} beat {loser.Name}");
    }

    void NextBout(World w)
    {
        A = B = null;
        while (true)
        {
            if (Round.Count >= 2)
            {
                A = Round[0]; B = Round[1];
                Round.RemoveRange(0, 2);
                Phase = "intro";
                T = 0;
                return;
            }
            if (Round.Count == 1) { Next.Add(Round[0]); Round.Clear(); }   // a bye
            if (Next.Count <= 1)
            {
                Champion = Next.FirstOrDefault();
                Phase = "done";
                T = 0;
                Headline = Champion != null ? $"{Champion.Name} is the champion!" : "Nobody's left standing";
                if (Champion != null)
                {
                    World.Play(Sfx.TaDa, Champion.Jt[J.Head], 0.6f);
                    foreach (var f in Entrants) f.Brain.TourneyOver(Champion, w);
                    w.Sticker("champion");
                    w.News("tournament", $"{Champion.Name} wins the tournament!", 4, Champion);
                }
                return;
            }
            Round = Next;
            Next = new();
            RoundSize = Round.Count;
        }
    }
}

sealed partial class Brain
{
    /// <summary>Trophies won, and the day it last won (it wears the crown all that day).</summary>
    public int Trophies;
    public DateTime? ChampionOn;
    float _tourneyNavAt;

    public void TourneyFight(Figure o)
    {
        if (f.Mode != Mode.Control) return;
        BeginFight(o, true);
        _dur = 60;
        FaceTo(o.Base.X);
        f.Emote(V("let's go!", "BRING IT!!", "you're going down.", "g-good luck!", "may the best one win"), 1.1f);
    }

    void DoTourney(World w)
    {
        if (w.Tourney is not { Over: false } tn || !tn.Entrants.Contains(f)) { Go(G.Idle, 1); return; }
        if (tn.Phase == "fight" && tn.InBout(f) && tn.Opponent(f) is { } o) { if (!InFight && f.Grounded) TourneyFight(o); return; }
        float x = tn.SlotX(f);
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (seg == null) return;
        if (!SameSegment(seg, tn.Floor))
        {
            if (_t0 < _tourneyNavAt) return;
            _tourneyNavAt = _t0 + 4;
            var a = Anchor.On(w.Env, tn.Floor, x);
            Navigate(() => a.Resolve(w.Env), 20 * S, true, () => Go(G.Tourney, 600), WalkPurpose.Social);
            return;
        }
        _run = MathF.Abs(x - f.Base.X) > 200 * S;
        if (!MoveToward(x, 12 * S)) return;
        f.DesiredVX = 0;
        if (tn.InBout(f) && tn.Opponent(f) is { } foe) { FaceTo(foe.Base.X); f.SetAction(Act.Ready); f.LookAt = foe.Jt[J.Head]; return; }
        FaceTo(tn.ArenaX);
        // Watching: cheer for whoever it likes more.
        if (tn.Phase == "fight" && tn.A != null && tn.B != null)
        {
            var fav = AffinityWith(tn.A) >= AffinityWith(tn.B) ? tn.A : tn.B;
            f.LookAt = fav.Jt[J.Head];
            if (rng.NextDouble() < World.Dt * 0.35) f.Emote(V($"go {fav.Name}!", $"GO {fav.Name.ToUpperInvariant()}!!", "…", $"y-you can do it {fav.Name}!", $"{fav.Name}…"), 1);
            if (f.Action != Act.Cheer && rng.NextDouble() < World.Dt * 0.3) f.SetAction(Act.Cheer);
            else if (f.Action == Act.Cheer && f.ActionT > 1) f.SetAction(Act.Stand);
        }
        else if (tn.Phase == "done" && tn.Champion != f) { if (f.Action != Act.Cheer || f.ActionT > 1.2f) f.SetAction(Act.Cheer); }
        else f.SetAction(Act.Stand);
    }

    public void TourneyResult(bool won, Figure other, bool final)
    {
        if (won)
        {
            f.Emote(final ? V("I won!!", "CHAMPION!!!", "obviously.", "I… I won?!", "victory") : V("next!", "WHO'S NEXT?!", "too easy.", "I won! wow!", "onward"), 1.6f);
            Cheered(0.3f);
        }
        else
        {
            f.Emote(V("good fight!", "aww! good one!!", "…lucky.", "you're strong…", "well fought"), 1.5f);
            AddAffinity(other, P.Aggression > 0.7f ? -0.04f : 0.05f);
            Sadness = M.Clamp01(Sadness + (P.Aggression > 0.6f ? 0.15f : 0.06f));
        }
        Go(G.Tourney, 600);
    }

    public void TourneyOver(Figure champ, World w)
    {
        if (champ == f)
        {
            Trophies++;
            ChampionOn = DateTime.Now;
            f.HatOverride = "crown";
            f.HatColourOverride = "#FFC83D";
            Cheered(0.6f);
            Practice(SkillKind.Fighting, 0.08f);
            Write("champion", V($"I won the tournament! Trophy number {Trophies}.", $"CHAMPION!!! TROPHY #{Trophies}!!!", $"Won the tournament. Trophy {Trophies}. As expected.", $"I won the tournament?! I'm still shaking. Trophy {Trophies}.", $"The crown is mine. Trophy {Trophies}."), "★", 60);
            if (f.Mode == Mode.Control) Go(G.Victory, 3);
        }
        else
        {
            Write("tourney:" + champ.Name, V($"{champ.Name} won the tournament.", $"{champ.Name} won the TOURNAMENT!!", $"{champ.Name} won. This time.", $"{champ.Name} won the tournament. I cheered.", $"{champ.Name} took the crown."), "★", 600);
            AddAffinity(champ, 0.03f);
        }
    }
}
