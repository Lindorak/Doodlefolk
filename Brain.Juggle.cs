namespace Doodlefolk;

/// <summary>Juggling with their hands (Figure.Juggle.cs draws it): something to do on their own, a juggling dream to
/// practise for, and a talent-show act. Better jugglers drop less and go up to four or five; every catch is practice.
/// A fumble drops a beanbag at their feet: they bend and pick it up, then go again or give up, as they are.</summary>
sealed partial class Brain
{
    int _jugSeen, _jugRun, _jugBest, _jugDrops;
    float _jugPickT = -1;

    void JuggleOptions(World w, OptionList opts)
    {
        if (Engaged || InFight || !f.Grounded || f.Visitor != VisitorKind.None || Baby || Stamina < 0.3f || f.Riding != null) return;
        float like = f.Tastes.Of(Thing.Juggling);
        if (like < -0.3f) return;
        opts.Add((0.3f + like) * (0.35f + P.Playfulness * 0.6f) * (0.6f + Boredom) * 0.55f, () => StartHandJuggle(w), "Juggle");
    }

    /// <summary>How many they can keep up: three, then four, then five.</summary>
    int JuggleCount => Sk(SkillKind.Juggling) > 0.85f ? 5 : Sk(SkillKind.Juggling) > 0.6f ? 4 : 3;

    float CatchChance(int n)
    {
        float k = Sk(SkillKind.Juggling);
        return MathF.Min(0.996f, 0.9f + 0.09f * k - 0.03f * (n - 3) * (1 - k) - Stress * 0.04f);
    }

    public void StartHandJuggle(World w, float seconds = 0)
    {
        Go(G.Toss, seconds > 0 ? seconds : rng.Range(18, 45));
        _jugRun = 0; _jugDrops = 0; _jugPickT = -1;
        Begin(JuggleCount);
        f.Emote(V("juggling!", "WATCH THIS!!", "juggling.", "um… you can watch…", "round and round"), 1.2f);
    }

    void Begin(int n)
    {
        f.JugCatch = () => rng.NextDouble() < CatchChance(n);
        f.StartJuggling(n);
        _jugSeen = f.JugCaught;
    }

    void DoToss(World w)
    {
        f.DesiredVX = 0;
        int caught = f.JugCaught - _jugSeen;
        if (caught > 0)
        {
            _jugSeen = f.JugCaught;
            _jugRun += caught;
            Practice(SkillKind.Juggling, 0.0012f * caught);
            if (_jugRun is 10 or 25 or 50 or 100 or 200) f.Emote(_jugRun + "!", 0.9f);
            if (_jugRun > _jugBest) _jugBest = _jugRun;
        }
        if (f.Juggling > 0)
        {
            // Time's up: catch them all and take a bow.
            if (_t > _dur)
            {
                f.StopJuggling();
                if (_jugRun >= 15) { f.Emote(V("ta-da!", "TA-DAAA!!", "done.", "…ta-da?", "and… fin."), 1.2f); Go(G.Cheer, 1.2f); Cheered(0.1f); }
                else Go(G.Idle, 1);
            }
            return;
        }
        // Dropped one.
        if (f.JugDropped != null)
        {
            if (_jugPickT < 0)
            {
                _jugDrops++;
                _jugPickT = 0;
                f.Emote(f.Archetype == Archetype.Tsundere ? "I MEANT to do that!" : V("oops!", "ARGH!", "tch.", "s-sorry…", "gravity…"), 1.1f);
                if (_jugRun >= 20) Write("juggle:" + _jugRun, V($"Juggled {_jugRun} catches in a row!", $"{_jugRun} CATCHES!! JUGGLING LEGEND!", $"{_jugRun} catches. Fine.", $"I did {_jugRun} catches… nobody saw…", $"{_jugRun} catches, like a fountain"), "★", 600);
            }
            if (!f.JugDropDown) return;
            _jugPickT += World.Dt;
            if (_jugPickT < 0.35f) return;
            if (f.JugDropped is { } d)
            {
                FaceTo(d.X);
                f.KeepFacing = true;
                MoveToward(d.X - f.Facing * f.Torso * 0.8f, 2 * S);
                f.HoldN = d;
                f.HoldF = d + new System.Numerics.Vector2(-f.Facing * 3 * S, -S);
            }
            f.SetAction(Act.Scoop);
            if (_jugPickT < 1.1f) return;
            // Finish the pickup at the bag, rather than making it vanish beside an empty hand.
            if (f.JugDropped is { } bag && MathF.Min(System.Numerics.Vector2.Distance(f.Jt[J.HandN], bag), System.Numerics.Vector2.Distance(f.Jt[J.HandF], bag)) > 5 * S)
            {
                if (_jugPickT > 2.5f) Go(G.Idle, 1);
                return;
            }
            f.JugDropped = null;
            _jugPickT = -1;
            // Again? The gritty keep at it; the easily put off don't.
            bool again = _t < _dur && _jugDrops <= TriesFor("Juggle") + 1 && Stamina > 0.25f;
            if (again) { _jugRun = 0; Begin(JuggleCount); }
            else { if (_jugDrops >= 3) f.Emote(V("I'll get it one day.", "NEXT TIME!!", "enough.", "…maybe tomorrow.", "practice makes…"), 1.3f); Go(G.Idle, 1); }
            return;
        }
        Go(G.Idle, 0.5f);
    }
}
