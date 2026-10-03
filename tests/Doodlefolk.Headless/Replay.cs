using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Doodlefolk.Headless;

/// <summary>Versioned inputs for a numeric-helper experiment, not a saved town or --simrun spec.</summary>
sealed record ReplaySpec
{
    public required int Version { get; init; }
    public required int Seed { get; init; }
    public required int Ticks { get; init; }
    public required float Gravity { get; init; }
    public required float Drag { get; init; }
    public required float Radius { get; init; }
    public required float FloorY { get; init; }
    public required float Bounce { get; init; }
    public required Point Position { get; init; }
    public required Point Velocity { get; init; }

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static ReplaySpec Parse(string json)
    {
        var spec = JsonSerializer.Deserialize<ReplaySpec>(json, JsonOptions)
            ?? throw new ArgumentException("Replay JSON must be an object.");
        spec.Validate();
        return spec;
    }

    public void Validate()
    {
        if (Version != 1) throw new ArgumentException("Only replay version 1 is supported.");
        if (Ticks is < 1 or > 72_000) throw new ArgumentException("ticks must be 1..72000 (up to ten minutes at 120 Hz).");
        Range(Gravity, 0, 10_000, "gravity");
        Range(Drag, 0, 10, "drag");
        Range(Radius, 0.01f, 1000, "radius");
        Range(FloorY, -100_000, 100_000, "floorY");
        Range(Bounce, 0, 1, "bounce");
        if (Position is null || Velocity is null) throw new ArgumentException("position and velocity are required.");
        Range(Position.X, -100_000, 100_000, "position.x");
        Range(Position.Y, -100_000, 100_000, "position.y");
        Range(Velocity.X, -10_000, 10_000, "velocity.x");
        Range(Velocity.Y, -10_000, 10_000, "velocity.y");
        if (Position.Y + Radius > FloorY) throw new ArgumentException("The ball must start above or touching the floor.");
    }

    static void Range(float value, float min, float max, string name)
    {
        if (!float.IsFinite(value) || value < min || value > max)
            throw new ArgumentException($"{name} must be finite and within {min}..{max}.");
    }
}

sealed record Point
{
    public required float X { get; init; }
    public required float Y { get; init; }
    public Vector2 Vector() => new(X, Y);
    public static Point From(Vector2 p) => new() { X = p.X, Y = p.Y };
}

sealed record ReplayResult(int Version, string Scope, string Runtime, string Architecture, ReplaySpec Spec,
    int FlightTicks, int Bounces, Point BallPosition, Point BallVelocity, Point SpringPosition,
    Point SpringVelocity, float MaximumIkLengthError, string TraceSha256);

static class Replay
{
    public static ReplayResult Run(ReplaySpec spec)
    {
        spec.Validate();
        var rng = new Random(spec.Seed);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        int flightTicks = 0, bounces = 0;
        var ball = spec.Position.Vector();
        var velocity = spec.Velocity.Vector();
        // Fly normally ends when the ball starts rolling. A generous time horizon avoids float
        // accumulation ending early; the callback enforces the exact maximum tick count.
        Ballistics.Fly(ball, velocity, spec.Gravity, spec.Drag, spec.Radius, spec.FloorY, spec.Bounce, 1,
            (spec.Ticks + 1) * Ballistics.Dt * 2, (_, p, v, count) =>
            {
                flightTicks++;
                ball = p; velocity = v; bounces = count;
                RequireFinite(p); RequireFinite(v);
                Require(p.Y + spec.Radius <= spec.FloorY + 0.02f, "Ball penetrated the flat floor.");
                Add(p.X); Add(p.Y); Add(v.X); Add(v.Y); Add(count);
                return flightTicks < spec.Ticks;
            });
        // Independently exercise the production animation math over a bounded seeded target stream.
        // These are spring/IK helper checks, not full character poses, behaviours, or rendered frames.
        Vector2 spring = Vector2.Zero, springVelocity = Vector2.Zero, target = Vector2.Zero;
        float maximumIkLengthError = 0;
        for (int tick = 0; tick < spec.Ticks; tick++)
        {
            if (tick % 120 == 0) target = new Vector2(rng.Range(-100, 100), rng.Range(-100, 100));
            M.Spring(ref spring, ref springVelocity, target, 12, 0.8f, Ballistics.Dt);
            RequireFinite(spring); RequireFinite(springVelocity);
            var aim = new Vector2(rng.Range(0.1f, 100), rng.Range(-100, 100));
            float first = rng.Range(10, 50), second = rng.Range(10, 50);
            var (joint, end) = M.IK(Vector2.Zero, aim, first, second, Vector2.UnitY);
            RequireFinite(joint); RequireFinite(end);
            float error = MathF.Max(MathF.Abs(joint.Length() - first), MathF.Abs(Vector2.Distance(joint, end) - second));
            maximumIkLengthError = MathF.Max(maximumIkLengthError, error);
            Require(error < 0.002f, $"IK changed a bone length by {error}.");
            Add(spring.X); Add(spring.Y); Add(springVelocity.X); Add(springVelocity.Y);
            Add(joint.X); Add(joint.Y); Add(end.X); Add(end.Y);
        }
        return new(1, "numeric-helpers-only", RuntimeInformation.FrameworkDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), spec, flightTicks, bounces,
            Point.From(ball), Point.From(velocity), Point.From(spring), Point.From(springVelocity),
            maximumIkLengthError, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());

        void Add(float value)
        {
            Span<byte> bytes = stackalloc byte[4];
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes, BitConverter.SingleToInt32Bits(value));
            hash.AppendData(bytes);
        }
    }

    static void RequireFinite(Vector2 p) => Require(float.IsFinite(p.X) && float.IsFinite(p.Y), "Non-finite position or velocity.");
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
