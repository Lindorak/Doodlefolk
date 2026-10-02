using System.Numerics;

namespace Doodlefolk;

/// <summary>What's under the cursor: whatever is drawn in front. It's a 2D picture, so a click goes to the thing
/// you can see on top, in the same order the scene is painted (front to back): the offer and wish bubbles, balls,
/// the front parts of things (chair backs, blankets over a sleeper), figures (later ones in front of earlier ones),
/// animals, things (moving ones over ones at rest, later over earlier), and last, anyone hiding behind something.</summary>
sealed partial class App
{
    enum PickKind { None, Offer, Wish, Prop, Figure, Pet, Item }

    readonly record struct Picked(PickKind Kind, Figure? Fig = null, int Joint = -1, Pet? Pet = null, Prop? Prop = null, Item? Item = null);

    Picked Pick(Vector2 c)
    {
        if (OfferHit(c)) return new(PickKind.Offer);
        if (WishHit(c)) return new(PickKind.Wish);
        if (HitProp(c) is { } prop) return new(PickKind.Prop, Prop: prop);
        for (int i = _w.Items.Count - 1; i >= 0; i--)
            if (_w.Items[i] is { Holder: null } over && over.HitOver(c)) return new(PickKind.Item, Item: over);
        if (FrontFigure(c, hiding: false) is { fig: { } f } hit) return new(PickKind.Figure, f, hit.joint);
        if (HitPet(c) is { } pet) return new(PickKind.Pet, Pet: pet);
        if (HitItem(c) is { } item) return new(PickKind.Item, Item: item);
        if (FrontFigure(c, hiding: true) is { fig: { } hf } hh) return new(PickKind.Figure, hf, hh.joint);
        return new(PickKind.None);
    }

    /// <summary>The figure in front at this point (they're drawn in list order, so the last one touched is on top).</summary>
    (Figure? fig, int joint) FrontFigure(Vector2 c, bool hiding)
    {
        for (int i = _w.Figures.Count - 1; i >= 0; i--)
        {
            var f = _w.Figures[i];
            if (f.Mode == Mode.Spawning || f.Dead || f.HidingBehind != hiding) continue;
            if (f.DistanceTo(c, out int j) <= 0) return (f, j);
        }
        return (null, -1);
    }
}
