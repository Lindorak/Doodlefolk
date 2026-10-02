using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

enum Sport { Soccer, Basketball, Tennis, Badminton }

/// <summary>A game in progress around some sports gear: who's playing on which side, the ball, the score, and
/// the referee (it decides when a goal, basket or point happens). Players' brains do the actual playing.</summary>
sealed class Match
{
    public readonly Sport Kind;
    public readonly List<Figure> Players = new();
    public readonly Dictionary<Figure, int> Team = new();
    public readonly Dictionary<Figure, int> Points = new();     // basketball: per player
    public readonly List<Item> Gear;
    public Prop Ball;
    public readonly int[] Score = new int[2];
    public float T, Pause;
    public bool Over;
    public int ServeTeam;
    public string? Shout;            // "GOAL!", "SWISH!", "POINT!" (shown for a moment)
    public float ShoutT;
    public Figure? Scorer;
    float _prevRimY = float.NaN;
    bool _ballWasDown;
    float _stuckT;

    public Match(Sport kind, List<Item> gear, Prop ball) { Kind = kind; Gear = gear; Ball = ball; }

    public int Target => Kind switch { Sport.Soccer => 3, Sport.Basketball => 9, _ => 5 };
    public bool OneGoal => Kind == Sport.Soccer && Gear.Count == 1;
    public static int MinPlayers(Sport s) => s is Sport.Tennis or Sport.Badminton ? 2 : 1;

    public string Name => Kind switch { Sport.Soccer => Prop.KindName(PropKind.SoccerBall) == "Football" ? "football" : "soccer", Sport.Basketball => "basketball", Sport.Tennis => "tennis", _ => "badminton" };

    public string ScoreText
    {
        get
        {
            if (Kind == Sport.Basketball) return string.Join(" · ", Players.OrderByDescending(p => Points.GetValueOrDefault(p)).Select(p => $"{p.Name} {Points.GetValueOrDefault(p)}"));
            if (OneGoal) return $"Goals {Score[0]}  ·  Saves {Score[1]}";
            return $"{Score[0]} – {Score[1]}";
        }
    }

    public Item Net => Gear[0];
    public float NetX => Gear[0].Pos.X;
    /// <summary>Tennis/badminton: which side of the net a team plays on (-1 left, +1 right).</summary>
    public int Side(int team) => team == 0 ? -1 : 1;

    /// <summary>Goal mouth point of goal g (world), at a height in object units.</summary>
    public Vector2 Mouth(Item g, float h) => g.Local(-18, h);
    /// <summary>Which way the field is from goal g (-1 or +1 in x).</summary>
    public float FieldDir(Item g) => g.Flip ? 1 : -1;
    public Vector2 RimCentre => Gear[0].Local(-4, 72);
    public float CourtFloor => Gear[0].Pos.Y;

    /// <summary>The surface the game is played on (the gear's window top or floor).</summary>
    public Platform? Court;
    float _outT;

    /// <summary>Where the ball goes back to after a goal: between the goals, or out in front of a single one.</summary>
    public Vector2 KickOff()
    {
        var k = KickOffRaw();
        if (Court != null) k.X = M.ClampIn(k.X, Court.X1 + 30 * Ball.S, Court.X2 - 30 * Ball.S);
        return k;
    }

    Vector2 KickOffRaw()
    {
        if (Kind == Sport.Soccer && Gear.Count == 2) return new Vector2((Gear[0].Pos.X + Gear[1].Pos.X) / 2, MathF.Min(Gear[0].Pos.Y, Gear[1].Pos.Y) - 60 * Ball.S);
        if (Kind == Sport.Soccer) return Mouth(Gear[0], 0) + new Vector2(FieldDir(Gear[0]) * 180 * Ball.S, -60 * Ball.S);
        if (Kind == Sport.Basketball) return RimCentre + new Vector2(FieldDir(Gear[0]) * 90 * Ball.S, 0);
        return new Vector2(NetX + Side(ServeTeam) * 110 * Ball.S, CourtFloor - 80 * Ball.S);
    }

    void Reset(World w, float pause)
    {
        Pause = pause;
        if (Ball.Holder is { } hd) { hd.Carrying = null; Ball.Holder = null; }
    }

    /// <summary>The referee. Returns false when the match is finished (or broken up).</summary>
    public bool Step(float dt, World w)
    {
        T += dt;
        ShoutT -= dt;
        Players.RemoveAll(p => !w.Figures.Contains(p) || p.Dead || p.Brain.Match != this);
        if (Over || Players.Count < MinPlayers(Kind) || !w.Props.Contains(Ball) || Gear.Any(g => !w.Items.Contains(g)) || T > 150) { End(w); return false; }
        if (Pause > 0)
        {
            Pause -= dt;
            if (Pause <= 0)
            {
                if (Ball.Holder is { } h) { h.Carrying = null; Ball.Holder = null; }
                Ball.Pinned = false;
                Ball.Pos = KickOff();
                Ball.Vel = default;
                Ball.OnGround = false;
            }
            return true;
        }
        var b = Ball;
        float s = b.S;
        var g0 = Gear[0];
        Court = w.Env.Platforms.FirstOrDefault(p => p.Hwnd == g0.GroundHwnd && g0.Pos.X >= p.X1 - 2 && g0.Pos.X <= p.X2 + 2 && MathF.Abs(p.Y - g0.Pos.Y) < 3);
        // Out of bounds: the ball has left the court (fallen off the window, say).
        bool outside = Court != null && b.Holder == null && !b.Pinned && (b.Pos.Y > Court.Y + 20 * s || ((b.OnGround || b.SinceBounce < 0.05f) && (b.Pos.X < Court.X1 || b.Pos.X > Court.X2)));
        _outT = outside ? _outT + dt : 0;
        if (_outT > 0.8f)
        {
            _outT = 0;
            if (Kind is Sport.Tennis or Sport.Badminton && b.LastTouch != null && Team.TryGetValue(b.LastTouch, out int hitter))
            {
                Score[1 - hitter]++;
                ServeTeam = 1 - hitter;
                Celebrate(1 - hitter, "OUT!", w);
            }
            else { Shout = "OUT!"; ShoutT = 1.2f; World.Play(Sfx.Whistle, b.Pos, 0.5f); }
            Reset(w, 1.2f);
            return true;
        }
        switch (Kind)
        {
            case Sport.Soccer:
                for (int g = 0; g < Gear.Count; g++)
                {
                    var goal = Gear[g];
                    float lx = (b.Pos.X - goal.Pos.X) / goal.Sc * (goal.Flip ? -1 : 1), ly = (goal.Pos.Y - b.Pos.Y) / goal.Sc;
                    if (lx > -15 && lx < 14 && ly > -2 && ly < 29)
                    {
                        int scoring = OneGoal ? 0 : 1 - g;      // the team defending goal g is team g
                        Score[scoring]++;
                        Scorer = b.LastTouch;
                        Celebrate(scoring, "GOAL!", w);
                        Reset(w, 2.2f);
                        break;
                    }
                }
                // One-goal shootout: a ball that ends up stopped far from the goal is a save.
                if (OneGoal && Pause <= 0)
                {
                    _stuckT = b.OnGround && b.Vel.LengthSquared() < 30 * 30 * s * s && b.LastTouch != null && Team.GetValueOrDefault(b.LastTouch!) == 1 ? _stuckT + dt : 0;
                    if (_stuckT > 0.5f) { Score[1]++; Celebrate(1, "SAVE!", w); Reset(w, 1.5f); _stuckT = 0; }
                }
                break;
            case Sport.Basketball:
            {
                var hoop = Gear[0];
                float lx = (b.Pos.X - hoop.Pos.X) / hoop.Sc * (hoop.Flip ? -1 : 1), ly = (hoop.Pos.Y - b.Pos.Y) / hoop.Sc;
                if (!float.IsNaN(_prevRimY) && _prevRimY > 72 && ly <= 72 && lx > -13.5f && lx < 5.5f && b.Vel.Y > 0 && b.Holder == null)
                {
                    var who = b.LastTouch;
                    if (who != null && Points.ContainsKey(who))
                    {
                        float dist = MathF.Abs(who.Base.X - RimCentre.X);
                        int pts = dist > 200 * s ? 3 : 2;
                        Points[who] += pts;
                        Scorer = who;
                        Shout = pts == 3 ? "THREE!" : b.SinceBounce > 1 ? "SWISH!" : "SCORE!";
                        World.Play(Sfx.Swish, b.Pos, 0.6f); World.Play(Sfx.TaDa, b.Pos, 0.4f);
                        ShoutT = 1.5f;
                        who.Brain.OnScored(this, w);
                        if (Points[who] >= Target) { Over = true; }
                    }
                }
                _prevRimY = ly;
                break;
            }
            default:
            {
                // A rally point when the ball lands on a side (or dies in the net).
                bool down = b.OnGround || (b.SinceBounce < 0.02f && b.Pos.Y > CourtFloor - 30 * s);
                if (down && !_ballWasDown && b.Holder == null && !b.Pinned)
                {
                    int sideTeam = b.Pos.X < NetX ? 0 : 1;
                    int winner = 1 - sideTeam;
                    Score[winner]++;
                    ServeTeam = winner;
                    Celebrate(winner, "POINT!", w);
                    Reset(w, 1.6f);
                }
                _ballWasDown = down;
                break;
            }
        }
        if (Kind != Sport.Basketball && Score.Any(x => x >= Target)) Over = true;
        return true;
    }

    void Celebrate(int team, string shout, World w)
    {
        World.Play(shout == "OUT!" ? Sfx.Whistle : Sfx.TaDa, Ball.Pos, 0.6f);
        Shout = shout;
        ShoutT = 1.6f;
        foreach (var p in Players) p.Brain.OnMatchMoment(this, Team.GetValueOrDefault(p) == team, w);
        w.Fx.Spark(Ball.Pos, Ball.S * 1.4f, w.Rng, 1.4f, new Color4(1, 0.9f, 0.3f, 1));
    }

    public void End(World w)
    {
        if (Players.Count == 0) { Over = true; return; }
        int best = Kind == Sport.Basketball ? -1 : Score[0] == Score[1] ? -1 : Score[0] > Score[1] ? 0 : 1;
        Figure? top = Kind == Sport.Basketball ? Players.OrderByDescending(p => Points.GetValueOrDefault(p)).FirstOrDefault() : null;
        foreach (var p in Players.ToArray())
        {
            bool won = Kind == Sport.Basketball ? p == top : best >= 0 && Team.GetValueOrDefault(p) == best;
            p.Brain.OnMatchOver(this, won, best < 0 && top == null, w);
        }
        Over = true;
    }

    public void Draw(Renderer r)
    {
        if (Players.Count == 0) return;
        var g = Gear[0];
        float s = g.Sc;
        Vector2 at = Kind switch
        {
            Sport.Soccer when Gear.Count == 2 => new Vector2((Gear[0].Pos.X + Gear[1].Pos.X) / 2, MathF.Min(Gear[0].Pos.Y, Gear[1].Pos.Y) - 70 * s),
            Sport.Basketball => g.Local(0, 104),
            _ => g.Local(0, Kind == Sport.Soccer ? 46 : 44),
        };
        Ui.Card(r, at, (Kind == Sport.Basketball ? 28 + Players.Count * 28 : 54) * s, 14 * s, 4 * s, s, 0.95f);
        r.Text(ScoreText, at + new Vector2(0, -0.3f * s), 8 * s, Ui.Ink, true);
        if (ShoutT > 0 && Shout != null)
            r.Text(Shout, at + new Vector2(0, -16 * s - (1.6f - ShoutT) * 8 * s), 12 * s, Ui.Accent.A(M.Clamp01(ShoutT * 1.5f)), true);
    }

    public System.Drawing.RectangleF Bounds()
    {
        var g = Gear[0];
        float s = g.Sc;
        Vector2 at = Kind switch
        {
            Sport.Soccer when Gear.Count == 2 => new Vector2((Gear[0].Pos.X + Gear[1].Pos.X) / 2, MathF.Min(Gear[0].Pos.Y, Gear[1].Pos.Y) - 70 * s),
            Sport.Basketball => g.Local(0, 104),
            _ => g.Local(0, Kind == Sport.Soccer ? 46 : 44),
        };
        float w = (Kind == Sport.Basketball ? 40 + Players.Count * 30 : 80) * s;
        return System.Drawing.RectangleF.FromLTRB(at.X - w, at.Y - 40 * s, at.X + w, at.Y + 12 * s);
    }
}
