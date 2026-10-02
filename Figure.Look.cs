using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>Drawing what a figure wears. Head pieces follow the head (tilting with it); clothes follow the bones;
/// capes, scarf tails and ponytails are little verlet chains that swing with movement.</summary>
sealed partial class Figure
{
    Look? _look;
    public Look Look { get => _look ??= Look.Generate(Traits, StyleChoice.Seed, Gender); set => _look = value; }

    readonly Vector2[] _cape = new Vector2[6], _capeOld = new Vector2[6];
    readonly Vector2[] _pony = new Vector2[4], _ponyOld = new Vector2[4];
    readonly Vector2[] _scarf = new Vector2[3], _scarfOld = new Vector2[3];
    bool _clothInit;
    readonly Vector2[] _lookBuf = new Vector2[48];

    bool FrontView => Action is Act.SitFront or Act.SitBack && Grounded && Mode == Mode.Control;

    (Vector2 h, Vector2 up, Vector2 fwd) HeadFrame()
    {
        Vector2 h = Jt[J.Head], up = h - Jt[J.Neck];
        up = up.LengthSquared() > 1e-4f ? Vector2.Normalize(up) : new Vector2(0, -1);
        Vector2 fwd = new Vector2(-up.Y, up.X) * (FrontView ? 1 : Facing);
        return (h, up, fwd);
    }

    static Color4 Hex(string s, Color4 fallback) => s.Length == 7 ? Settings.ParseHex(s) : fallback;

    Color4 PartCol(int c, Color4 main) => c switch
    {
        0 => main,
        1 => M.Shade(main, 0.72f),
        2 => Color4.Lerp(main, new Color4(1, 1, 1, 1), 0.35f),
        _ => ItemDef.Fixed[Math.Clamp(c, 3, ItemDef.Fixed.Length - 1)],
    };

    void DrawShapes(Renderer r, Shape[] shapes, Func<float, float, Vector2> map, float unit, Color4 main, float alpha)
    {
        var ink = new Color4(0.1f, 0.1f, 0.1f, 0.75f * alpha);
        foreach (var sh in shapes)
        {
            var p = sh.P;
            int n = 0;
            void Pt(float x, float y) { if (n < _lookBuf.Length) _lookBuf[n++] = map(x, y); }
            var col = PartCol(sh.Col, main);
            col = new Color4(col.R, col.G, col.B, col.A * alpha);
            switch (sh.Kind)
            {
                case 'r': Pt(p[0], p[1]); Pt(p[2], p[1]); Pt(p[2], p[3]); Pt(p[0], p[3]); break;
                case 'o':
                {
                    float rad = MathF.Min(p[4], MathF.Min(p[2] - p[0], p[3] - p[1]) * 0.5f);
                    void Corner(float cx, float cy, float a0) { for (int i = 0; i <= 3; i++) { float a = a0 + i * MathF.PI / 6; Pt(cx + MathF.Cos(a) * rad, cy + MathF.Sin(a) * rad); } }
                    Corner(p[2] - rad, p[1] + rad, -MathF.PI / 2); Corner(p[2] - rad, p[3] - rad, 0); Corner(p[0] + rad, p[3] - rad, MathF.PI / 2); Corner(p[0] + rad, p[1] + rad, MathF.PI);
                    break;
                }
                case 'e': for (int i = 0; i < 16; i++) { float a = i * MathF.Tau / 16; Pt(p[0] + MathF.Cos(a) * p[2], p[1] + MathF.Sin(a) * p[3]); } break;
                case 'p': for (int i = 0; i + 1 < p.Length; i += 2) Pt(p[i], p[i + 1]); break;
                case 'l': r.Line(map(p[0], p[1]), map(p[2], p[3]), col, sh.W * unit); continue;
                case 'c': for (int i = 0; i + 3 < p.Length; i += 2) r.Line(map(p[i], p[i + 1]), map(p[i + 2], p[i + 3]), col, sh.W * unit); continue;
            }
            if (n < 3) continue;
            r.Polygon(_lookBuf.AsSpan(0, n), col, ink, MathF.Max(0.8f, 0.9f * S));
        }
    }

    /// <summary>Behind the body: cape, long hair/afro, hood, headband tails.</summary>
    public static bool SkipLooks;   // debug: performance bisecting

    void DrawLookBack(Renderer r, float alpha)
    {
        if (SkipLooks) return;
        var look = Look;
        if (look.Back == "cape" && _clothInit) DrawCape(r, Hex(look.BackColour, Color), alpha);
        var (h, up, fwd) = HeadFrame();
        float hr = HeadR;
        Vector2 Map(float x, float y) => h + fwd * x * hr + up * y * hr;
        if (Look.Find(Look.Hairs, look.Hair) is { Back: { } hb } && !(FrontView && Action == Act.SitFront && look.Hair == "long"))
            DrawShapes(r, hb, Map, hr, Hex(look.HairColour, Color), alpha);
        if (look.Top == "hoodie") DrawShapes(r, new[] { new Shape('e', new[] { -0.7f, -0.2f, 0.75f, 0.9f }, 1) }, Map, hr, Hex(look.TopColour, Color), alpha);
        if (Look.Find(Look.Hats, HatOverride ?? look.Hat) is { Back: { } tb }) DrawShapes(r, tb, Map, hr, Hex(HatColourOverride ?? look.HatColour, Color), alpha);
        if (look.Hair == "ponytail" && _clothInit)
        {
            var hc = Hex(look.HairColour, Color);
            hc = new Color4(hc.R, hc.G, hc.B, alpha);
            for (int i = 0; i < _pony.Length - 1; i++) r.Line(_pony[i], _pony[i + 1], hc, (2.6f - i * 0.5f) * S);
        }
        if (look.Neck == "scarf" && _clothInit)
        {
            var sc = Hex(look.NeckColour, Color);
            sc = new Color4(sc.R, sc.G, sc.B, alpha);
            for (int i = 0; i < _scarf.Length - 1; i++) r.Line(_scarf[i], _scarf[i + 1], sc, 2.6f * S);
        }
    }

    /// <summary>Over the torso (after the body lines): top, neckwear, waist.</summary>
    void DrawLookBody(Renderer r, float alpha)
    {
        if (SkipLooks) return;
        var look = Look;
        Vector2 neck = Jt[J.Neck], pel = Jt[J.Pelvis];
        Vector2 down = pel - neck;
        float tl = down.Length();
        Vector2 dn = tl > 1e-3f ? down / tl : new Vector2(0, 1);
        Vector2 fwd = new Vector2(-dn.Y, dn.X) * -(FrontView ? 1 : Facing);
        Color4 A(Color4 c) => new(c.R, c.G, c.B, c.A * alpha);
        if (look.Top.Length > 0)
        {
            var c = A(Hex(look.TopColour, Color));
            var dark = A(M.Shade(Hex(look.TopColour, Color), 0.8f));
            float w = LineW * (look.Top == "tank" ? 1.7f : 2.05f);
            // Sleeves first (far, then near), then the body of the shirt over them.
            if (look.Top != "tank")
            {
                float k = look.Top == "hoodie" ? 1 : 0.55f;
                r.Line(neck, Vector2.Lerp(neck, Jt[J.ElbowF], k), dark, LineW * 1.7f);
                if (look.Top == "hoodie") r.Line(Jt[J.ElbowF], Vector2.Lerp(Jt[J.ElbowF], Jt[J.HandF], 0.85f), dark, LineW * 1.7f);
            }
            r.Line(neck + dn * 1.2f * S, pel + dn * 1.5f * S, c, w);
            if (look.Top != "tank")
            {
                float k = look.Top == "hoodie" ? 1 : 0.55f;
                r.Line(neck, Vector2.Lerp(neck, Jt[J.ElbowN], k), c, LineW * 1.7f);
                if (look.Top == "hoodie") r.Line(Jt[J.ElbowN], Vector2.Lerp(Jt[J.ElbowN], Jt[J.HandN], 0.85f), c, LineW * 1.7f);
            }
        }
        if (look.Waist == "belt")
        {
            Vector2 b = pel - dn * 1.8f * S;
            Vector2 side = FrontView ? new Vector2(1, 0) : fwd;
            r.Line(b - side * 2.8f * S, b + side * 2.8f * S, A(Hex(look.WaistColour, Color)), 1.8f * S);
            r.Disc(b + side * (FrontView ? 0 : 1.6f) * S, 0.9f * S, A(ItemDef.Fixed[15]));
        }
        else if (look.Waist == "skirt")
        {
            var c = A(Hex(look.WaistColour, Color));
            Vector2 a = pel - dn * 2.5f * S;
            Vector2 kN = Vector2.Lerp(pel, Jt[J.KneeN], 0.8f), kF = Vector2.Lerp(pel, Jt[J.KneeF], 0.8f);
            Vector2 left = kN.X < kF.X ? kN : kF, right = kN.X < kF.X ? kF : kN;
            Span<Vector2> sk = stackalloc Vector2[] { a + new Vector2(-2.6f * S, 0), a + new Vector2(2.6f * S, 0), right + new Vector2(2.5f * S, 0), left + new Vector2(-2.5f * S, 0) };
            r.FillPolygon(sk, c);
            var ink = new Color4(0.1f, 0.1f, 0.1f, 0.6f * alpha);
            for (int i = 0; i < 4; i++) r.Line(sk[i], sk[(i + 1) % 4], ink, 0.8f * S);
        }
        if (look.Neck == "tie" && Action != Act.SitBack)
        {
            Vector2 s = neck + dn * 1.5f * S + fwd * 1.2f * S;
            Span<Vector2> tie = stackalloc Vector2[] { s - fwd * 0.9f * S, s + fwd * 0.9f * S, s + dn * 9 * S + fwd * 1.3f * S, s + dn * 11 * S, s + dn * 9 * S - fwd * 0.4f * S };
            r.FillPolygon(tie, A(Hex(look.NeckColour, Color)));
        }
        else if (look.Neck == "bowtie" && Action != Act.SitBack)
        {
            Vector2 s = neck + dn * 1.2f * S + fwd * 1.0f * S;
            Vector2 side = FrontView ? new Vector2(1, 0) : dn;
            Span<Vector2> a = stackalloc Vector2[] { s, s + side * 2.4f * S - new Vector2(0, 1.3f * S), s + side * 2.4f * S + new Vector2(0, 1.3f * S) };
            Span<Vector2> b = stackalloc Vector2[] { s, s - side * 2.4f * S - new Vector2(0, 1.3f * S), s - side * 2.4f * S + new Vector2(0, 1.3f * S) };
            var c = A(Hex(look.NeckColour, Color));
            r.FillPolygon(a, c); r.FillPolygon(b, c);
        }
        else if (look.Neck == "scarf")
            r.Line(neck - fwd * 2.2f * S + dn * 0.6f * S, neck + fwd * 2.2f * S + dn * 0.6f * S, A(Hex(look.NeckColour, Color)), 2.4f * S);
    }

    /// <summary>On the head (after the head disc): hair, beard, glasses, hat. And shoes on the feet.</summary>
    void DrawLookFront(Renderer r, float alpha)
    {
        if (SkipLooks) return;
        var look = Look;
        var (h, up, fwd) = HeadFrame();
        float hr = HeadR;
        bool back = Action == Act.SitBack && FrontView, front = FrontView && !back;
        Vector2 Map(float x, float y) => h + fwd * x * hr + up * y * hr;
        // Seen from the front, face pieces sit in the middle of the face.
        Vector2 Face(float x, float y) => front ? h + fwd * (x - 0.62f) * hr + up * y * hr : Map(x, y);
        if (Look.Find(Look.Hairs, look.Hair) is { } hair) DrawShapes(r, hair.Front, Map, hr, Hex(look.HairColour, Color), alpha);
        if (!back && Look.Find(Look.Beards, look.Beard) is { } beard) DrawShapes(r, beard.Front, Face, hr, Hex(look.HairColour, Color), alpha);
        if (!back && Look.Find(Look.GlassesParts, look.Glasses) is { } gl)
        {
            if (front)
            {
                // Both lenses, mirrored.
                DrawShapes(r, gl.Front.Where(s => s.Kind != 'l').ToArray(), (x, y) => h + fwd * (x - 0.62f + 0.36f) * hr + up * y * hr, hr, Color, alpha);
                DrawShapes(r, gl.Front.Where(s => s.Kind != 'l').ToArray(), (x, y) => h + fwd * (0.62f - x - 0.36f) * hr + up * y * hr, hr, Color, alpha);
            }
            else DrawShapes(r, gl.Front, Map, hr, Color, alpha);
        }
        if (Look.Find(Look.Hats, HatOverride ?? look.Hat) is { } hat) DrawShapes(r, hat.Front, Map, hr, Hex(HatColourOverride ?? look.HatColour, Color), alpha);
        if (Look.Find(Look.ShoeParts, look.Shoes) is { } shoe)
        {
            var sc = Hex(look.ShoeColour, Color);
            foreach (int j in new[] { J.FootF, J.FootN })
            {
                Vector2 foot = Jt[j];
                float dir = FrontView ? (j == J.FootN ? 1 : -1) * 0.25f : Facing;
                DrawShapes(r, shoe.Front, (x, y) => foot + new Vector2(x * dir * S - (FrontView ? 0 : Facing * 0.5f * S), -y * S + 1.2f * S), S, j == J.FootF ? M.Shade(sc, 0.8f) : sc, alpha);
            }
        }
    }

    // ---------------- swinging bits ----------------

    void StepCloth(float dt)
    {
        var look = Look;
        bool cape = look.Back == "cape", tail = look.Hair == "ponytail", scarf = look.Neck == "scarf";
        if (!cape && !tail && !scarf) return;
        var (h, up, fwd) = HeadFrame();
        Vector2 neck = Jt[J.Neck];
        Vector2 back = -fwd;
        Vector2 capeA = neck + back * 1.5f * S, tailA = h + fwd * -0.95f * HeadR + up * 0.45f * HeadR, scarfA = neck + back * 2 * S + new Vector2(0, 1.2f * S);
        if (!_clothInit)
        {
            for (int i = 0; i < _cape.Length; i++) _cape[i] = _capeOld[i] = capeA + new Vector2(0, i * 4.5f * S);
            for (int i = 0; i < _pony.Length; i++) _pony[i] = _ponyOld[i] = tailA + new Vector2(0, i * 2.4f * S);
            for (int i = 0; i < _scarf.Length; i++) _scarf[i] = _scarfOld[i] = scarfA + new Vector2(0, i * 4 * S);
            _clothInit = true;
        }
        Vector2 g = new(0, Gravity * 0.35f * dt * dt);
        void Chain(Vector2[] p, Vector2[] o, Vector2 anchor, float seg, float drag)
        {
            o[0] = p[0]; p[0] = anchor;
            for (int i = 1; i < p.Length; i++)
            {
                Vector2 v = (p[i] - o[i]) * drag;
                o[i] = p[i];
                p[i] += v + g;
            }
            for (int it = 0; it < 4; it++)
                for (int i = 0; i < p.Length - 1; i++)
                {
                    Vector2 d = p[i + 1] - p[i];
                    float l = d.Length();
                    if (l < 1e-4f) continue;
                    Vector2 c = d * ((l - seg) / l);
                    if (i == 0) p[i + 1] -= c; else { p[i] += c * 0.5f; p[i + 1] -= c * 0.5f; }
                }
        }
        if (cape) Chain(_cape, _capeOld, capeA, 4.5f * S, 0.95f);
        if (tail) Chain(_pony, _ponyOld, tailA, 2.4f * S, 0.93f);
        if (scarf) Chain(_scarf, _scarfOld, scarfA, 4 * S, 0.94f);
    }

    void DrawCape(Renderer r, Color4 c, float alpha)
    {
        int n = _cape.Length;
        Span<Vector2> band = stackalloc Vector2[n * 2];
        for (int i = 0; i < n; i++)
        {
            Vector2 d = i < n - 1 ? _cape[i + 1] - _cape[i] : _cape[i] - _cape[i - 1];
            Vector2 perp = d.LengthSquared() > 1e-4f ? Vector2.Normalize(new Vector2(-d.Y, d.X)) : new Vector2(1, 0);
            float w = (2 + i * 1.1f) * S;
            band[i] = _cape[i] + perp * w;
            band[n * 2 - 1 - i] = _cape[i] - perp * w;
        }
        r.FillPolygon(band, new Color4(c.R, c.G, c.B, alpha));
        var ink = new Color4(0.1f, 0.1f, 0.1f, 0.55f * alpha);
        for (int i = 0; i < band.Length; i++) r.Line(band[i], band[(i + 1) % band.Length], ink, 0.8f * S);
    }
}
