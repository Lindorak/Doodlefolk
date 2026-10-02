using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

sealed partial class World
{
    public const float Dt = 1 / 120f;
    /// <summary>The one world (brains use it to look up the colour rules).</summary>
    public static World Current { get; private set; } = null!;

    public readonly Env Env = new();
    public readonly List<Figure> Figures = new();
    public readonly List<Prop> Props = new();
    public readonly List<Item> Items = new();
    public readonly List<Projectile> Projectiles = new();
    public readonly List<Match> Matches = new();
    /// <summary>Hooks for the brain to bring things into the world (a ball for a game, rackets).</summary>
    public Func<PropKind, Prop>? MakeProp;
    public Func<string, Item?>? MakeItem;
    public readonly Fx Fx = new();
    public readonly Random Rng = new();
    public FightSettings Fight = new();
    public Vector2 Cursor, CursorVel;
    public Figure? Hover;
    public float Scale = 1;
    /// <summary>Freeze-frame on big impacts: the simulation pauses while this counts down.</summary>
    public float HitStop;
    /// <summary>Accumulated shove for the mouse cursor (figures punching it); applied by the app.</summary>
    public Vector2 CursorPush;
    public static bool Debug;

    public World() => Current = this;

    /// <summary>Debug event log (only with --debug): %TEMP%\stickfight_events.log.</summary>
    public static void Log(string msg)
    {
        if (!Debug) return;
        try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "stickfight_events.log"), $"{DateTime.Now:HH:mm:ss.fff} {msg}\n"); }
        catch (IOException) { }
    }

    public void RemoveFigure(Figure f)
    {
        f.DropCarried(Vector2.Zero);
        Figures.Remove(f);
    }

    public void RemoveProp(Prop p)
    {
        if (p.Holder != null) p.Holder.Carrying = null;
        Props.Remove(p);
    }

    public void RemoveItem(Item it)
    {
        Items.Remove(it);
        foreach (var f in Figures) f.Brain.OnItemGone(it);
        if (it.Holder != null) { if (it.Holder.CarryingItem == it) it.Holder.CarryingItem = null; if (it.Holder.Weapon == it) it.Holder.Weapon = null; }
    }
}

/// <summary>Cheap particle effects: dust puffs and impact starbursts.</summary>
sealed class Fx
{
    struct Puff
    {
        public Vector2 Pos, Vel;
        public float Life, Max, R0, R1;
    }

    struct Ray
    {
        public Vector2 Pos, Dir;
        public float Life, Max, Len;
        public Color4 Color;
    }

    struct Spirit
    {
        public Vector2 Pos;
        public float Life, S;
        public Color4 Color;
    }

    static readonly Color4 Gold = new(1, 0.85f, 0.2f, 1);
    readonly List<Puff> _puffs = new();
    readonly List<Ray> _rays = new();
    readonly List<Spirit> _ghosts = new();
    public bool Any => _puffs.Count > 0 || _rays.Count > 0 || _ghosts.Count > 0;
    const float GhostLife = 2.4f;

    /// <summary>A little ghost floating up from a figure that died.</summary>
    public void Ghost(Vector2 at, Color4 color, float s) => _ghosts.Add(new Spirit { Pos = at, Color = color, S = s });

    /// <summary>A starburst (high-fives, hits). <paramref name="size"/> scales it; default colour is gold.</summary>
    public void Spark(Vector2 at, float s, Random rng, float size = 1, Color4? color = null)
    {
        int n = size > 1.2f ? 9 : 7;
        for (int i = 0; i < n; i++)
        {
            float a = i * MathF.Tau / n + rng.Range(-0.2f, 0.2f);
            _rays.Add(new Ray
            {
                Pos = at,
                Dir = new(MathF.Cos(a), MathF.Sin(a)),
                Max = 0.2f + 0.08f * size,
                Len = rng.Range(6, 10) * s * size,
                Color = color ?? Gold,
            });
        }
    }

    public void Dust(Vector2 at, float s, int n, float strength, Random rng)
    {
        strength = Math.Clamp(strength, 0.3f, 2.5f);
        for (int i = 0; i < n; i++)
        {
            float dir = i % 2 == 0 ? 1 : -1;
            _puffs.Add(new Puff
            {
                Pos = at + new Vector2(dir * rng.Range(2, 8) * s, -rng.Range(0, 3) * s),
                Vel = new(dir * rng.Range(30, 110) * s * strength, -rng.Range(10, 50) * s),
                Max = rng.Range(0.35f, 0.7f),
                R0 = rng.Range(1.5f, 3) * s,
                R1 = rng.Range(4, 7) * s * MathF.Min(1.5f, 0.6f + strength * 0.4f),
            });
        }
    }

    public void Step(float dt)
    {
        for (int i = _ghosts.Count - 1; i >= 0; i--)
        {
            var g = _ghosts[i];
            g.Life += dt;
            g.Pos.Y -= 45 * g.S * dt;
            if (g.Life >= GhostLife) _ghosts.RemoveAt(i); else _ghosts[i] = g;
        }
        for (int i = _rays.Count - 1; i >= 0; i--)
        {
            var r = _rays[i];
            r.Life += dt;
            if (r.Life >= r.Max) _rays.RemoveAt(i); else _rays[i] = r;
        }
        for (int i = _puffs.Count - 1; i >= 0; i--)
        {
            var p = _puffs[i];
            p.Life += dt;
            if (p.Life >= p.Max) { _puffs.RemoveAt(i); continue; }
            p.Pos += p.Vel * dt;
            p.Vel *= 1 - 4 * dt;
            _puffs[i] = p;
        }
    }

    public RectangleF? Bounds()
    {
        if (!Any) return null;
        float x1 = float.MaxValue, y1 = float.MaxValue, x2 = float.MinValue, y2 = float.MinValue;
        foreach (var g in _ghosts)
        {
            x1 = MathF.Min(x1, g.Pos.X - 14 * g.S); y1 = MathF.Min(y1, g.Pos.Y - 30 * g.S);
            x2 = MathF.Max(x2, g.Pos.X + 14 * g.S); y2 = MathF.Max(y2, g.Pos.Y + 22 * g.S);
        }
        foreach (var r in _rays)
        {
            float reach = r.Len * 2.2f;
            x1 = MathF.Min(x1, r.Pos.X - reach); y1 = MathF.Min(y1, r.Pos.Y - reach);
            x2 = MathF.Max(x2, r.Pos.X + reach); y2 = MathF.Max(y2, r.Pos.Y + reach);
        }
        foreach (var p in _puffs)
        {
            float r = MathF.Max(p.R0, p.R1);
            x1 = MathF.Min(x1, p.Pos.X - r); y1 = MathF.Min(y1, p.Pos.Y - r);
            x2 = MathF.Max(x2, p.Pos.X + r); y2 = MathF.Max(y2, p.Pos.Y + r);
        }
        return RectangleF.FromLTRB(x1, y1, x2, y2);
    }

    public void Draw(Renderer r)
    {
        foreach (var g in _ghosts)
        {
            // A pale little figure with a wavy tail, drifting upward and fading.
            float t = g.Life / GhostLife, a = MathF.Sin(MathF.PI * M.Clamp01(t * 1.4f)) * 0.55f * (1 - t * 0.5f), s = g.S;
            var c = new Color4(M.Lerp(g.Color.R, 1, 0.6f), M.Lerp(g.Color.G, 1, 0.6f), M.Lerp(g.Color.B, 1, 0.6f), a);
            Vector2 head = g.Pos + new Vector2(MathF.Sin(g.Life * 3) * 3 * s, -22 * s);
            r.Disc(head, 6 * s, c);
            Vector2 prev = head + new Vector2(0, 6 * s);
            for (int k = 1; k <= 5; k++)
            {
                Vector2 p = head + new Vector2(MathF.Sin(g.Life * 6 + k) * 2.5f * s, 6 * s + k * 4 * s);
                r.Line(prev, p, c, (3.2f - k * 0.45f) * s);
                prev = p;
            }
            r.Line(head + new Vector2(-1, 9) * s, head + new Vector2(-8, 13 + MathF.Sin(g.Life * 5) * 2) * s, c, 2.2f * s);
            r.Line(head + new Vector2(1, 9) * s, head + new Vector2(8, 13 + MathF.Cos(g.Life * 5) * 2) * s, c, 2.2f * s);
        }
        foreach (var ray in _rays)
        {
            float t = ray.Life / ray.Max;
            Vector2 a = ray.Pos + ray.Dir * ray.Len * (0.4f + t), b = ray.Pos + ray.Dir * ray.Len * (1 + t * 1.1f);
            var c = ray.Color;
            r.Line(a, b, new Color4(c.R, c.G, c.B, 1 - t), ray.Len * 0.28f);
        }
        foreach (var p in _puffs)
        {
            float t = p.Life / p.Max;
            float a = MathF.Pow(1 - t, 1.5f) * 0.5f;
            r.Disc(p.Pos, M.Lerp(p.R0, p.R1, t), new Color4(0.8f, 0.8f, 0.8f, a));
        }
    }
}
