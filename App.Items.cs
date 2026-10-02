using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>Objects on the desktop: spawning (including from typed words), dragging, drawing and saving.</summary>
sealed partial class App
{
    Item? _pressItem;

    Item? HitItem(Vector2 c)
    {
        for (int i = _w.Items.Count - 1; i >= 0; i--)
            if (_w.Items[i].Holder == null && _w.Items[i].HitTest(c)) return _w.Items[i];
        return null;
    }

    Item? SpawnItem(ItemDef def, float size = 1, Color4? colour = null, bool flip = false)
    {
        var it = new Item(def, _w.Scale) { SizeMul = size, Flip = flip, Born = _clock.Elapsed.TotalSeconds };
        if (colour is { } c) it.Color = c;
        var plat = RandomSpawnPlatform(def.W * it.Sc + 20 * _w.Scale);
        float half = def.W * it.Sc * 0.5f;
        float x = plat != null ? _w.Rng.Range(plat.X1 + half, MathF.Max(plat.X1 + half + 1, plat.X2 - half)) : _w.Env.Virtual.Left + _w.Env.Virtual.Width / 2f;
        var (_, _, top) = _w.Env.BoundsAt(x);
        it.Pos = new Vector2(x, top + def.H * it.Sc + 30 * _w.Scale);
        it.Vel = new Vector2(_w.Rng.Range(-60, 60) * _w.Scale, 0);
        _w.Items.Add(it);
        return it;
    }

    /// <summary>"a giant red couch" → a giant red couch. Returns what to tell the user.</summary>
    string Summon2Last = "";

    string Summon(string text) => Summon2Last = SummonInner(text);

    string SummonInner(string text)
    {
        var (def, size, colour, noun) = ItemCatalog.Parse(text);
        if (noun.Length == 0) return "Type the name of a thing, like \"a comfy couch\" or \"pizza\".";
        if (ItemCatalog.BallFor(noun) is { } kind)
        {
            var p = SpawnProp(kind);
            p.SizeMul = Math.Clamp(size, 0.3f, 5f);
            if (colour is { } c) p.Color = c;
            return $"Here's your {Prop.KindName(kind).ToLowerInvariant()}!";
        }
        if (def == null) return $"Nobody here knows what \"{noun}\" is yet.";
        if (_w.Items.Count >= 60) return "That's a lot of stuff already. Clear some things first.";
        var it = SpawnItem(def, size, colour, _w.Rng.NextDouble() < 0.5);
        if (it == null) return "Couldn't find room for it.";
        string sz = size >= 2.4f ? "giant " : size >= 1.8f ? "huge " : size >= 1.3f ? "big " : size <= 0.5f ? "tiny " : size <= 0.75f ? "little " : "";
        return $"Drew {(sz.Length > 0 || colour != null ? "a " : def.Article + " ")}{sz}{def.Name.ToLowerInvariant()}.";
    }

    double _nextTidy;

    /// <summary>Games hand out rackets; spare ones left lying around get tidied away (a couple of each are kept).</summary>
    void TidyGear(double now)
    {
        if (now < _nextTidy) return;
        _nextTidy = now + 4;
        foreach (var key in new[] { "racket", "badmintonracket" })
        {
            var spare = _w.Items.Where(i => i.Def.Key == key && i.Holder == null && i.User == null && i != _pressItem).ToList();
            foreach (var it in spare.Skip(2).Take(3))
            {
                _w.Fx.Dust(it.Pos, _w.Scale, 6, 0.5f, _w.Rng);
                _w.RemoveItem(it);
            }
        }
    }

    void GrabItem(Item it)
    {
        foreach (var f in _w.Figures) f.Brain.OnItemGone(it);   // anyone using it gets dumped off
        it.Pinned = true;
        it.PinOffset = _w.Cursor - it.Pos;
        it.PinTarget = _w.Cursor;
        _pressItem = it;
        _prevCursor = _w.Cursor;
    }

    void ReleaseItem()
    {
        if (_pressItem == null) return;
        _pressItem.Release(M.ClampLength(_w.CursorVel, 4000 * _w.Scale));
        _pressItem = null;
    }

    void DrawItems(bool over)
    {
        double t = _clock.Elapsed.TotalSeconds;
        if (_skipItems) return;
        foreach (var it in _w.Items) if (it.Holder == null && Dirty(it.Bounds())) it.Draw(_r, over, t);
    }

    // Things being held (a book, a sword, the radio) are saved too; half-eaten food isn't.
    List<SavedItem> SaveItems() => _w.Items.Where(i => i.BitesLeft == i.Def.Bites)
        .Select(i => new SavedItem { Key = i.Def.Key, Size = i.SizeMul, Color = Settings.Hex(i.Color), Flip = i.Flip }).ToList();

    void RestoreItems(List<SavedItem> items)
    {
        foreach (var s in items)
            if (ItemCatalog.Find(s.Key) is { } def)
                SpawnItem(def, Math.Clamp(s.Size, 0.3f, 3.5f), s.Color.Length == 7 ? Settings.ParseHex(s.Color) : null, s.Flip);
    }
}
