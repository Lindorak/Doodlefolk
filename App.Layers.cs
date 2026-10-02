namespace StickFight;

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
        _r.BuildLayers(() =>
        {
            foreach (var it in _static)
            {
                it.Shadow(_w.Env, out var ic, out float irx, out float iry, out float ia);
                if (ia > 0) Gfx.GroundShadow(_r, ic, irx, iry, ia);
            }
            if (Gfx.Q.DropShadows)
            {
                _r.BeginShadowLayer(Gfx.DropOpacity);
                foreach (var it in _static) it.DrawDropShadow(_r);
                _r.EndShadowLayer();
            }
            if (!_skipItems) foreach (var it in _static) it.Draw(_r, false, t);
            DrawHomeFlags(onlyStatic: true);
        }, () =>
        {
            if (!_skipItems) foreach (var it in _static) it.Draw(_r, true, t);
        });
        _clip = saved;
        ForceFullRedraw();
    }
}
