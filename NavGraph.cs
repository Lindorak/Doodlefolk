using System.Numerics;

namespace Doodlefolk;

enum MoveKind { Walk, Drop, Jump, Climb }

/// <summary>A surface on the map, identified by what it belongs to and where it is (platform objects are rebuilt every
/// frame; keys stay the same while nothing moves).</summary>
readonly record struct NavKey(IntPtr Hwnd, int Y, int X1);

/// <summary>One way of getting from one surface to another: walk on to the next one, step off the end and drop, jump
/// (an arc that's been checked to really land there), or climb a window's side.</summary>
sealed class NavEdge
{
    public NavKey From, To;
    public MoveKind Kind;
    public float FromX, ToX, ToY, Cost;
    public IntPtr WallHwnd;
    public int WallSide;
    public override string ToString() => $"{Kind} {FromX:0}->{ToX:0},{ToY:0}";
}

/// <summary>What a figure can do, which decides which moves exist for it (bigger figures jump further).</summary>
readonly record struct Mover(float S, float Gravity, float ClimbOffset, float Height)
{
    public int Bucket => (int)MathF.Round(S * 8);
}

/// <summary>The figures' map of the desktop: every surface they can stand on (window tops, lines of text, furniture,
/// the taskbar) linked by the moves between them. Moves are worked out lazily, the first time a route search needs
/// them, and kept until something on screen changes. Routes are found with A*.</summary>
sealed class NavGraph
{
    readonly Env _env;
    int _sig;
    readonly Dictionary<(NavKey, int), List<NavEdge>> _edges = new();
    readonly Dictionary<NavKey, Platform> _byKey = new();
    public int Version { get; private set; }

    public NavGraph(Env env) => _env = env;

    public static NavKey Key(Platform p) => new(p.Hwnd, (int)MathF.Round(p.Y), (int)MathF.Round(p.X1));
    public Platform? Get(NavKey k) => _byKey.TryGetValue(k, out var p) ? p : null;

    static bool Same(Platform a, Platform b) => a.Hwnd == b.Hwnd && MathF.Abs(a.Y - b.Y) < 2 && a.X1 < b.X2 && a.X2 > b.X1;

    /// <summary>Call once a frame after all surfaces are in: forgets cached moves if the layout changed.</summary>
    public void Refresh()
    {
        int sig = _env.Platforms.Count;
        _byKey.Clear();
        foreach (var p in _env.Platforms)
        {
            var k = Key(p);
            _byKey[k] = p;
            sig = HashCode.Combine(sig, k, (int)MathF.Round(p.X2));
        }
        foreach (var w in _env.Walls) sig = HashCode.Combine(sig, w.Hwnd, (int)w.X, (int)w.Y1, (int)w.Y2);
        if (sig == _sig) return;
        _sig = sig;
        _edges.Clear();
        Version++;
    }

    // ---------------- jumping ----------------

    /// <summary>Launch velocity for a jump from <paramref name="from"/> to <paramref name="to"/>, with an apex a little above
    /// the higher point (the figures' real jump limits).</summary>
    public static bool Lob(Vector2 from, Vector2 to, float g, float S, out Vector2 v)
    {
        v = default;
        float dx = to.X - from.X, rise = from.Y - to.Y;
        float apex = MathF.Max(rise, 0) + 26 * S + MathF.Abs(dx) * 0.12f;
        if (apex > 330 * S) return false;
        float vy = -MathF.Sqrt(2 * g * apex);
        float tUp = -vy / g;
        float tDown = MathF.Sqrt(2 * (apex - rise) / g);
        float vx = dx / (tUp + tDown);
        if (MathF.Abs(vx) > 700 * S) return false;
        v = new(vx, vy);
        return true;
    }

    /// <summary>Fly the arc and see what it actually lands on first (something in the way, like a couch seat over the
    /// floor, means the jump doesn't go where it was aimed).</summary>
    bool ArcLands(Vector2 from, Vector2 v, float g, Platform target, float top, float height)
    {
        const float dt = 1 / 60f;
        Vector2 p = from;
        for (float t = dt; t < 3; t += dt)
        {
            var q = from + v * t + new Vector2(0, 0.5f * g * t * t);
            if (q.Y - height < top) return false;          // would hit the top of the screen
            if (q.Y > p.Y && _env.FindLanding(q.X, p.Y, q.Y) is { } hit) return hit == target || Same(hit, target);
            p = q;
        }
        return false;
    }

    // ---------------- moves out of a surface ----------------

    public List<NavEdge> Edges(Platform a, Mover m)
    {
        var key = (Key(a), m.Bucket);
        if (!_edges.TryGetValue(key, out var list)) _edges[key] = list = Build(a, m);
        return list;
    }

    List<NavEdge> Build(Platform a, Mover m)
    {
        var list = new List<NavEdge>();
        var ka = Key(a);
        float S = m.S;
        var (L, R, T) = _env.BoundsAt((a.X1 + a.X2) / 2);

        // Off either end: on to a neighbour at the same height, or step off and drop to whatever's below.
        foreach (int side in new[] { -1, 1 })
        {
            float ex = side < 0 ? a.X1 : a.X2;
            if (ex <= L + 8 * S || ex >= R - 8 * S) continue;
            float lx = ex + side * 10 * S;
            Platform? next = null;
            foreach (var b in _env.Platforms)
                if (b != a && MathF.Abs(b.Y - a.Y) < 3 && lx >= b.X1 - 2 && lx <= b.X2 + 2) { next = b; break; }
            if (next != null)
            {
                list.Add(new NavEdge { From = ka, To = Key(next), Kind = MoveKind.Walk, FromX = ex - side * 2 * S, ToX = lx, ToY = next.Y, Cost = 0.1f });
                continue;
            }
            if (_env.Below(lx, a.Y + 3) is not { } below) continue;
            float h = below.Y - a.Y;
            if (h < 3 || h > 1700 * S) continue;
            list.Add(new NavEdge { From = ka, To = Key(below), Kind = MoveKind.Drop, FromX = ex - side * 3 * S, ToX = lx, ToY = below.Y, Cost = 0.3f + MathF.Sqrt(2 * h / m.Gravity) + h / (1200 * S) });
        }

        // Jumps: one good way to each surface in range, checked by flying the arc.
        foreach (var b in _env.Platforms)
        {
            if (b == a || Same(a, b) || b.X2 - b.X1 < 16 * S) continue;
            float rise = a.Y - b.Y;
            if (rise > 300 * S || rise < -1500 * S) continue;
            float gap = b.X1 > a.X2 ? b.X1 - a.X2 : a.X1 > b.X2 ? a.X1 - b.X2 : 0;
            if (gap > 520 * S) continue;
            foreach (var (fx, tx) in JumpCandidates(a, b, S))
            {
                var from = new Vector2(fx, a.Y);
                if (!Lob(from, new Vector2(tx, b.Y), m.Gravity, S, out var v) || !ArcLands(from, v, m.Gravity, b, T, m.Height)) continue;
                float air = -v.Y / m.Gravity * 2;
                list.Add(new NavEdge { From = ka, To = Key(b), Kind = MoveKind.Jump, FromX = fx, ToX = tx, ToY = b.Y, Cost = 0.5f + air + MathF.Max(0, rise) / (400 * S) });
                break;
            }
        }

        // Climbs: up a window's side to its top.
        foreach (var wall in _env.Walls)
        {
            if (!wall.ReachesTop || wall.Y2 < a.Y - 6 * S || wall.Y1 > a.Y - 20 * S) continue;
            float x = wall.X + wall.Side * m.ClimbOffset;
            if (x < a.X1 + 4 * S || x > a.X2 - 4 * S) continue;
            if (_env.SupportAt(wall.X - wall.Side * 9 * S, wall.Y1, wall.Hwnd) is not { } top || top.Hwnd != wall.Hwnd) continue;
            float rise = a.Y - wall.Y1;
            list.Add(new NavEdge
            {
                From = ka, To = Key(top), Kind = MoveKind.Climb, FromX = x, ToX = wall.X - wall.Side * 12 * S, ToY = top.Y,
                Cost = 1 + rise / (70 * S), WallHwnd = wall.Hwnd, WallSide = wall.Side,
            });
        }
        return list;
    }

    /// <summary>Where to take off from and land, best first.</summary>
    static IEnumerable<(float from, float to)> JumpCandidates(Platform a, Platform b, float S)
    {
        float A(float x) => M.ClampIn(x, a.X1 + 4 * S, a.X2 - 4 * S);
        float B(float x) => M.ClampIn(x, b.X1 + 6 * S, b.X2 - 6 * S);
        if (b.X1 >= a.X2)
        {
            yield return (A(a.X2 - 6 * S), B(b.X1 + 14 * S));
            yield return (A(a.X2 - 6 * S), B(b.X1 + 50 * S));
            yield return (A(a.X2 - 50 * S), B(b.X1 + 14 * S));
        }
        else if (b.X2 <= a.X1)
        {
            yield return (A(a.X1 + 6 * S), B(b.X2 - 14 * S));
            yield return (A(a.X1 + 6 * S), B(b.X2 - 50 * S));
            yield return (A(a.X1 + 50 * S), B(b.X2 - 14 * S));
        }
        else
        {
            float o1 = MathF.Max(a.X1, b.X1), o2 = MathF.Min(a.X2, b.X2), mid = (o1 + o2) / 2;
            if (b.Y < a.Y)
            {
                // Above and overlapping: hop up from underneath.
                yield return (A(mid - 18 * S), B(mid + 6 * S));
                yield return (A(o1 + 6 * S), B(o1 + 24 * S));
                yield return (A(o2 - 6 * S), B(o2 - 24 * S));
            }
            else
            {
                // Below and overlapping (the floor under a couch): land past one of our ends.
                if (b.X1 < a.X1 - 16 * S) yield return (A(a.X1 + 6 * S), B(a.X1 - 20 * S));
                if (b.X2 > a.X2 + 16 * S) yield return (A(a.X2 - 6 * S), B(a.X2 + 20 * S));
            }
        }
    }

    // ---------------- routes ----------------

    /// <summary>A* from where the figure stands to the goal surface. <paramref name="extra"/> adds a figure's own
    /// preferences and bad experiences to each move's cost. Null if there's no way (within a search budget).</summary>
    public List<NavEdge>? FindPath(Platform start, float sx, Platform goal, float gx, Mover m, Func<NavEdge, float>? extra, int budget = 600)
    {
        var sk = Key(start);
        var gk = Key(goal);
        float S = m.S;
        float H(float x, float y) => MathF.Abs(x - gx) / (260 * S) + MathF.Abs(y - goal.Y) / (700 * S);
        var open = new PriorityQueue<(NavKey k, float x), float>();
        var best = new Dictionary<NavKey, float> { [sk] = 0 };
        var came = new Dictionary<NavKey, NavEdge>();
        var done = new HashSet<NavKey>();
        open.Enqueue((sk, sx), H(sx, start.Y));
        int n = 0;
        while (open.TryDequeue(out var cur, out _))
        {
            if (!done.Add(cur.k)) continue;
            var a = Get(cur.k);
            if (a == null) continue;
            if (cur.k == gk || Same(a, goal))
            {
                var path = new List<NavEdge>();
                for (var k = cur.k; came.TryGetValue(k, out var e); k = e.From) path.Add(e);
                path.Reverse();
                return path;
            }
            if (++n > budget) break;
            float g0 = best[cur.k];
            foreach (var e in Edges(a, m))
            {
                if (done.Contains(e.To)) continue;
                float c = g0 + MathF.Abs(e.FromX - cur.x) / (90 * S) + e.Cost + (extra?.Invoke(e) ?? 0);
                if (best.TryGetValue(e.To, out var old) && old <= c) continue;
                best[e.To] = c;
                came[e.To] = e;
                open.Enqueue((e.To, e.ToX), c + H(e.ToX, e.ToY));
            }
        }
        return null;
    }
}
