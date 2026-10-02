using System.Numerics;

namespace Doodlefolk;

/// <summary>Resizing things like windows (the fish tank): drag a side edge to make it wider or narrower, the top edge
/// to make it taller or shorter, or a top corner for both. The bottom stays on whatever it's standing on.</summary>
sealed partial class App
{
    Item? _resizeIt;
    string _resizeEdge = "";
    Vector2 _resizeFrom;
    float _rsW, _rsH, _rsLeft, _rsRight;
    const float EdgeGrip = 9;

    /// <summary>Which edge or corner of a resizable thing the cursor is over (W, E, N, NW, NE), if any.</summary>
    (Item it, string edge)? ResizeEdge(Vector2 c)
    {
        foreach (var it in _w.Items)
        {
            if (!it.Resizable || !it.Free || !it.OnGround) continue;
            float w = it.Def.W * it.Sc * it.ScaleX, h = it.Def.H * it.Sc * it.ScaleY;
            float left = it.Pos.X - w / 2, right = it.Pos.X + w / 2, top = it.Pos.Y - h, bottom = it.Pos.Y;
            bool nearL = MathF.Abs(c.X - left) < EdgeGrip, nearR = MathF.Abs(c.X - right) < EdgeGrip, nearT = MathF.Abs(c.Y - top) < EdgeGrip;
            bool inY = c.Y > top - EdgeGrip && c.Y < bottom, inX = c.X > left - EdgeGrip && c.X < right + EdgeGrip;
            if (nearT && nearL) return (it, "NW");
            if (nearT && nearR) return (it, "NE");
            if (nearL && inY) return (it, "W");
            if (nearR && inY) return (it, "E");
            if (nearT && inX) return (it, "N");
        }
        return null;
    }

    static Cursor CursorFor(string edge) => edge switch { "W" or "E" => Cursors.SizeWE, "N" => Cursors.SizeNS, "NW" => Cursors.SizeNWSE, _ => Cursors.SizeNESW };

    /// <summary>Hovering: show the resize cursor over an edge. True if the cursor is over one (so clicks come to us).</summary>
    bool ResizeHover(Vector2 c)
    {
        if (_resizeIt != null) { _overlay.CursorOverride = CursorFor(_resizeEdge); return true; }
        var hit = _pressFig == null && _pressProp == null && _pressItem == null && _pressPet == null ? ResizeEdge(c) : null;
        _overlay.CursorOverride = hit is { } h ? CursorFor(h.edge) : null;
        return hit != null;
    }

    bool StartResize(Vector2 c)
    {
        if (ResizeEdge(c) is not { } hit) return false;
        _resizeIt = hit.it;
        _resizeEdge = hit.edge;
        _resizeFrom = c;
        _rsW = hit.it.Def.W * hit.it.Sc * hit.it.ScaleX;
        _rsH = hit.it.Def.H * hit.it.Sc * hit.it.ScaleY;
        _rsLeft = hit.it.Pos.X - _rsW / 2;
        _rsRight = hit.it.Pos.X + _rsW / 2;
        return true;
    }

    void ResizeStep(Vector2 c)
    {
        var it = _resizeIt;
        if (it == null) return;
        if ((Control.MouseButtons & MouseButtons.Left) == 0 || !_w.Items.Contains(it)) { _resizeIt = null; _overlay.CursorOverride = null; return; }
        float dx = c.X - _resizeFrom.X, dy = c.Y - _resizeFrom.Y;
        float baseW = it.Def.W * it.Sc, baseH = it.Def.H * it.Sc;
        if (_resizeEdge.Contains('W') || _resizeEdge.Contains('E'))
        {
            float w = Math.Clamp(_resizeEdge.Contains('W') ? _rsW - dx : _rsW + dx, baseW * 0.6f, baseW * 4);
            it.ScaleX = w / baseW;
            it.Pos.X = _resizeEdge.Contains('W') ? _rsRight - w / 2 : _rsLeft + w / 2;
        }
        if (_resizeEdge.Contains('N'))
        {
            float h = Math.Clamp(_rsH - dy, baseH * 0.6f, baseH * 3);
            it.ScaleY = h / baseH;
        }
    }
}
