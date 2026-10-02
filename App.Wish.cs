using System.Numerics;

namespace StickFight;

/// <summary>A figure's wish as a thought bubble over its head: a cloud with a little picture of the thing and a word or
/// two. Click it and the thing drops in beside them; leave it and the thought fades.</summary>
sealed partial class App
{
    Item? _wishItemIcon;
    Prop? _wishBallIcon;
    Wish? _wishIconFor;

    void WishFrame()
    {
        _w.Wishes = _settings.Wishes;
        _w.Romance = _settings.Romance;
        if (_w.Wish is not { } wish) return;
        var f = wish.By;
        bool gone = !_w.Figures.Contains(f) || f.Dead || f.Mode == Mode.Ragdoll;
        if (gone || !_w.Wishes) { _w.Wish = null; return; }
        if (World.Now > wish.Until) { _w.Wish = null; f.Brain.WishIgnored(wish.What); }
    }

    RectangleF? WishRect()
    {
        if (_w.Wish is not { } wish) return null;
        var f = wish.By;
        float s = MathF.Max(1, f.S);
        Vector2 head = f.Jt[J.Head];
        float w = (28 + wish.What.Say.Length * 5.6f) * s, h = 24 * s;
        var c = head + new Vector2(f.Facing * 16 * f.S, -(f.HeadR + 26 * f.S + h / 2));
        var (L, R, T) = _w.Env.BoundsAt(head.X);
        c.X = M.ClampIn(c.X, L + w / 2 + 4, R - w / 2 - 4);
        c.Y = MathF.Max(c.Y, T + h / 2 + 4);
        wish.Bubble = new RectangleF(c.X - w / 2, c.Y - h / 2, w, h);
        return RectangleF.FromLTRB(MathF.Min(wish.Bubble.Left, head.X) - 6, wish.Bubble.Top - 6, MathF.Max(wish.Bubble.Right, head.X) + 6, head.Y);
    }

    bool WishHit(Vector2 c) => _w.Wish is { } wish && wish.Bubble.Width > 0 &&
                               c.X >= wish.Bubble.Left - 4 && c.X <= wish.Bubble.Right + 4 && c.Y >= wish.Bubble.Top - 4 && c.Y <= wish.Bubble.Bottom + 4;

    void DrawWish()
    {
        if (_w.Wish is not { } wish || WishRect() is not RectangleF all || !Dirty(all)) return;
        var f = wish.By;
        float s = MathF.Max(1, f.S);
        var b = wish.Bubble;
        Vector2 c = new(b.Left + b.Width / 2, b.Top + b.Height / 2), head = f.Jt[J.Head];
        float t = (float)(wish.Until - World.Now);
        float fade = M.Clamp01(t / 0.6f) * M.Clamp01((15 - t) / 0.3f);
        // Thought trail: two little puffs from the head up to the cloud.
        for (int i = 0; i < 2; i++)
        {
            var p = Vector2.Lerp(head + new Vector2(0, -f.HeadR - 3 * f.S), new Vector2(MathF.Min(MathF.Max(head.X, b.Left + 8 * s), b.Right - 8 * s), b.Bottom), 0.35f + i * 0.35f);
            float r = (1.6f + i * 1.2f) * s;
            _r.Disc(p, r + 0.9f * s, Ui.Ink.A(0.8f * fade));
            _r.Disc(p, r, Ui.Fill.A(fade));
        }
        Ui.Card(_r, c, b.Width, b.Height, b.Height / 2, s, fade, wish.Hot ? Ui.Accent : null, wish.Hot ? 2 : 1.3f);
        // The thing itself, small, on the left.
        Vector2 icon = new(b.Left + 13 * s, c.Y);
        if (_wishIconFor != wish)
        {
            _wishIconFor = wish;
            _wishItemIcon = wish.What.Item is { } d ? new Item(d, 1) { SizeMul = MathF.Min(16 * s / d.H, 20 * s / d.W) } : null;
            _wishBallIcon = wish.What.Ball is PropKind k ? new Prop(k, 1) { SizeMul = 7 * s / 6.5f } : null;
        }
        if (_wishItemIcon is { } ii)
        {
            ii.Pos = icon + new Vector2(0, ii.Def.H * ii.Sc / 2);
            ii.Draw(_r, false, _clock.Elapsed.TotalSeconds);
            ii.Draw(_r, true, _clock.Elapsed.TotalSeconds);
        }
        if (_wishBallIcon is { } bi) { bi.Pos = icon; bi.Draw(_r); }
        _r.Text(wish.What.Say, new Vector2(b.Left + 26 * s + (b.Width - 30 * s) / 2, c.Y - 0.5f * s), 9.5f * s, (wish.Hot ? Ui.Accent : Ui.Ink).A(fade), true);
        if (wish.Hot) _r.Text("click to give", new Vector2(c.X, b.Bottom + 7 * s), 7 * s, Ui.Pencil.A(fade), true);
    }

    /// <summary>The user clicked the thought: drop the thing in right beside whoever asked.</summary>
    void GrantWish()
    {
        if (_w.Wish is not { } wish) return;
        _w.Wish = null;
        var f = wish.By;
        float side = f.Facing;
        Item? item = null;
        Prop? ball = null;
        if (wish.What.Item is { } def && _w.Items.Count < 60)
        {
            item = SpawnItem(def);
            if (item != null)
            {
                item.Pos = new Vector2(f.Base.X + side * (def.W * item.Sc / 2 + 18 * f.S), f.Base.Y - 90 * f.S);
                item.Vel = Vector2.Zero;
                item.OnGround = false;
            }
        }
        else if (wish.What.Ball is PropKind k)
        {
            ball = SpawnProp(k);
            ball.Pos = new Vector2(f.Base.X + side * 25 * f.S, f.Base.Y - 90 * f.S);
            ball.Vel = Vector2.Zero;
        }
        World.Play(Sfx.TaDa, f.Base, 0.4f);
        f.Brain.WishGranted(wish.What, item, ball);
        _settings.WishesGranted++;
        _w.Sticker("wish");
        if (_settings.WishesGranted >= 10) _w.Sticker("wishes10");
    }
}
