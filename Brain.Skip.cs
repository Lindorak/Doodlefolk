using System.Numerics;

namespace Doodlefolk;

/// <summary>Skipping rope. Solo: the rope loops round them and they hop as it passes under their feet; beginners turn
/// it slowly and trip, practice makes it faster and fancier (alternating feet, crossed arms, double-unders: two turns a
/// jump). Long rope: two friends turn, one jumps in; and Double Dutch, two ropes turning in turn, for the skilled. Every
/// streak is counted: a personal best, and the town record to beat.</summary>
sealed partial class Brain
{
    public int SkipBest;
    Item? _rope;
    Figure? _turnA, _turnB, _jumper;
    bool _skipTurner, _doubleDutch;
    float _skipT, _skipNextHop;
    int _skipStreak, _skipMisses, _skipLap;
    bool _skipDouble;

    /// <summary>A rope to skip with (or a long rope and two friends to turn it).</summary>
    void SkipOptions(World w, OptionList opts)
    {
        if (Stamina < 0.35f || Baby) return;
        var rope = w.Items.Where(i => i.Def.Key is "jumprope" or "longrope" && i.Free && i.OnGround && !Unreachable(i) && Vector2.Distance(i.Pos, f.Base) < 1500 * S)
                          .OrderBy(i => Vector2.Distance(i.Pos, f.Base)).FirstOrDefault();
        if (rope == null) return;
        float keen = (0.25f + P.Energy * 0.4f + P.Playfulness * 0.3f) * (0.6f + Sk(SkillKind.Skipping)) * Taste(Thing.Tricks);
        if (rope.Def.Key == "jumprope") opts.Add(keen, () => GoSkip(rope, w), "Skip rope");
        else if (w.Figures.Count(o => o != f && o.Mode == Mode.Control && !o.Brain.Engaged && AffinityWith(o) > 0) >= 2)
            opts.Add(keen * (0.6f + P.Sociability * 0.6f), () => GoLongRope(rope, w), "Play long rope");
    }

    void GoSkip(Item rope, World w)
    {
        _navAbout = rope;
        Navigate(() => w.Items.Contains(rope) && rope.Free ? rope.Pos : null, 6 * S, false, () =>
        {
            if (!w.Items.Contains(rope) || !rope.Free) { Go(G.Idle, 1); return; }
            _rope = rope; rope.Holder = f;
            _turnA = _turnB = _jumper = null; _skipTurner = false; _doubleDutch = false;
            StartSkipping(w);
        }, WalkPurpose.Other);
    }

    void StartSkipping(World w)
    {
        _skipT = 0; _skipStreak = 0; _skipMisses = 0; _skipLap = 0; _skipDouble = false;
        f.SkipPhase = MathF.PI * 0.05f;
        f.SkipRopes = _doubleDutch ? 2 : 1;
        _skipNextHop = 0;
        Go(G.Skip, rng.Range(20, 45));
        f.Emote(V("skip skip!", "WATCH THIS!", "skipping. fine.", "here goes…", "and… begin"), 1.2f);
    }

    /// <summary>Turns per second: beginners slowly, the practised briskly.</summary>
    float TurnRate => 1.0f + Sk(SkillKind.Skipping) * 1.1f;

    void DoSkip(World w)
    {
        var rope = _rope;
        if (rope == null || !w.Items.Contains(rope) || (rope.Holder != f && _turnA == null)) { EndSkip(w); return; }
        f.DesiredVX = 0;
        _skipT += World.Dt;
        if (_skipTurner) { DoTurn(w); return; }
        bool group = _turnA != null;
        if (group && (_turnA!.Brain._g != G.Skip || _turnB!.Brain._g != G.Skip)) { EndSkip(w); return; }
        if (group && !JumpInPosition(w)) return;
        if (group) f.DesiredVX = 0;
        // The rope's angle: ours when solo, the turners' when jumping in.
        float phase = group ? _turnA!.SkipPhase : f.SkipPhase;
        if (!group)
        {
            float rate = TurnRate * (_skipDouble ? 2 : 1);
            float before = f.SkipPhase;
            f.SkipPhase = (f.SkipPhase + World.Dt * rate * MathF.Tau) % MathF.Tau;
            phase = f.SkipPhase;
            // Crossed arms now and then, for the practised.
            f.SkipCross = Sk(SkillKind.Skipping) > 0.55f && (_skipLap / 4) % 3 == 2;
            if (before < MathF.PI && phase >= MathF.PI) RopePasses(w, under: true);
            if (phase < before) { _skipLap++; _skipDouble = Sk(SkillKind.Skipping) > 0.7f && _skipLap % 5 == 4 && !_skipDouble; }
        }
        else
        {
            // Two ropes (Double Dutch): the second is half a turn behind.
            float ph = _turnA!.SkipPhase, prev = _jumpPrevPhase;
            _jumpPrevPhase = ph;
            if (prev < MathF.PI && ph >= MathF.PI) RopePasses(w, under: true);
            if (_doubleDutch && prev >= 0 && (prev + MathF.PI) % MathF.Tau < MathF.PI && (ph + MathF.PI) % MathF.Tau >= MathF.PI) RopePasses(w, under: true);
        }
        // Hop just before each rope comes round under the feet.
        float toBottom = (MathF.PI - phase + MathF.Tau) % MathF.Tau;
        float lead = 0.22f * MathF.Tau * (group ? TurnRate * 0.85f : TurnRate);
        if (f.Grounded && !f.JumpPending && _t0 - _hopAt > 0.2f && toBottom < lead && toBottom > lead * 0.4f)
        {
            f.RequestJump(new Vector2(0, -MathF.Sqrt(2 * f.Gravity * f.Leg * (_skipDouble ? 0.55f : 0.32f))), 0.01f);
            _hopAt = _t0;
        }
        if (_t > _dur || _skipMisses >= 3 || Stamina < 0.12f) EndSkip(w);
        if (World.StaminaOn) Stamina = MathF.Max(0, Stamina - World.Dt * 0.006f);
    }

    float _jumpPrevPhase = -1, _hopAt = -9;

    /// <summary>The rope comes round under them: cleared, or tripped over.</summary>
    void RopePasses(World w, bool under)
    {
        float skill = Sk(SkillKind.Skipping);
        float miss = 0.13f * MathF.Pow(1 - skill, 1.6f) + (1 - Stamina) * 0.05f + (_doubleDutch ? 0.06f : 0) + (_skipDouble ? 0.08f : 0);
        // Hopped in time (the rope passes in the moment they're up).
        bool airborne = !f.Grounded || f.JumpPending || _t0 - _hopAt < 0.3f;
        if (!airborne || rng.NextDouble() < miss)
        {
            // Caught on the feet.
            _skipMisses++;
            Flash(Manpu.SweatDrop, 1.6f);
            f.Emote(V("oops!", "ARGH! AGAIN!", "tch.", "eep…", "the rope wins"), 1);
            if (_skipStreak >= 5) f.Emote($"{_skipStreak}!", 1.2f);
            NoteStreak(w);
            _skipStreak = 0;
            if (_turnA == null) f.SkipPhase = MathF.PI * 0.05f;
            return;
        }
        _skipStreak++;
        Practice(SkillKind.Skipping, 0.004f);
        if (_skipStreak % 10 == 0) f.Emote(_skipStreak.ToString(), 0.8f);
        if (_skipDouble && _turnA == null) f.Emote("double!", 0.7f);
    }

    void NoteStreak(World w)
    {
        if (_skipStreak <= SkipBest) return;
        bool first = SkipBest == 0;
        SkipBest = _skipStreak;
        if (_skipStreak >= 8 && !first) { Flash(Manpu.Sparkles, 2); Write("skip:best", V($"New skipping best: {SkipBest} in a row!", $"{SkipBest} SKIPS IN A ROW!!! NEW BEST!", $"{SkipBest} skips. Personal best. Whatever.", $"I did {SkipBest} skips in a row…!", $"{SkipBest} turns of the rope, unbroken."), "★", 300); }
        if (_skipStreak > w.SkipRecord)
        {
            bool had = w.SkipRecord > 0;
            w.SkipRecord = _skipStreak; w.SkipRecordBy = f.Name;
            if (had && _skipStreak >= 10) { w.News("record", $"{f.Name} set a new skipping record: {_skipStreak} in a row", 2); f.Emote(V("TOWN RECORD!", "RECORD!!! WOOO!", "Record. Obviously.", "a… record?!", "a record"), 1.6f); }
        }
    }

    void EndSkip(World w)
    {
        NoteStreak(w);
        if (_skipStreak + SkipBest > 0 && _turnA == null && !_skipTurner) Write("skip", V($"Skipped rope. Best today: {SkipBest}.", "Skipping!!! My legs are jelly!", "Skipped. Fine.", "I skipped rope for a while…"), "♪", 600);
        Cheered(0.1f);
        LeaveSkip();
        Go(G.Idle, 1.2f);
    }

    bool _leavingSkip;
    public bool Turning => _skipTurner && _g == G.Skip;

    void LeaveSkip()
    {
        if (_leavingSkip) return;
        _leavingSkip = true;
        var others = new[] { _turnA, _turnB, _jumper };
        _turnA = _turnB = _jumper = null;
        if (_rope is { } rope && rope.Holder == f) { rope.Holder = null; rope.Vel = Vector2.Zero; rope.OnGround = false; }
        f.SkipPhase = -1; f.SkipCross = false; f.RopeMate = null; f.SkipRopes = 1;
        // Tell the others in a long rope game it's over.
        _rope = null; _skipTurner = false; _jumpPrevPhase = -1;
        foreach (var o in others)
            if (o != null && o != f && o.Brain._g == G.Skip && !o.Brain._leavingSkip) o.Brain.Go(G.Idle, 1);
        _leavingSkip = false;
    }

    // ---------------- long rope and Double Dutch ----------------

    /// <summary>Find two friends, hand them the ends, and jump in (or turn, and let a friend jump).</summary>
    void GoLongRope(Item rope, World w)
    {
        var mates = w.Figures.Where(o => o != f && o.Mode == Mode.Control && o.Grounded && !o.Brain.Engaged && !o.Brain.InFight && !o.Brain.Asleep
                                         && o.Brain._g is G.Idle or G.Watch or G.SitFloor or G.Walk && AffinityWith(o) > 0
                                         && MathF.Abs(o.Base.Y - rope.Pos.Y) < 6 * S && MathF.Abs(o.Base.X - rope.Pos.X) < 900 * S)
                             .OrderByDescending(o => AffinityWith(o)).Take(2).ToList();
        if (mates.Count < 2 || !mates.All(o => o.Brain.AgreeToHelp(f, rope, w))) { f.Emote(V("nobody to turn…", "WHO WANTS TO TURN?!", "never mind.", "maybe later…"), 1.4f); Go(G.Idle, 1); return; }
        // The best skipper jumps; the other two turn.
        var all = new List<Figure> { f, mates[0], mates[1] }.OrderByDescending(o => o.Brain.Sk(SkillKind.Skipping)).ToList();
        var jumper = all[0];
        float mid = rope.Pos.X, span = 62 * S;
        var a = all[1]; var b = all[2];
        bool dutch = jumper.Brain.Sk(SkillKind.Skipping) > 0.5f;
        rope.Holder = a;
        foreach (var o in all) { o.Brain._rope = rope; o.Brain._turnA = a; o.Brain._turnB = b; o.Brain._jumper = jumper; o.Brain._doubleDutch = dutch; }
        a.Brain.BeginTurn(mid - span, b, true, w);
        b.Brain.BeginTurn(mid + span, a, false, w);
        jumper.Brain.BeginJumpIn(mid, w);
        f.Emote(dutch ? V("Double Dutch!", "DOUBLE DUTCH!!!", "Two ropes. Keep up.", "t-two ropes?!") : V("long rope!", "JUMP IN!!", "turn it.", "let's play!"), 1.6f);
    }

    float _turnX;
    bool _turnLead;

    void BeginTurn(float x, Figure other, bool lead, World w)
    {
        _skipTurner = true; _turnX = x; _turnLead = lead;
        f.RopeMate = lead ? other : null;
        f.SkipPhase = lead ? 0 : -1;
        f.SkipRopes = _doubleDutch ? 2 : 1;
        _skipT = 0;
        Go(G.Skip, 40);
    }

    void BeginJumpIn(float x, World w)
    {
        _skipTurner = false; _turnX = x; _skipStreak = 0; _skipMisses = 0; _jumpPrevPhase = -1;
        f.SkipPhase = -1;
        Go(G.Skip, 40);
    }

    void DoTurn(World w)
    {
        if (_jumper == null || !w.Figures.Contains(_jumper) || _jumper.Brain._g != G.Skip) { EndSkip(w); return; }
        // Walk to our end, face the other turner, then turn steadily (at the jumper's pace).
        var other = _turnLead ? _turnB : _turnA;
        if (other == null || !w.Figures.Contains(other)) { EndSkip(w); return; }
        if (!MoveToward(_turnX, 4 * S)) return;
        FaceTo(other.Base.X);
        f.KeepFacing = true;
        if (_turnLead)
        {
            bool ready = MathF.Abs(other.Base.X - other.Brain._turnX) < 8 * S && MathF.Abs(_jumper.Base.X - _jumper.Brain._turnX) < 10 * S;
            if (ready) f.SkipPhase = (f.SkipPhase + World.Dt * _jumper.Brain.TurnRate * 0.85f * MathF.Tau) % MathF.Tau;
            if (_t > _dur) EndSkip(w);
        }
        else f.SkipPhase = _turnA?.SkipPhase ?? 0;
        if (rng.NextDouble() < World.Dt * 0.15) f.Emote(V("♪ cinderella… ♪", "FASTER!", "keep going.", "one, two…", "turn, turn"), 1);
    }

    /// <summary>The jumper: into the middle (when the rope's up), then hop each time it comes round.</summary>
    bool JumpInPosition(World w) => MoveToward(_turnX, 4 * S);
}
