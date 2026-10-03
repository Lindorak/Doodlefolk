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

    bool AnimatingWorld => Rider != null || IsWater || (Def.Key == "fairylights" && LightsOn > 0.01f);

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

    /// <summary>The water's surface: level with the edge of the window it's on (its depth goes on down out of sight).</summary>
    public float SurfaceY => Pos.Y - 0.8f * Sc;

    /// <summary>Half the open water, in the object's own units (between the banks / the pool's edges).</summary>
    float WaterHalf => Def.Key == "pond" ? 63 : 51;

    /// <summary>A point in the object's units, where the ends (banks, ladder, diving board) keep their own size when
    /// the water is stretched wider: only the open water in between gets longer.</summary>
    Vector2 EndLocal(float x, float y)
    {
        float inner = WaterHalf, ax = MathF.Abs(x);
        float X = ax <= inner ? x * ScaleX : MathF.Sign(x) * (inner * ScaleX + (ax - inner));
        return Pos + new Vector2((Flip ? -X : X) * Sc, -y * Sc);
    }

    /// <summary>Water seen from the side: a wavy, glinting surface with ripples drifting along it, a hint of its depth
    /// just under the window's edge, and around it a pond's banks, reeds, lily pads, ducks and leaping fish, or a pool's
    /// tiled edges, ladder, diving board and lane rope. Anyone swimming is drawn only above the surface, and while
    /// someone's in, the surface is drawn again in front of them.</summary>
    void DrawWater(Renderer r, bool over, float t)
    {
        float sc = Sc, half = WaterHalf, surf = SurfaceY;
        bool pond = Def.Key == "pond";
        var deep = new Color4(Color.R * 0.7f, Color.G * 0.82f, Color.B * 0.95f, 1);
        if (over)
        {
            if (Swimmers > 0) DrawSurface(r, t, half, deep, front: true);
            return;
        }
        // A hint of depth: the water's colour just showing under the window's edge, fading straight away.
        Span<Vector2> q = stackalloc Vector2[4];
        q[0] = EndLocal(-half, 0); q[1] = EndLocal(half, 0); q[2] = EndLocal(half - 2, -5); q[3] = EndLocal(-half + 2, -5);
        r.FillPolygon(q, deep.A(0.12f));
        DrawSurface(r, t, half, deep, front: false);
        if (pond) DrawPondEdges(r, t); else DrawPoolEdges(r, t);
        // Things on (and leaping out of) the water, cut off at the surface.
        r.PushAbove(surf + 0.15f * sc);
        if (pond) { DrawLeapingFish(r, t); DrawDucks(r, t); }
        r.PopClip();
    }

    void DrawSurface(Renderer r, float t, float half, Color4 deep, bool front)
    {
        float sc = Sc;
        const int n = 24;
        Span<Vector2> band = stackalloc Vector2[n * 2];
        for (int i = 0; i < n; i++)
        {
            float x = -half + i * half * 2 / (n - 1);
            float wave = (MathF.Sin(t * 2.1f + x * 0.21f) * 0.32f + MathF.Sin(t * 3.3f - x * 0.13f) * 0.18f) * (front ? 1.2f : 1);
            if (Swimmers > 0) wave *= 1.6f;
            band[i] = EndLocal(x, 0.8f + wave);
            band[n * 2 - 1 - i] = EndLocal(x, front ? 0.1f : -0.4f);
        }
        // The water at the edge, with a bright line where the light catches the top.
        r.FillPolygon(band, new Color4(Color.R, Color.G, Color.B, front ? 0.55f : 0.9f));
        for (int i = 0; i + 1 < n; i++) r.Line(band[i], band[i + 1], new Color4(1, 1, 1, front ? 0.5f : 0.75f), 0.55f * sc);
        if (front) return;
        for (int i = 0; i + 1 < n; i++) r.Line(band[n * 2 - 1 - i], band[n * 2 - 2 - i], deep.A(0.55f), 0.5f * sc);
        // Ripples drifting along, and glints that come and go.
        for (int i = 0; i < 5 + (int)(ScaleX * 2); i++)
        {
            float span = half * 1.8f;
            float x = ((t * (3.5f + i * 0.7f) + i * 41) % span) - span / 2;
            float y = 0.9f + MathF.Sin(t * 2.1f + x * 0.21f) * 0.32f;
            var a = EndLocal(x - 3, y + 0.5f); var m = EndLocal(x, y + 1.1f); var b = EndLocal(x + 3, y + 0.5f);
            r.Line(a, m, new Color4(1, 1, 1, 0.42f), 0.45f * sc); r.Line(m, b, new Color4(1, 1, 1, 0.42f), 0.45f * sc);
        }
        for (int i = 0; i < 6; i++)
        {
            float tw = MathF.Sin(t * (1.7f + i * 0.31f) + i * 2.3f + Id);
            if (tw < 0.55f) continue;
            float x = MathF.Sin(i * 12.9898f + Id) * half * 0.9f;
            var at = EndLocal(x, 1.1f + MathF.Sin(t * 2.1f + x * 0.21f) * 0.32f);
            r.Line(at - new Vector2(1.2f * sc, 0), at + new Vector2(1.2f * sc, 0), new Color4(1, 1, 1, (tw - 0.55f) * 2), 0.5f * sc);
        }
    }

    void DrawPondEdges(Renderer r, float t)
    {
        float sc = Sc;
        var grass = M.Hex(0x7CB342); var grassDark = M.Hex(0x558B2F); var mud = M.Hex(0x8D6E63);
        var stone = M.Hex(0x9AA3AD); var stoneDark = M.Hex(0x6B7480); var reed = M.Hex(0x689F38); var cat = M.Hex(0x6D4C41);
        Span<Vector2> bank = stackalloc Vector2[8];
        float[] bx = { 63, 64.5f, 67, 70.5f, 74, 77, 78, 63 }, by = { 0, 1.4f, 2.8f, 3.4f, 2.9f, 1.4f, 0, 0 };
        foreach (int side in new[] { -1, 1 })
        {
            // A grassy bank with a muddy edge and a couple of stones at the water.
            for (int i = 0; i < 8; i++) bank[i] = EndLocal(side * bx[i], by[i]);
            r.FillPolygon(bank, Ink);
            for (int i = 0; i < 8; i++) bank[i] = EndLocal(side * (bx[i] + (i is 0 or 7 ? 0.6f : 0)), MathF.Max(0, by[i] - 0.45f));
            r.FillPolygon(bank, grass);
            r.Line(EndLocal(side * 64, 0.5f), EndLocal(side * 76.5f, 0.5f), mud.A(0.8f), 0.9f * sc);
            foreach (var (x, rx, ry) in new[] { (65.2f, 1.9f, 1.2f), (68.4f, 1.4f, 0.9f) })
            {
                var c = EndLocal(side * x, 1.0f);
                r.Oval(c, (rx + 0.35f) * sc, (ry + 0.35f) * sc, Ink);
                r.Oval(c, rx * sc, ry * sc, stone);
                r.Oval(c + new Vector2(0, 0.35f * sc), rx * 0.8f * sc, ry * 0.45f * sc, stoneDark.A(0.6f));
            }
            for (int i = 0; i < 4; i++)
            {
                var b = EndLocal(side * (69 + i * 2.2f), 2.6f);
                for (int k = -1; k <= 1; k++) r.Line(b, b + new Vector2((k * 0.9f + MathF.Sin(t * 1.3f + i) * 0.3f) * sc, -(2.2f + (k == 0 ? 1 : 0)) * sc), grassDark, 0.5f * sc);
            }
        }
        // Reeds and bulrushes, swaying.
        foreach (var (x, h, head) in new[] { (-66.5f, 16f, false), (-68.5f, 23f, true), (-70.5f, 19f, true), (-72.5f, 14f, false), (67.5f, 15f, true), (69.5f, 11f, false) })
        {
            float sway = MathF.Sin(t * 0.9f + x * 0.37f) * (h * 0.06f);
            var b = EndLocal(x, 1.2f); var top = EndLocal(x + sway, h);
            var mid = Vector2.Lerp(b, top, 0.55f) + new Vector2(sway * 0.3f * sc, 0);
            r.Line(b, mid, reed, 1.0f * sc); r.Line(mid, top, reed, 0.8f * sc);
            if (head)
            {
                var hc = Vector2.Lerp(mid, top, 0.72f);
                r.Oval(hc, 1.15f * sc, 2.9f * sc, Ink);
                r.Oval(hc, 0.85f * sc, 2.6f * sc, cat);
            }
            else r.Line(mid, mid + new Vector2((x < 0 ? -3.5f : 3.5f) * sc, -2.5f * sc), reed, 0.6f * sc);
        }
        // Lily pads floating on the surface (one in flower).
        foreach (var (x, w, flower) in new[] { (-40f, 6.5f, true), (-31f, 4f, false), (33f, 5.5f, false) })
        {
            float bob = MathF.Sin(t * 1.4f + x) * 0.15f;
            var c = Local(x, 1.0f + bob);
            r.Oval(c, (w + 0.4f) * sc, 1.25f * sc, Ink);
            r.Oval(c, w * sc, 0.95f * sc, M.Hex(0x66BB6A));
            r.Line(c, c + new Vector2(w * 0.7f * sc, -0.3f * sc), M.Hex(0x43A047), 0.4f * sc);
            if (flower)
            {
                var fc = c + new Vector2(-1.5f * sc, -1.3f * sc);
                for (int k = 0; k < 5; k++) r.Oval(fc + new Vector2((k - 2) * 0.75f * sc, (MathF.Abs(k - 2) * 0.35f - 0.3f) * sc), 0.75f * sc, 1.25f * sc, M.Hex(0xF8BBD0));
                r.Disc(fc + new Vector2(0, -0.2f * sc), 0.6f * sc, M.Hex(0xFDD835));
            }
        }
    }

    void DrawPoolEdges(Renderer r, float t)
    {
        float sc = Sc;
        var tile = M.Hex(0xF7F5EF); var grout = M.Hex(0xB0BEC5); var steel = M.Hex(0xCFD8DC); var steelDark = M.Hex(0x78909C);
        Span<Vector2> cope = stackalloc Vector2[4];
        foreach (int side in new[] { -1, 1 })
        {
            // Tiled coping at each end.
            cope[0] = EndLocal(side * 51, 0); cope[1] = EndLocal(side * 58, 0); cope[2] = EndLocal(side * 58, 2.6f); cope[3] = EndLocal(side * 51, 2.6f);
            r.FillPolygon(cope, Ink);
            cope[0] = EndLocal(side * 51.4f, 0); cope[1] = EndLocal(side * 57.6f, 0); cope[2] = EndLocal(side * 57.6f, 2.2f); cope[3] = EndLocal(side * 51.4f, 2.2f);
            r.FillPolygon(cope, tile);
            for (float x = 53.5f; x < 57.5f; x += 2.1f) r.Line(EndLocal(side * x, 0.2f), EndLocal(side * x, 2.1f), grout, 0.35f * sc);
            r.Line(EndLocal(side * 51.4f, 1.1f), EndLocal(side * 57.6f, 1.1f), grout, 0.3f * sc);
            r.Line(EndLocal(side * 51.2f, 2.4f), EndLocal(side * 57.8f, 2.4f), M.Hex(0x4FC3F7).A(0.8f), 0.5f * sc);
        }
        // The ladder: two steel rails curving over the edge and down into the water.
        foreach (var (dx, w, col) in new[] { (1.2f, 1.0f, steelDark), (0f, 1.35f, steel) })
        {
            Vector2 P(float x, float y) => EndLocal(x + dx, y);
            var pts = new[] { P(45.5f, 0.2f), P(45.5f, 9.5f), P(46.6f, 12.4f), P(49.3f, 13.2f), P(51.8f, 11.6f), P(52.6f, 8.6f), P(52.8f, 2.6f) };
            for (int i = 0; i + 1 < pts.Length; i++) r.Line(pts[i], pts[i + 1], Ink, (w + 0.6f) * sc);
            for (int i = 0; i + 1 < pts.Length; i++) r.Line(pts[i], pts[i + 1], col, w * sc);
        }
        // A diving board at the other end, springing while someone's in.
        float spring = MathF.Sin(t * 5) * 0.12f * (Swimmers > 0 ? 1 : 0.2f);
        Span<Vector2> stand = stackalloc Vector2[4];
        stand[0] = EndLocal(-57.5f, 2.6f); stand[1] = EndLocal(-53.5f, 2.6f); stand[2] = EndLocal(-54f, 6.2f); stand[3] = EndLocal(-57f, 6.2f);
        r.FillPolygon(stand, steelDark);
        var b0 = EndLocal(-58, 6.2f); var b1 = EndLocal(-41, 6.6f + spring * 6);
        r.Line(b0, b1, Ink, 2.2f * sc);
        r.Line(b0, b1, M.Hex(0x29B6F6), 1.5f * sc);
        r.Line(b0 + new Vector2(0, -0.35f * sc), b1 + new Vector2(0, -0.35f * sc), new Color4(1, 1, 1, 0.6f), 0.35f * sc);
        // The lane rope floating along the surface.
        float half = WaterHalf;
        for (float x = -half + 3; x < half - 2; x += 3.2f)
        {
            float y = 1.0f + MathF.Sin(t * 2.1f + x * 0.21f) * 0.32f;
            var c = Local(x, y);
            int k = (int)MathF.Round((x + half) / 3.2f);
            r.Oval(c, 1.3f * sc, 0.75f * sc, Ink);
            r.Oval(c, 1.0f * sc, 0.5f * sc, k % 4 == 0 ? M.Hex(0xE53935) : k % 2 == 0 ? M.Hex(0x1E88E5) : M.Hex(0xF7F5EF));
        }
    }

    /// <summary>Fish in the pond show themselves only when they leap: an arc out of the water and back in, with a splash
    /// each way, in the colours of whatever's biting this season.</summary>
    void DrawLeapingFish(Renderer r, float t)
    {
        var cols = Fishes.PondColours;
        if (cols.Length == 0) return;
        float sc = Sc;
        Span<Vector2> body = stackalloc Vector2[8];
        float[] fx = { 3.4f, 2.2f, 0, -2.4f, -3.2f, -2.4f, 0, 2.2f }, fy = { 0, -1.1f, -1.35f, -0.9f, 0, 0.9f, 1.35f, 1.1f };
        for (int i = 0; i < 2; i++)
        {
            float period = 7.5f + i * 3.7f, len = 0.85f;
            float cyc = (t + Id * 1.7f + i * 4.1f) / period;
            float ph = (cyc - MathF.Floor(cyc)) * period;
            if (ph > len + 0.5f) continue;
            int n = (int)MathF.Floor(cyc);
            float h = MathF.Abs(MathF.Sin(n * 78.233f + i * 12.9898f + Id)) % 1;
            float x0 = (h * 2 - 1) * WaterHalf * 0.75f, dir = (n + i) % 2 == 0 ? 1 : -1, size = 0.8f + 0.4f * ((h * 7) % 1);
            if (ph <= len)
            {
                float p = ph / len;
                var at = Local(x0 + dir * 14 * p, 0.8f + 11 * size * MathF.Sin(MathF.PI * p));
                float ang = MathF.Atan2(-MathF.Cos(MathF.PI * p) * 11 * size * MathF.PI, dir * 14 * (Flip ? -1 : 1));
                var c = cols[(Id + i + n) % cols.Length];
                var rot = Matrix3x2.CreateRotation(ang);
                for (int k = 0; k < 8; k++) body[k] = at + Vector2.Transform(new Vector2(fx[k] * size * sc, fy[k] * size * sc), rot);
                r.FillPolygon(body, Ink);
                for (int k = 0; k < 8; k++) body[k] = at + Vector2.Transform(new Vector2(fx[k] * 0.82f * size * sc, fy[k] * 0.78f * size * sc), rot);
                r.FillPolygon(body, c);
                var tail = at + Vector2.Transform(new Vector2(-3.2f * size * sc, 0), rot);
                r.FillPolygon(stackalloc Vector2[] { tail, tail + Vector2.Transform(new Vector2(-2.2f * size * sc, -1.6f * size * sc), rot), tail + Vector2.Transform(new Vector2(-2.2f * size * sc, 1.6f * size * sc), rot) }, c);
                r.Disc(at + Vector2.Transform(new Vector2(1.9f * size * sc, -0.35f * size * sc), rot), 0.32f * size * sc, Ink);
            }
            // A splash where it leaves the water and where it goes back in.
            foreach (var (when, x) in new[] { (0f, x0), (len, x0 + dir * 14) })
            {
                float age = ph - when;
                if (age < 0 || age > 0.45f) continue;
                float k = age / 0.45f;
                var s = Local(x, 0.8f);
                for (int d = -2; d <= 2; d++)
                    r.Disc(s + new Vector2(d * (1 + k * 2.2f) * sc, -(MathF.Sin(MathF.PI * k) * (3 - MathF.Abs(d) * 0.8f)) * sc), (0.6f - k * 0.3f) * sc, new Color4(1, 1, 1, 0.8f * (1 - k)));
            }
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
            DrawDuck(r, Local(x, 0.8f + 0.3f * k + bob), dir * (Flip ? -1 : 1), k * Sc, duckling ? 2 : i % 2, t + i);
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
