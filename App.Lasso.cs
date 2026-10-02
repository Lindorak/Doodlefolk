using System.Numerics;
using Vortice.Mathematics;
using static StickFight.Native;

namespace StickFight;

/// <summary>Lassos: drawing the ropes, and the cursor ride: a figure that ropes your cursor spins it round a couple of
/// times and flings it across the screen. Only when you've left the mouse alone for a few seconds; the moment you
/// move it yourself, it breaks free. (A setting: Figures can lasso your cursor.)</summary>
sealed partial class App
{
    double _grabStart = -1;
    Vector2 _grabSet, _flingVel;
    float _grabAngle;
    Vector2 _lastCursorSample;

    void LassoFrame(double now, float dt)
    {
        // How long the cursor has been still (only then may it be roped).
        if (Vector2.Distance(_w.Cursor, _lastCursorSample) > 3) { _w.CursorStill = 0; _lastCursorSample = _w.Cursor; }
        else _w.CursorStill += dt;
        World.LassoCursor = _settings.LassoCursor;

        var by = _w.CursorLasso;
        if (by == null) { _grabStart = -1; return; }
        bool unsafeNow = !_settings.LassoCursor || World.Calm || Control.MouseButtons != MouseButtons.None || _w.Env.FullscreenActive || _fakeCursor != null || !_w.Figures.Contains(by) || by.Mode != Mode.Control;
        if (_grabStart < 0)
        {
            if (unsafeNow) { EndCursorLasso(by, true); return; }
            _grabStart = now;
            _grabSet = _w.Cursor;
            Vector2 c0 = by.Jt[J.HandN];
            _grabAngle = MathF.Atan2(_w.Cursor.Y - c0.Y, _w.Cursor.X - c0.X);
            return;
        }
        // You took the mouse back.
        GetCursorPos(out var p);
        if (unsafeNow || Vector2.Distance(new Vector2(p.X, p.Y), _grabSet) > 35) { EndCursorLasso(by, true); return; }
        float t = (float)(now - _grabStart), s = by.S;
        Vector2 centre = by.Jt[J.HandN] + new Vector2(0, -by.Torso * 0.8f);
        Vector2 pos;
        if (t < 1.9f)
        {
            // Round and round (faster, and wider).
            _grabAngle += dt * (4 + t * 3.5f);
            float rad = (50 + t * 45) * s;
            pos = centre + new Vector2(MathF.Cos(_grabAngle) * rad, MathF.Sin(_grabAngle) * rad * 0.55f);
            if ((int)(t * 4) != (int)((t - dt) * 4)) World.Play(Sfx.Whoosh, centre, 0.2f, 1.6f, 0.1);
        }
        else if (t < 2.5f)
        {
            if (_flingVel == Vector2.Zero)
            {
                // Let go along the swing.
                _flingVel = new Vector2(-MathF.Sin(_grabAngle), MathF.Cos(_grabAngle) * 0.55f) * 2600 * s;
                if (MathF.Abs(_flingVel.Y) > MathF.Abs(_flingVel.X)) _flingVel = new Vector2(_flingVel.X * 2.4f + by.Facing * 900 * s, _flingVel.Y * 0.4f);
                World.Play(Sfx.Swish, centre, 0.45f, 1.2f);
            }
            pos = _grabSet + _flingVel * dt;
            _flingVel *= MathF.Pow(0.05f, dt);
        }
        else { EndCursorLasso(by, false); return; }
        var v = _w.Env.Virtual;
        int x = Math.Clamp((int)MathF.Round(pos.X), v.Left, v.Right - 1), y = Math.Clamp((int)MathF.Round(pos.Y), v.Top, v.Bottom - 1);
        SetCursorPos(x, y);
        _grabSet = new Vector2(x, y);
    }

    void EndCursorLasso(Figure by, bool brokeFree)
    {
        _w.CursorLasso = null;
        _grabStart = -1;
        _flingVel = Vector2.Zero;
        if (_w.Figures.Contains(by)) by.Brain.CursorLassoOver(brokeFree);
    }

    RectangleF? LassoRect()
    {
        RectangleF? all = null;
        foreach (var f in _w.Figures)
        {
            if (!f.Brain.LassoRope(out var hand, out var end, out _, out _)) continue;
            float pad = 40 * f.S;
            var r = RectangleF.FromLTRB(MathF.Min(hand.X, end.X) - pad, MathF.Min(hand.Y, end.Y) - pad - f.Torso, MathF.Max(hand.X, end.X) + pad, MathF.Max(hand.Y, end.Y) + pad);
            if (_w.CursorLasso == f) r = RectangleF.Union(r, RectangleF.FromLTRB(_w.Cursor.X - 300 * f.S, _w.Cursor.Y - 200 * f.S, _w.Cursor.X + 300 * f.S, _w.Cursor.Y + 200 * f.S));
            all = all is { } a ? RectangleF.Union(a, r) : r;
        }
        return all;
    }

    void DrawLassos()
    {
        if (LassoRect() is not RectangleF b || !Dirty(b)) return;
        var rope = new Color4(0.76f, 0.6f, 0.36f, 1);
        var ink = new Color4(0.25f, 0.18f, 0.1f, 0.9f);
        foreach (var f in _w.Figures)
        {
            if (!f.Brain.LassoRope(out var hand, out var end, out int phase, out float spin)) continue;
            float s = f.S;
            void Loop(Vector2 c, float rx, float ry, float rot)
            {
                Vector2 prev = default;
                for (int i = 0; i <= 14; i++)
                {
                    float a = i / 14f * MathF.Tau + rot;
                    var q = c + new Vector2(MathF.Cos(a) * rx, MathF.Sin(a) * ry);
                    if (i > 0) { _r.Line(prev, q, ink, 2.2f * s); _r.Line(prev, q, rope, 1.3f * s); }
                    prev = q;
                }
            }
            if (phase == 0)
            {
                var c = hand + new Vector2(0, -14 * s);
                Loop(c, 13 * s * (0.8f + 0.2f * MathF.Sin(spin)), 4 * s, spin);
                _r.Line(hand, c + new Vector2(MathF.Cos(spin) * 13 * s, MathF.Sin(spin) * 4 * s), rope, 1.3f * s);
                continue;
            }
            Vector2 tgt = _w.CursorLasso == f ? _w.Cursor : end;
            _r.Line(hand, tgt, ink, 2.2f * s);
            _r.Line(hand, tgt, rope, 1.3f * s);
            Loop(tgt, 7 * s, phase == 1 ? 5 * s : 3 * s, spin * 0.3f);
        }
    }
}
