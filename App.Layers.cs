namespace Doodlefolk;

/// <summary>Things sitting still (most of the furniture, most of the time) are drawn once into two cached layers, one
/// under the figures and one for the bits in front of them (chair backs, blankets), and each frame just copies the
/// patch it needs. Anything moving, animated, carried or being hidden behind is drawn live. The layers are rebuilt
/// whenever one of the still things changes.</summary>
sealed partial class App
{
    readonly HashSet<Item> _static = new();
    int _layerKey;

    bool IsStatic(Item it) => _static.Contains(it);

    void UpdateLayers()
    {
        _static.Clear();
        int key = HashCode.Combine(Gfx.Q.Shadows, Gfx.Q.DropShadows, Gfx.Q.Shading, Gfx.Q.DetailedArt, Ui.Chalk, _r.Bounds, _skipItems);
        bool hiding = _w.Game is { Kind: GameKind.HideSeek };
        if (!hiding)
            foreach (var it in _w.Items)
                if (it.Holder == null && !it.Animating) { _static.Add(it); key = HashCode.Combine(key, it.Id, it.StateKey()); }
        if (key == _layerKey) return;
        _layerKey = key;
        var saved = _clip;
        _clip = new RectangleF(-1e7f, -1e7f, 2e7f, 2e7f);
        double t = _clock.Elapsed.TotalSeconds;
        // Where each still thing reaches: its outline plus room for its shadow and any glow.
        var reach = _static.ToDictionary(it => it, it => { var b = it.Bounds(); float pad = 90 * _w.Scale * MathF.Max(1, it.SizeMul); b.Inflate(pad, pad); return b; });
        var overAreas = new List<RectangleF>();   // occupied things (the only ones with parts in front) are drawn live
        _r.BuildLayers(tile =>
        {
            var here = _static.Where(it => reach[it].IntersectsWith(tile)).ToList();
            if (here.Count == 0) return;
            foreach (var it in here)
            {
                it.Shadow(_w.Env, out var ic, out float irx, out float iry, out float ia);
                if (ia > 0) Gfx.GroundShadow(_r, ic, irx, iry, ia);
            }
            if (Gfx.Q.DropShadows)
            {
                _r.BeginShadowLayer(Gfx.DropOpacity);
                foreach (var it in here) { _r.PushAbove(GroundUnder(it.Pos)); it.DrawDropShadow(_r); _r.PopClip(); }
                _r.EndShadowLayer();
            }
            if (!_skipItems) foreach (var it in here) it.Draw(_r, false, t);
            DrawHomeFlags(onlyStatic: true);
        }, reach.Values.ToList(), tile =>
        {
            if (!_skipItems) foreach (var it in _static) if (reach[it].IntersectsWith(tile)) it.Draw(_r, true, t);
        }, overAreas);
        _clip = saved;
        ForceFullRedraw();
    }
}
