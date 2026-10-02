using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Things out in the world: vehicles (wheels that turn as they're ridden), water (a pond with ducks and lily
/// pads, a pool; swimmers show through the water), and lights (a lamp, fairy lights, a lantern, and homes whose windows
/// glow at night), which light up whoever is near them.</summary>
sealed partial class Item
{
    /// <summary>Whoever is riding it (bike, skateboard, go-kart).</summary>
    public Figure? Rider;
    /// <summary>How many are swimming in it (water ripples while anyone is).</summary>
    public int Swimmers;

    public bool IsVehicle => Def.Verbs.Contains(Verb.Ride);
    public bool IsWater => Def.Verbs.Contains(Verb.Swim);
    public bool IsLight => Def.Key is "lamp" or "fairylights" or "lantern";

    static float NightNow => World.Current is { DayNight: true } w ? w.Night : 0;
    static float LightsOn => M.Smooth(M.Clamp01((NightNow - 0.15f) / 0.35f));

    bool AnimatingWorld => Rider != null || Def.Key == "pond" || (Def.Key == "pool" && Swimmers > 0) || (Def.Key == "fairylights" && LightsOn > 0.01f);

    /// <summary>Changes that need a redraw for lights (they come on at dusk).</summary>
    int WorldStateKey => IsLight || Def.Key is "treehouse" or "fort" or "tent" ? (int)(LightsOn * 20) : 0;

    /// <summary>Keep a ridden vehicle under its rider.</summary>
    bool StepRidden(float dt)
    {
        if (Rider is not { } rd) return false;
        if (!World.Current!.Figures.Contains(rd) || rd.Riding != this || rd.Mode != Mode.Control) { Rider = null; if (rd.Riding == this) rd.Riding = null; return false; }
        Pos = rd.Base;
        GroundHwnd = rd.GroundHwnd;
        Vel = rd.Vel;
        OnGround = rd.Grounded;
        Flip = rd.Facing < 0;
        Angle = rd.Grounded ? 0 : Math.Clamp(rd.Vel.Y / (2400 * _s), -0.35f, 0.35f) * rd.Facing;
        float wheelR = Def.Key switch { "bike" => 6, "gokart" => 4.2f, _ => 1.3f } * Sc;
        WheelAngle += rd.Vel.X * dt / wheelR;
        return true;
    }

    /// <summary>Where it shines, how far, and how brightly (nothing by day).</summary>
    public (Vector2 at, float reach, float strength)? Light()
    {
        float on = LightsOn;
        if (on <= 0.01f) return null;
        return Def.Key switch
        {
            "lamp" => (Local(0, 46), 230 * _s * MathF.Max(1, SizeMul), 0.55f * on),
            "fairylights" => (Local(0, 34), 200 * _s * MathF.Max(1, SizeMul), 0.35f * on),
            "lantern" when Holder != null || OnGround => (Local(0, 8), 120 * _s * MathF.Max(1, SizeMul), 0.45f * on),
            "treehouse" => (Local(18, 76), 110 * _s, 0.3f * on),
            _ => null,
        };
    }

    void DrawWorldLive(Renderer r, bool over, double time)
    {
        float sc = Sc, t = (float)time;
        switch (Def.Key)
        {
            case "bike":
                if (over) break;
                foreach (float x in new[] { -13f, 13f }) DrawWheel(r, Local(x, 6), 6 * sc, true);
                break;
            case "gokart":
                if (over) break;
                foreach (float x in new[] { -13f, 13f }) DrawWheel(r, Local(x, 4.2f), 4.2f * sc, false);
                break;
            case "skateboard":
                if (over) break;
                foreach (float x in new[] { -7.5f, 7.5f }) { r.Disc(Local(x, 1.2f), 1.5f * sc, Ink); r.Disc(Local(x, 1.2f), 1.1f * sc, M.Hex(0xFFF3E0)); }
                break;
            case "pond":
            case "pool":
                DrawWater(r, over, t);
                break;
            case "lamp":
            {
                if (over) break;
                float on = LightsOn;
                if (on <= 0.01f) break;
                var bulb = Local(0, 47);
                // A soft cone of light to the floor, then the glow.
                r.FillPolygon(stackalloc Vector2[] { Local(-7, 48), Local(7, 48), Local(26, 0), Local(-26, 0) }, new Color4(1, 0.88f, 0.55f, 0.07f * on));
                r.FireGlow(bulb, 70 * sc * on, 60 * sc * on, 0.5f * on);
                r.Disc(bulb, 2.3f * sc, new Color4(1, 0.97f, 0.78f, on));
                break;
            }
            case "fairylights":
            {
                if (over) break;
                float on = LightsOn;
                if (on <= 0.01f) break;
                var cols = new[] { M.Hex(0xFFD54F), M.Hex(0xEF5350), M.Hex(0x66BB6A), M.Hex(0x42A5F5), M.Hex(0xAB47BC) };
                for (int i = 0; i < 9; i++)
                {
                    float x = -64 + i * 16, y = 46 - 14 * (1 - (x / 72) * (x / 72)) - 1.6f;
                    float tw = 0.65f + 0.35f * MathF.Sin(t * (1.3f + i * 0.37f) + i * 2.1f);
                    var c = cols[i % cols.Length];
                    var at = Local(x, y);
                    r.FireGlow(at, 12 * sc * on * tw, 12 * sc * on * tw, 0.35f * on * tw);
                    r.Disc(at, 1.9f * sc, new Color4(MathF.Min(1, c.R + 0.25f), MathF.Min(1, c.G + 0.25f), MathF.Min(1, c.B + 0.25f), on * (0.6f + 0.4f * tw)));
                }
                break;
            }
            case "lantern":
            {
                if (over) break;
                float on = LightsOn;
                if (on <= 0.01f) break;
                r.FireGlow(Local(0, 8), 34 * sc * on, 30 * sc * on, 0.5f * on);
                r.Oval(Local(0, 7.5f), 2.6f * sc, 4 * sc, new Color4(1, 0.9f, 0.55f, 0.8f * on));
                break;
            }
            case "treehouse":
            {
                if (over) break;
                float on = LightsOn;
                if (on <= 0.01f) break;
                var a = Local(14.5f, 72.5f); var b = Local(21.5f, 79.5f);
                r.FillPolygon(stackalloc Vector2[] { a, new Vector2(b.X, a.Y), b, new Vector2(a.X, b.Y) }, new Color4(1, 0.82f, 0.42f, 0.95f * on));
                r.FireGlow(Local(18, 76), 22 * sc * on, 18 * sc * on, 0.35f * on);
                break;
            }
            case "fort":
            case "tent":
            {
                // Someone home after dark: a glow from the doorway.
                if (over || User == null) break;
                float on = LightsOn;
                if (on <= 0.01f) break;
                r.FireGlow(Local(0, 8), 26 * sc * on, 14 * sc * on, 0.35f * on);
                break;
            }
        }
    }

    void DrawWheel(Renderer r, Vector2 c, float rad, bool spokes)
    {
        var tyre = new Color4(0.13f, 0.13f, 0.13f, 1);
        if (spokes)
        {
            r.Ring(c, rad, tyre, 1.7f * _s);
            for (int i = 0; i < 4; i++)
            {
                var d = M.Dir(WheelAngle + i * MathF.PI / 4) * (rad - 0.6f * _s);
                r.Line(c - d, c + d, new Color4(0.62f, 0.64f, 0.68f, 1), 0.5f * _s);
            }
            r.Disc(c, 1 * _s, Col(6));
        }
        else
        {
            r.Disc(c, rad, tyre);
            r.Disc(c, rad * 0.45f, Col(5));
            r.Line(c, c + M.Dir(WheelAngle) * rad * 0.45f, Col(6), 0.8f * _s);
        }
    }

    /// <summary>Water: ripples, ducks paddling (with ducklings), and a translucent front so swimmers show through.</summary>
    void DrawWater(Renderer r, bool over, float t)
    {
        float sc = Sc;
        bool pond = Def.Key == "pond";
        float half = pond ? 70 : 50, top = pond ? 6.5f : 10;
        if (!over)
        {
            // Little ripples drifting across.
            for (int i = 0; i < 4; i++)
            {
                float x = ((t * (4 + i) + i * 37) % (half * 1.6f)) - half * 0.8f;
                r.Line(Local(x - 4, top - 1.6f), Local(x + 4, top - 1.6f), new Color4(1, 1, 1, 0.45f), 0.6f * sc);
            }
            if (pond) { DrawPondFish(r, t); DrawDucks(r, t); }
            return;
        }
        // The front of the water, in front of anyone in it.
        if (Swimmers <= 0) return;
        var water = new Color4(Color.R * 0.85f, Color.G * 0.9f, Color.B, 0.62f);
        Span<Vector2> band = stackalloc Vector2[20];
        for (int i = 0; i < 10; i++)
        {
            float x = -half + i * half * 2 / 9;
            float wave = MathF.Sin(t * 3 + i * 1.3f) * 0.6f;
            band[i] = Local(x, top + wave);
            band[19 - i] = Local(x, 2);
        }
        r.FillPolygon(band, water);
        for (int i = 0; i < 9; i++) r.Line(band[i], band[i + 1], new Color4(1, 1, 1, 0.55f), 0.7f * sc);
    }

    /// <summary>Shapes under the surface, in the colours of whatever's biting this season.</summary>
    void DrawPondFish(Renderer r, float t)
    {
        var cols = Fishes.PondColours;
        if (cols.Length == 0) return;
        for (int i = 0; i < 3; i++)
        {
            float ph = Id * 1.3f + i * 2.1f, sp = 0.08f + i * 0.025f;
            float x = MathF.Sin(t * sp + ph) * 48;
            float dir = MathF.Cos(t * sp + ph) >= 0 ? 1 : -1;
            if (Flip) dir = -dir;
            var c = cols[(Id + i) % cols.Length].A(0.33f);
            var at = Local(x, 2.4f + i * 0.9f);
            r.Oval(at, 3.4f * Sc, 1.1f * Sc, c);
            r.FillPolygon(stackalloc Vector2[] { at + new Vector2(-dir * 3f * Sc, 0), at + new Vector2(-dir * 5.2f * Sc, -1.2f * Sc), at + new Vector2(-dir * 5.2f * Sc, 1.2f * Sc) }, c);
        }
    }

    void DrawDucks(Renderer r, float t)
    {
        int n = 2 + Id % 3;
        float half = 56;
        for (int i = 0; i < n; i++)
        {
            bool duckling = i >= 1 && n > 2;
            float lag = duckling ? i * 1.6f : i * 23;
            float ph = Id * 0.7f + (duckling ? 0 : i * 2.4f);
            float tt = t - lag;
            float x = MathF.Sin(tt * 0.06f + ph) * half;
            float dir = MathF.Cos(tt * 0.06f + ph) >= 0 ? 1 : -1;
            float bob = MathF.Sin(t * 2.2f + i) * 0.35f;
            float k = duckling ? 0.5f : 1;
            DrawDuck(r, Local(x, 5.4f + bob), dir * (Flip ? -1 : 1), k * Sc, duckling ? 2 : i % 2, t + i);
        }
    }

    static void DrawDuck(Renderer r, Vector2 at, float dir, float u, int kind, float t)
    {
        var ink = new Color4(0.12f, 0.11f, 0.1f, 0.85f);
        Color4 body = kind switch { 0 => M.Hex(0x8D6E63), 1 => M.Hex(0xF5F1E8), _ => M.Hex(0xFFE082) };
        Color4 head = kind switch { 0 => M.Hex(0x2E7D32), 1 => M.Hex(0xF5F1E8), _ => M.Hex(0xFFE082) };
        // Now and then a dip, tail up.
        bool dip = kind != 2 && MathF.Sin(t * 0.37f) > 0.97f;
        Vector2 hd = at + new Vector2(dir * 3.4f * u, dip ? 1.2f * u : -3.2f * u);
        r.Oval(at + new Vector2(0, -1 * u), 4.6f * u, 2.6f * u, ink);
        r.Oval(at + new Vector2(0, -1 * u), 4.1f * u, 2.1f * u, body);
        r.FillPolygon(stackalloc Vector2[] { at + new Vector2(-dir * 3.4f * u, -1.6f * u), at + new Vector2(-dir * 5.6f * u, (dip ? -4 : -2.8f) * u), at + new Vector2(-dir * 3.2f * u, -0.2f * u) }, body);
        if (!dip)
        {
            r.Disc(hd, 1.95f * u, ink);
            r.Disc(hd, 1.55f * u, head);
            r.FillPolygon(stackalloc Vector2[] { hd + new Vector2(dir * 1.2f * u, -0.3f * u), hd + new Vector2(dir * 3 * u, 0.2f * u), hd + new Vector2(dir * 1.2f * u, 0.6f * u) }, M.Hex(0xFB8C00));
            r.Disc(hd + new Vector2(dir * 0.5f * u, -0.5f * u), 0.35f * u, ink);
            if (kind == 0) r.Line(hd + new Vector2(-dir * 0.4f * u, 1.5f * u), hd + new Vector2(dir * 0.6f * u, 1.5f * u), new Color4(1, 1, 1, 0.9f), 0.5f * u);
        }
        // Water line across the body.
        r.Line(at + new Vector2(-5 * u, 0.3f * u), at + new Vector2(5 * u, 0.3f * u), new Color4(1, 1, 1, 0.5f), 0.5f * u);
    }
}
