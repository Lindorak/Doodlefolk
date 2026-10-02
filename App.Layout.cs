using System.Numerics;

namespace Doodlefolk;

/// <summary>Where the town lives (an idea from Rusty's Retirement and Chill Corner): all over the desktop (the
/// usual), a "taskbar village" strip just above the taskbar (windows aren't terrain; lighter and out of the way), or
/// as a live wallpaper behind every window (they still walk on window tops, and your icons and clicks go straight
/// through).</summary>
sealed partial class App
{
    public string ApplyLayout()
    {
        string layout = _settings.TownLayout;
        _w.Env.Strip = layout == "strip" ? Math.Clamp(_settings.StripHeight, 110, 400) * _w.Scale : 0;
        if (!_selfTest && !_trailer) _overlay.SetBehind(layout == "wallpaper");
        _w.Env.Refresh(_overlay.Handle);
        if (layout == "strip") GatherIntoStrip();
        World.Log($"layout: {layout}");
        return layout switch
        {
            "strip" => "The town has moved down to the taskbar.",
            "wallpaper" => "The town lives behind your windows now.",
            _ => "The town has the whole desktop again.",
        };
    }

    /// <summary>Everyone and everything above the strip comes down to the taskbar floor.</summary>
    void GatherIntoStrip()
    {
        foreach (var f in _w.Figures.ToList())
        {
            var (_, _, top) = _w.Env.BoundsAt(f.Base.X);
            if (f.Base.Y >= top + 20 * _w.Scale) continue;
            if (_w.Env.Below(f.Base.X, top) is { } floor) { f.PlaceAt(floor, f.Base.X); f.SpawnT = 0.999f; }
        }
        foreach (var it in _w.Items.Where(i => i.Holder == null))
        {
            var (_, _, top) = _w.Env.BoundsAt(it.Pos.X);
            if (it.Pos.Y < top + 10 * _w.Scale) { it.Pos = new Vector2(it.Pos.X, top + 20 * _w.Scale); it.Vel = Vector2.Zero; it.OnGround = false; }
        }
        foreach (var p in _w.Props.Where(p => p.Holder == null))
        {
            var (_, _, top) = _w.Env.BoundsAt(p.Pos.X);
            if (p.Pos.Y < top + p.Radius) { p.Pos = new Vector2(p.Pos.X, top + p.Radius + 10 * _w.Scale); p.Vel = Vector2.Zero; }
        }
        foreach (var pet in _w.Pets)
        {
            var (_, _, top) = _w.Env.BoundsAt(pet.Pos.X);
            if (pet.Pos.Y < top + 10 * _w.Scale) { pet.Pos = new Vector2(pet.Pos.X, top + 20 * _w.Scale); pet.Vel = Vector2.Zero; pet.Grounded = false; }
        }
    }
}
