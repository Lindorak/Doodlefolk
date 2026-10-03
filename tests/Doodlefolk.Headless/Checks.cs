using System.Numerics;
using System.Text.Json;

namespace Doodlefolk.Headless;

static class Checks
{
    static int passed, failed;

    public static int Run()
    {
        Console.WriteLine("Doodlefolk headless proof: production numeric helpers and covering-array planner only");
        Console.WriteLine("No Windows app, town lifecycle, rendering, sound, desktop integration, or Steam is loaded.");
        Test("ballistic launch/analytic endpoint, four drag profiles", () =>
        {
            foreach (float drag in new[] { 0, 0.15f, 0.9f, 2.4f })
            foreach (float duration in new[] { 0.2f, 0.8f, 1.1f, 1.6f })
            {
                Vector2 start = new(700, 980), goal = new(1180, 1020);
                var velocity = Ballistics.Launch(start, goal, 1800, drag, duration);
                Near(Ballistics.At(start, velocity, 1800, drag, duration), goal, 0.02f);
            }
        });
        Test("ballistic prediction versus production fixed-step flight", () =>
        {
            foreach (var (drag, duration) in new[] { (0.15f, 0.8f), (2.4f, 1.1f), (0.15f, 1.6f) })
            {
                Vector2 start = new(700, 980), goal = new(1180, 1020), last = start;
                var velocity = Ballistics.Launch(start, goal, 1800, drag, duration);
                int tick = 0, ticks = (int)MathF.Round(duration / Ballistics.Dt);
                Ballistics.Fly(start, velocity, 1800, drag, 6.5f, 100_000, 0.7f, 1, duration * 2,
                    (_, p, _, _) => { last = p; return ++tick < ticks; });
                Require(tick == ticks, "fixed-step count");
                Near(last, goal, 12); // The analytic solver intentionally approximates the discrete integrator.
            }
        });
        Test("flight integrates gravity, drag, then position", () =>
        {
            Vector2 p = new(4, 8), v = new(150, -200);
            var expectedVelocity = (v + new Vector2(0, 1800 * Ballistics.Dt)) * (1 - 0.15f * Ballistics.Dt);
            Ballistics.Fly(p, v, 1800, 0.15f, 6, 1000, 0.7f, 1, 1, (t, actualPosition, actualVelocity, count) =>
            {
                Near(actualVelocity, expectedVelocity, 0);
                Near(actualPosition, p + expectedVelocity * Ballistics.Dt, 0);
                Require(t == Ballistics.Dt && count == 0, "first tick metadata");
                return false;
            });
        });
        Test("bounce clamps to floor and reverses vertical velocity", () =>
        {
            int visited = 0;
            Ballistics.Fly(new(0, 94), new(100, 500), 0, 0, 6, 100, 0.65f, 1, 1, (_, p, v, bounces) =>
            {
                visited++;
                Near(p.Y, 94, 0); Near(v.Y, -325, 0); Near(v.X, 100, 0);
                Require(bounces == 1, "one bounce");
                return false;
            });
            Require(visited == 1, "callback stops the simulation");
        });
        Test("low-energy bounce terminates at rolling state", () =>
        {
            int visited = 0;
            Ballistics.Fly(new(0, 94), new(20, 100), 0, 0, 6, 100, 0.65f, 1, 10, (_, p, v, bounces) =>
            {
                visited++;
                Near(p.Y, 94, 0); Near(v.Y, 0, 0); Near(v.X, 20, 0);
                Require(bounces == 1, "one contact");
                return true;
            });
            Require(visited == 1, "Fly stops when rolling starts");
        });
        Test("flight is deterministic across repeated bounce traces", () =>
        {
            var first = Trace(); var second = Trace();
            Require(first.SequenceEqual(second), "identical trace");
            Require(first.Count > 100 && first[^1].Bounces >= 2, "multiple bounces were exercised");
        });
        Test("shot clears net for tennis and shuttlecock", () =>
        {
            foreach (float drag in new[] { 0.15f, 2.4f })
            {
                var velocity = Ballistics.Over(new(700, 990), new(1200, 1025), 1800, drag, 960, 990, 10, 2500);
                Require(velocity.HasValue, "shot exists");
                bool reached = false;
                Ballistics.Fly(new(700, 990), velocity!.Value, 1800, drag, 6, 100_000, 0.7f, 1, 10, (_, p, _, _) =>
                {
                    if (p.X < 960) return true;
                    reached = true;
                    Require(p.Y < 985, "clearance at net");
                    return false;
                });
                Require(reached, "reached net within horizon");
            }
        });
        Test("unreachable shot is rejected", () => Require(
            Ballistics.Over(Vector2.Zero, new(500, 0), 1800, 0.15f, 250, -100, 10, 1) is null, "speed bound"));
        Test("prop kinds retain persisted values and drag profiles", () =>
        {
            Require((int)PropKind.Ball == 0 && (int)PropKind.SoccerBall == 1 && (int)PropKind.Basketball == 2
                && (int)PropKind.BeachBall == 3 && (int)PropKind.TennisBall == 4 && (int)PropKind.Shuttlecock == 5, "persisted enum values");
            Near(Ballistics.DragOf(PropKind.BeachBall), 0.9f, 0);
            Near(Ballistics.DragOf(PropKind.Shuttlecock), 2.4f, 0);
            foreach (var kind in new[] { PropKind.Ball, PropKind.SoccerBall, PropKind.Basketball, PropKind.TennisBall })
                Near(Ballistics.DragOf(kind), 0.15f, 0);
        });
        Test("scalar springs stay finite and converge at 120 Hz", () =>
        {
            foreach (float omega in new[] { 4, 12, 24 })
            foreach (float damping in new[] { 0.5f, 0.8f, 1.0f })
            {
                float x = -100, v = 30;
                for (int tick = 0; tick < 2400; tick++)
                {
                    M.Spring(ref x, ref v, 25, omega, damping, Ballistics.Dt);
                    Require(float.IsFinite(x) && float.IsFinite(v), "finite scalar spring");
                }
                Near(x, 25, 0.001f); Near(v, 0, 0.001f);
            }
        });
        Test("vector spring equals the two independent scalar springs", () =>
        {
            Vector2 p = new(-40, 80), v = new(10, -30), target = new(20, -10);
            float x = p.X, y = p.Y, vx = v.X, vy = v.Y;
            for (int tick = 0; tick < 1200; tick++)
            {
                M.Spring(ref p, ref v, target, 12, 0.8f, Ballistics.Dt);
                M.Spring(ref x, ref vx, target.X, 12, 0.8f, Ballistics.Dt);
                M.Spring(ref y, ref vy, target.Y, 12, 0.8f, Ballistics.Dt);
                Near(p, new(x, y), 0.0001f); Near(v, new(vx, vy), 0.0001f);
            }
            Near(p, target, 0.001f);
        });
        Test("IK preserves both lengths across 10000 reachable and clamped targets", () =>
        {
            var rng = new Random(1701);
            for (int i = 0; i < 10_000; i++)
            {
                Vector2 anchor = new(rng.Range(-100, 100), rng.Range(-100, 100));
                var target = anchor + M.Dir(rng.Range(-MathF.PI, MathF.PI)) * rng.Range(0.1f, 200);
                float first = rng.Range(10, 50), second = rng.Range(10, 50);
                var (joint, end) = M.IK(anchor, target, first, second, i % 2 == 0 ? Vector2.UnitY : -Vector2.UnitY);
                Finite(joint); Finite(end);
                Near(Vector2.Distance(anchor, joint), first, 0.002f);
                Near(Vector2.Distance(joint, end), second, 0.002f);
                Require(Vector2.Distance(anchor, end) <= first + second + 0.002f, "maximum reach");
            }
        });
        Test("IK coincident equal-bone endpoint remains finite", () =>
        {
            var anchor = new Vector2(4, 8);
            var (joint, end) = M.IK(anchor, anchor, 20, 20, Vector2.UnitY);
            Near(Vector2.Distance(anchor, joint), 20, 0); Near(Vector2.Distance(joint, end), 20, 0);
            Near(end, anchor, 0);
        });
        Test("IK bend preference chooses the requested side", () =>
        {
            var positive = M.IK(Vector2.Zero, new(20, 0), 20, 20, Vector2.UnitY);
            var negative = M.IK(Vector2.Zero, new(20, 0), 20, 20, -Vector2.UnitY);
            Require(positive.joint.Y > 0 && negative.joint.Y < 0, "bend direction");
            Near(positive.end, negative.end, 0);
        });
        Test("numeric clamp and zero-length segment remain finite", () =>
        {
            Near(M.ClampIn(40, 10, 0), 5, 0);
            Near(M.ClampLength(new(3, 4), 2).Length(), 2, 0.0001f);
            Near(M.ClampLength(Vector2.Zero, 10), Vector2.Zero, 0);
            Near(M.DistToSegment(new(3, 4), Vector2.Zero, Vector2.Zero), 5, 0);
        });
        foreach (var (sizes, strength, name) in new[]
        {
            (new[] { 2, 2, 2 }, 2, "small pairs"),
            (new[] { 3, 3, 3, 3 }, 2, "four-factor pairs"),
            (new[] { 4, 3, 2, 5, 3, 2, 2, 3, 4, 2, 3, 2, 2, 2, 3, 2, 2, 2, 3, 2 }, 2, "20-factor pairs"),
            (new[] { 3, 2, 3, 2, 2, 3 }, 3, "six-factor triples"),
            (new[] { 2, 3, 3, 2, 4, 2, 3, 4, 2, 4, 4, 3, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2 }, 3, "26-factor triples"),
        })
            Test($"planner covers {name} (independent exhaustive check)", () =>
            {
                var rows = Coverage.Build(sizes, strength, new Random(3));
                Require(rows.Count > 0, "nonempty plan");
                foreach (var row in rows)
                {
                    Require(row.Length == sizes.Length, "row width");
                    for (int factor = 0; factor < sizes.Length; factor++)
                        Require(row[factor] >= 0 && row[factor] < sizes[factor], "valid factor value");
                }
                Require(Coverage.Covers(rows, sizes, strength), "production coverage check");
                CheckCoverageIndependently(rows, sizes, strength);
                Console.WriteLine($"  {rows.Count} planner rows; these are not executed towns");
            });
        Test("planner seed reproduces exact rows", () =>
        {
            int[] sizes = { 3, 2, 4, 2, 3 };
            var first = Coverage.Build(sizes, 2, new Random(5));
            var second = Coverage.Build(sizes, 2, new Random(5));
            Require(first.Select(r => string.Join(',', r)).SequenceEqual(second.Select(r => string.Join(',', r))), "same-seed rows");
        });
        Test("coverage checker detects a missing required row", () =>
        {
            int[] sizes = { 2, 2 };
            var rows = Coverage.Build(sizes, 2, new Random(1));
            rows.RemoveAt(0);
            Require(!Coverage.Covers(rows, sizes, 2), "missing pair must fail");
        });
        Test("replay JSON round-trips every input", () =>
        {
            var spec = Example();
            Require(ReplaySpec.Parse(JsonSerializer.Serialize(spec, ReplaySpec.JsonOptions)) == spec, "round-trip");
        });
        Test("replay produces identical same-runtime trace and result", () =>
        {
            var spec = Example();
            var first = Replay.Run(spec); var second = Replay.Run(spec);
            Require(first == second, "same spec produces same complete result");
            Require(first.FlightTicks > 100 && first.Bounces > 1, "flight coverage");
            Require(first.MaximumIkLengthError < 0.002f, "IK invariant");
            Require(Replay.Run(spec with { Seed = spec.Seed + 1 }).TraceSha256 != first.TraceSha256, "seed affects target stream");
        });
        Test("replay rejects unsupported version and unbounded tick counts", () =>
        {
            Throws(() => (Example() with { Version = 2 }).Validate());
            Throws(() => (Example() with { Ticks = 0 }).Validate());
            Throws(() => (Example() with { Ticks = 72_001 }).Validate());
        });
        Test("replay rejects non-finite and invalid physical inputs", () =>
        {
            Throws(() => (Example() with { Drag = float.NaN }).Validate());
            Throws(() => (Example() with { Gravity = float.PositiveInfinity }).Validate());
            Throws(() => (Example() with { Bounce = 1.1f }).Validate());
            Throws(() => (Example() with { Radius = 0 }).Validate());
            Throws(() => (Example() with { Position = new() { X = 0, Y = 1000 } }).Validate());
        });
        Test("replay rejects missing, unknown, or malformed JSON fields", () =>
        {
            Throws(() => ReplaySpec.Parse("{}"));
            Throws(() => ReplaySpec.Parse("null"));
            Throws(() => ReplaySpec.Parse("{"));
            var json = JsonSerializer.Serialize(Example(), ReplaySpec.JsonOptions);
            Throws(() => ReplaySpec.Parse(json.Replace("\"ticks\":", "\"typoTicks\":")));
            Throws(() => ReplaySpec.Parse(json.Replace("\"x\": 0", "\"x\": null")));
        });
        Test("replay respects one-tick and ten-minute bounds", () =>
        {
            Require(Replay.Run(Example() with { Ticks = 1 }).FlightTicks == 1, "one tick");
            var spec = Example() with { Ticks = 72_000, Gravity = 0, Drag = 0, Velocity = new() { X = 1, Y = 0 } };
            Require(Replay.Run(spec).FlightTicks == 72_000, "full horizon without early rolling");
        });
        Test("CLI replays JSON to stdout and a new output file", () => WithTemporaryDirectory(dir =>
        {
            string input = Path.Combine(dir, "input.json"), output = Path.Combine(dir, "result.json");
            File.WriteAllText(input, JsonSerializer.Serialize(Example(), ReplaySpec.JsonOptions));
            using var stdout = new StringWriter(); using var stderr = new StringWriter();
            Require(Cli.Run(new[] { "--replay", input, "--out", output }, stdout, stderr) == 0, "successful replay exit");
            Require(stderr.ToString().Length == 0, "no error output");
            Require(File.ReadAllText(output) == stdout.ToString(), "stdout matches saved report");
            using var json = JsonDocument.Parse(stdout.ToString());
            Require(json.RootElement.GetProperty("scope").GetString() == "numeric-helpers-only", "explicit report scope");
        }));
        Test("CLI rejects bad arguments/files and preserves existing output", () => WithTemporaryDirectory(dir =>
        {
            string input = Path.Combine(dir, "input.json"), existing = Path.Combine(dir, "keep.txt");
            File.WriteAllText(input, JsonSerializer.Serialize(Example(), ReplaySpec.JsonOptions));
            File.WriteAllText(existing, "keep exactly this");
            string oversized = Path.Combine(dir, "oversized.json");
            File.WriteAllText(oversized, new string(' ', 65_537));
            foreach (var (arguments, expected) in new[]
            {
                (new[] { "--unknown" }, 2),
                (new[] { "--replay", input, "--out" }, 2),
                (new[] { "--replay", Path.Combine(dir, "absent.json") }, 1),
                (new[] { "--replay", oversized }, 1),
                (new[] { "--replay", input, "--out", existing }, 1),
                (new[] { "--replay", input, "--out", input }, 1),
            })
            {
                using var stdout = new StringWriter(); using var stderr = new StringWriter();
                Require(Cli.Run(arguments, stdout, stderr) == expected, "error exit code");
                Require(stderr.ToString().Length > 0 && stdout.ToString().Length == 0, "errors go to stderr");
            }
            Require(File.ReadAllText(existing) == "keep exactly this", "existing output is unchanged");
            Require(ReplaySpec.Parse(File.ReadAllText(input)) == Example(), "input is unchanged");
        }));
        Console.WriteLine($"{passed} passed; {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    public static ReplaySpec Example() => new()
    {
        Version = 1, Seed = 1701, Ticks = 1200, Gravity = 1800, Drag = 0.15f,
        Radius = 6.5f, FloorY = 1000, Bounce = 0.7f,
        Position = new() { X = 0, Y = 500 }, Velocity = new() { X = 250, Y = -700 },
    };

    static List<(float Time, Vector2 Position, Vector2 Velocity, int Bounces)> Trace()
    {
        var trace = new List<(float, Vector2, Vector2, int)>();
        Ballistics.Fly(new(0, 500), new(250, -700), 1800, 0.15f, 6.5f, 1000, 0.7f, 1, 10,
            (t, p, v, b) => { trace.Add((t, p, v, b)); return true; });
        return trace;
    }

    // Intentionally does not call Coverage.Combinations or Coverage.Covers: a planner bug must
    // not be hidden by reusing the same combination-generation code in its test oracle.
    static void CheckCoverageIndependently(List<int[]> rows, int[] sizes, int strength)
    {
        for (int a = 0; a < sizes.Length; a++)
        for (int b = a + 1; b < sizes.Length; b++)
            if (strength == 2) Check(new[] { a, b });
            else for (int c = b + 1; c < sizes.Length; c++) Check(new[] { a, b, c });
        void Check(int[] factors)
        {
            var seen = rows.Select(row => string.Join(',', factors.Select(f => row[f]))).ToHashSet();
            int required = factors.Aggregate(1, (n, f) => n * sizes[f]);
            Require(seen.Count == required, $"uncovered values for factors {string.Join(',', factors)}");
        }
    }

    static void WithTemporaryDirectory(Action<string> action)
    {
        string dir = Path.Combine(Path.GetTempPath(), "doodlefolk-headless-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { action(dir); }
        finally { Directory.Delete(dir, true); }
    }

    static void Test(string name, Action body)
    {
        try { body(); passed++; Console.WriteLine($"PASS {name}"); }
        catch (Exception e) { failed++; Console.WriteLine($"FAIL {name}: {e.Message}"); }
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Near(float actual, float expected, float tolerance) =>
        Require(float.IsFinite(actual) && MathF.Abs(actual - expected) <= tolerance, $"expected {expected} ± {tolerance}, got {actual}");
    static void Near(Vector2 actual, Vector2 expected, float tolerance)
    {
        Finite(actual); Require(Vector2.Distance(actual, expected) <= tolerance, $"expected {expected} ± {tolerance}, got {actual}");
    }
    static void Finite(Vector2 p) => Require(float.IsFinite(p.X) && float.IsFinite(p.Y), $"non-finite vector {p}");
    static void Throws(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or JsonException) { return; }
        throw new InvalidOperationException("Invalid input was accepted.");
    }
}
