using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>The little ones. Rabbits hop, flop into a loaf, thump a back foot when alarmed, binky (leap and twist)
/// when happy, use a litter box, and nibble whatever's green (the garden!). Hamsters are tiny, sleep the day away and
/// come alive at night, stuff their cheeks, and run for miles on a wheel. Both are prey to the cats.</summary>
sealed partial class Pet
{
    float _pouch;
    Item? _wheel;

    bool Small => Kind is PetKind.Rabbit or PetKind.Hamster;
    /// <summary>What a cat (or a dog) would chase.</summary>
    public bool IsPrey => Kind is PetKind.Parrot or PetKind.Hamster or PetKind.Rabbit;

    // ---------------- behaviour ----------------

    /// <summary>Rabbit and hamster options, added to the usual ones.</summary>
    void SmallOptions(World w, Action<float, string, Action> add)
    {
        if (Kind == PetKind.Hamster)
        {
            // Nocturnal: the day is for sleeping, the night for the wheel.
            if (w.Night < 0.3f && Energy < 0.95f) add(1.2f * (1 - w.Night), "sleep (it's daytime)", () => GoSleep(w));
            if (Nearest(w, "hamsterwheel") is { } wheel && Energy > 0.35f)
                add((0.4f + Boredom * 1.4f) * (0.5f + w.Night * 1.5f), "run on the wheel", () => Travel(() => w.Items.Contains(wheel) ? wheel.Pos : null, 1, 4 * S, 20, () => { _wheel = wheel; Go(State.Wheel, _rng.Range(10, 30)); }));
        }
        if (Kind == PetKind.Rabbit)
        {
            // A happy rabbit binkies: a leap with a twist.
            if (Happiness > 0.7f && Energy > 0.5f) add(0.35f * Tm(Temperament.Playful, 2), "binky", () => { Go(State.Zoomies, 2.5f); Vel = new Vector2(Facing * 80 * S, -420 * S); Grounded = false; });
            // Anything green is fair game (the garden especially).
            if (Tempted(Habit.Chewing, 0.2f + Boredom * 0.3f + Hunger * 0.3f) && w.Items.Where(i => i.IsPlant && i.Def.Key != "seedpatch" && Vector2.Distance(i.Pos, Pos) < 1500 * _s).OrderBy(i => Vector2.Distance(i.Pos, Pos)).FirstOrDefault() is { } plant)
                add(1.1f, "nibble a plant", () => Travel(() => w.Items.Contains(plant) ? Beside(plant) : null, 1.1f, 6 * S, 20, () => NibblePlant(plant, w)));
        }
    }

    void NibblePlant(Item plant, World w)
    {
        if (!w.Items.Contains(plant)) return;
        Misdeed(Habit.Chewing, w);
        Hunger = MathF.Max(0, Hunger - 0.25f);
        Go(State.Eat, 0.1f);
        _pose = Pose.HeadDown;
        string what = plant.Def.Name.ToLowerInvariant();
        var planter = w.Figures.FirstOrDefault(f => f.Id == plant.PlanterId);
        w.RemoveItem(plant);
        Log($"Ate the {what}!");
        w.News("pets", $"{Name} ate {(planter != null ? planter.Name + "'s" : "a")} {what}", 1);
        planter?.Emote("my " + what + "!!", 1.6f);
    }

    void DoWheel(World w, float dt)
    {
        var wheel = _wheel;
        if (wheel == null || !w.Items.Contains(wheel) || wheel.Holder != null || _t > _dur || Energy < 0.15f) { if (wheel != null) wheel.SpinV = 0; GoIdle(1); return; }
        Pos = new Vector2(wheel.Pos.X, wheel.Pos.Y - 3 * wheel.Sc);
        Vel = Vector2.Zero;
        _walk += dt * 22;
        wheel.SpinV = 7 * Facing;
        Boredom = MathF.Max(0, Boredom - dt * 0.04f);
        Energy = MathF.Max(0, Energy - dt * 0.002f);
        Weight = MathF.Max(0, Weight - dt * 0.00002f);
        if ((int)(_t * 2) != (int)((_t - dt) * 2) && _rng.NextDouble() < 0.3) World.Play(Sfx.Squeak, wheel.Pos, 0.05f, 2.4f, 0.4);
        _pose = Pose.Stand;
    }

    /// <summary>Stuffing its cheeks (hamsters, after eating).</summary>
    void TickPouch(float dt) => _pouch = MathF.Max(0, _pouch - dt * 0.05f);

    // ---------------- drawing ----------------

    void DrawRabbit(Renderer r)
    {
        float s = S, f = Facing, H = Height, L = Length, g = Girth;
        var ink = new Color4(0.12f, 0.12f, 0.12f, 0.9f);
        var col = Lit(Color);
        var dark = Darken(col, 0.78f);
        var white = Lit(new Color4(0.97f, 0.96f, 0.94f, 1));
        float headK = 1 + (1 - Age) * 0.4f;
        bool moving = Grounded && MathF.Abs(Vel.X) > 4 * s;
        float hop = moving ? MathF.Abs(MathF.Sin(_walk * 0.55f)) * H * 0.45f : 0;
        Vector2 P(float x, float y) => Pos + new Vector2(x * f, y - hop);
        var pose = Held || (!Grounded && !Flying) ? Pose.Air : _pose;
        bool loaf = pose is Pose.Sit or Pose.Curl or Pose.Lie || _st == State.Sleep;
        bool upright = pose == Pose.Upright || (_st is State.Ask or State.Flee && !moving);
        bool lop = Id % 3 == 0;
        // Body.
        Vector2 body = upright ? P(-L * 0.05f, -H * 0.62f) : P(0, -H * 0.4f);
        float rx = (upright ? L * 0.28f : L * 0.46f) * g, ry = (upright ? H * 0.5f : H * (loaf ? 0.36f : 0.4f)) * MathF.Sqrt(g);
        // Big hind foot, then the body over it.
        r.Oval(P(-L * 0.15f, -H * 0.1f), L * 0.27f + 1 * s, H * 0.13f + 1 * s, ink);
        r.Oval(P(-L * 0.15f, -H * 0.1f), L * 0.27f, H * 0.13f, dark);
        r.Oval(body, rx + 1.2f * s, ry + 1.2f * s, ink);
        r.Oval(body, rx, ry, col);
        if (Gfx.Q.Shading) r.Oval(body - new Vector2(rx * 0.2f, ry * 0.35f), rx * 0.5f, ry * 0.3f, new Color4(1, 1, 1, 0.18f));
        // Tail puff and front paws.
        r.Disc(P(-L * 0.47f, -H * 0.45f), H * 0.16f + 0.8f * s, ink);
        r.Disc(P(-L * 0.47f, -H * 0.45f), H * 0.16f, white);
        if (!loaf) r.Line(P(L * 0.25f, -H * (upright ? 0.55f : 0.2f)), P(L * 0.3f, upright ? -H * 0.4f : -0.5f * s), dark, 2.4f * s);
        // Head and ears.
        float hr = H * 0.3f * headK;
        Vector2 head = upright ? P(L * 0.12f, -H * 1.1f) : P(L * 0.38f, -H * (loaf ? 0.55f : 0.68f));
        bool back = moving || _st == State.Sleep;
        foreach (float side in new[] { -0.25f, 0.2f })
        {
            Vector2 baseP = head + new Vector2(f * hr * side, -hr * 0.6f);
            Vector2 tip = lop ? baseP + new Vector2(-f * hr * 0.4f, hr * 1.6f) : back ? baseP + new Vector2(-f * hr * 2.2f, -hr * 0.8f) : baseP + new Vector2(-f * hr * 0.3f, -hr * 2.3f);
            r.Line(baseP, tip, ink, hr * 0.62f + 1.2f * s);
            r.Line(baseP, tip, col, hr * 0.62f);
            r.Line(Vector2.Lerp(baseP, tip, 0.2f), Vector2.Lerp(baseP, tip, 0.85f), new Color4(0.95f, 0.65f, 0.7f, 0.85f), hr * 0.25f);
        }
        r.Disc(head, hr + 1.2f * s, ink);
        r.ShadedDisc(head, hr, col);
        var eye = head + new Vector2(f * hr * 0.35f, -hr * 0.1f);
        if (_st == State.Sleep || _st == State.Petted) r.Line(eye - new Vector2(hr * 0.2f, 0), eye + new Vector2(hr * 0.2f, 0), ink, 0.9f * s);
        else { r.Disc(eye, hr * 0.2f, ink); r.Disc(eye + new Vector2(-0.4f * s, -0.4f * s), hr * 0.07f, new Color4(1, 1, 1, 0.9f)); }
        r.Disc(head + new Vector2(f * hr * 0.92f, hr * 0.1f), hr * 0.13f, new Color4(0.95f, 0.55f, 0.6f, 1));
        if (Gfx.Q.DetailedArt) { var wc = new Color4(0.2f, 0.2f, 0.2f, 0.4f); Vector2 n = head + new Vector2(f * hr * 0.9f, hr * 0.2f); r.Line(n, n + new Vector2(f * hr * 0.7f, -hr * 0.1f), wc, 0.5f * s); r.Line(n, n + new Vector2(f * hr * 0.7f, hr * 0.15f), wc, 0.5f * s); }
        if (_st == State.Sleep) { float z = (_sleepZ * 0.5f) % 1; r.Text("z", head + new Vector2(0, -hr * 2 - z * 12 * s), (6 + z * 4) * s, Ui.Ink.A(1 - z), true); }
        DrawOutfit(r, P(-L * 0.35f, -H * 0.4f), P(L * 0.3f, -H * 0.45f), ry * 1.6f, head, hr, f);
    }

    void DrawHamster(Renderer r)
    {
        float s = S, f = Facing, H = Height, L = Length, g = Girth;
        var ink = new Color4(0.12f, 0.12f, 0.12f, 0.9f);
        var col = Lit(Color);
        var belly = Lit(new Color4(0.98f, 0.95f, 0.88f, 1));
        bool asleep = _st == State.Sleep;
        float bob = _st == State.Wheel ? MathF.Sin(_walk * 2) * 0.6f * s : 0;
        Vector2 P(float x, float y) => Pos + new Vector2(x * f, y + bob);
        // Little feet scurrying.
        if (!asleep)
            for (int i = 0; i < 2; i++)
            {
                float ph = _walk * 1.3f + i * MathF.PI;
                float mv = (Grounded && MathF.Abs(Vel.X) > 3 * s) || _st == State.Wheel ? 1 : 0;
                r.Line(P(-L * 0.15f + i * L * 0.35f, -H * 0.15f), P(-L * 0.15f + i * L * 0.35f + MathF.Sin(ph) * 1.5f * s * mv, -0.3f * s), Darken(col, 0.7f), 1.4f * s);
            }
        Vector2 body = P(0, -H * 0.5f);
        float rx = L * 0.52f * g, ry = H * 0.5f * MathF.Sqrt(g);
        if (asleep) { rx *= 0.85f; ry *= 0.85f; body = P(0, -H * 0.4f); }
        r.Oval(body, rx + 1 * s, ry + 1 * s, ink);
        r.Oval(body, rx, ry, col);
        r.Oval(body + new Vector2(f * rx * 0.2f, ry * 0.35f), rx * 0.6f, ry * 0.5f, belly);
        // Ears, cheeks, eye, nose.
        foreach (float ex in new[] { 0.05f, 0.3f })
        {
            var e = P(L * ex, -H * 0.95f);
            r.Disc(e, H * 0.17f + 0.6f * s, ink); r.Disc(e, H * 0.17f, col); r.Disc(e, H * 0.09f, new Color4(0.95f, 0.65f, 0.7f, 1));
        }
        if (_pouch > 0.05f) { var ch = P(L * 0.38f, -H * 0.35f); r.Disc(ch, H * (0.18f + 0.18f * _pouch) + 0.6f * s, ink); r.Disc(ch, H * (0.18f + 0.18f * _pouch), belly); }
        var eye = P(L * 0.33f, -H * 0.62f);
        if (asleep) r.Line(eye - new Vector2(1.2f * s, 0), eye + new Vector2(1.2f * s, 0), ink, 0.8f * s);
        else r.Disc(eye, 1.1f * s, ink);
        r.Disc(P(L * 0.52f, -H * 0.5f), 0.8f * s, new Color4(0.95f, 0.55f, 0.6f, 1));
        if (asleep) { float z = (_sleepZ * 0.5f) % 1; r.Text("z", P(L * 0.2f, -H * 1.4f - z * 10 * s), (5 + z * 3) * s, Ui.Ink.A(1 - z), true); }
        DrawOutfit(r, P(-L * 0.3f, -H * 0.5f), P(L * 0.3f, -H * 0.5f), ry * 1.6f, P(L * 0.25f, -H * 0.7f), H * 0.3f, f);
    }
}
