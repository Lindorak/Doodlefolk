using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>Something a figure would like that isn't around: an object from the catalogue or a kind of ball.</summary>
sealed record Want(string Name, ItemDef? Item, PropKind? Ball, float Strength, string Say);

/// <summary>A figure asking you for something: a thought bubble you can click to give it to them.</summary>
sealed class Wish
{
    public required Figure By;
    public required Want What;
    public double Until;
    public RectangleF Bubble;
    public bool Hot;
}

/// <summary>Wanting things. Needs and tastes turn into wishes ("pizza?" when hungry, a ball when bored and sporty, a
/// bed when worn out). Without the Creator's Pencil they ask you; with it, they draw the thing themselves.</summary>
sealed partial class Brain
{
    float _nextWish = 60, _nextCreate, _pencilSince, _pencilBreak, _pencilKeep = 240;
    ItemDef? _drawing;
    Want? _drawingWant;
    PropKind? _drawingBall;
    Item? _gift;
    Prop? _giftBall;

    public bool HasPencil => f.Weapon?.Def.Verbs.Contains(Verb.Create) == true;

    static readonly ItemDef[] Foods = ItemCatalog.All.Where(d => d.Verbs.Contains(Verb.Eat)).ToArray();

    float DefLike(ItemDef d) => d.Likes.Length == 0 ? 0 : d.Likes.Average(t => f.Tastes.Of(t));

    /// <summary>What it would most like right now that isn't already nearby (null if it's content).</summary>
    Want? Wanting(World w)
    {
        bool Near(Func<Item, bool> ok, float r) => w.Items.Any(i => ok(i) && Vector2.Distance(i.Pos, f.Base) < r * S);
        bool BallNear(float r) => w.Props.Any(p => p.Holder == null && Vector2.Distance(p.Pos, f.Base) < r * S);
        Want? best = null;
        void Consider(Want x) { if (best == null || x.Strength > best.Strength) best = x; }

        if (Hunger > 0.5f && Foods.Length > 0 && !Near(i => i.Def.Verbs.Contains(Verb.Eat) && i.Holder == null, 1800))
        {
            var food = Foods[rng.Next(Foods.Length)];
            Consider(new(food.Name, food, null, Hunger * Taste(Thing.Eating) * 0.8f, $"{food.Name.ToLowerInvariant()}?"));
        }
        if (Stamina < 0.35f && !Near(i => i.Def.Verbs.Any(v => v is Verb.Lie or Verb.Hammock) && i.User == null, 2200))
        {
            var bed = ItemCatalog.Find(P.Playfulness > 0.6f ? "hammock" : "bed");
            if (bed != null) Consider(new(bed.Name, bed, null, (0.5f - Stamina) * 2.2f * Taste(Thing.Napping), $"a {bed.Name.ToLowerInvariant()}? *yawn*"));
        }
        if (Boredom > 0.45f)
        {
            // A ball of the kind it likes best.
            var kinds = new[] { (PropKind.SoccerBall, Thing.SoccerBalls), (PropKind.Basketball, Thing.Basketballs), (PropKind.BeachBall, Thing.BeachBalls) };
            var (kind, thing) = kinds.OrderByDescending(k => f.Tastes.Of(k.Item2)).First();
            float ballLove = (f.Tastes.Of(thing) + f.Tastes.Of(Thing.PlayingBall)) / 2;
            if (ballLove > 0.15f && !BallNear(1500))
                Consider(new(Prop.KindName(kind), null, kind, Boredom * (0.4f + ballLove), $"a {Prop.KindName(kind).ToLowerInvariant()}?"));
            void Fun(string key, Thing t, float min, Func<Item, bool> have, float r, string say)
            {
                if (f.Tastes.Of(t) < min || ItemCatalog.Find(key) is not { } d || Near(have, r)) return;
                Consider(new(d.Name, d, null, Boredom * (0.3f + f.Tastes.Of(t)), say));
            }
            Fun("radio", Thing.Dancing, 0.3f, i => i.Def.Verbs.Contains(Verb.Dance), 2500, "some music?");
            Fun("book", Thing.Reading, 0.3f, i => i.Def.Verbs.Contains(Verb.Read), 2000, "a good book?");
            Fun("trampoline", Thing.Tricks, 0.35f, i => i.Def.Verbs.Contains(Verb.Bounce), 2500, "a trampoline!?");
            Fun("armchair", Thing.Sitting, 0.35f, i => i.Def.Verbs.Contains(Verb.Sit), 1500, "somewhere to sit?");
            if (P.Aggression > 0.55f && f.Weapon == null) Fun("sword", Thing.Fighting, 0.3f, i => i.Def.Weapon && i.Holder == null, 1500, "a sword...");
        }
        if (Fear > 0.5f && HunterAround(w) && ItemCatalog.Find("box") is { } box && !Near(i => i.Def.Verbs.Contains(Verb.Hide) && i.User == null, 1500))
            Consider(new(box.Name, box, null, Fear * 1.4f, "somewhere to hide!"));
        return best;
    }

    /// <summary>Something it fancies drawing just for fun (with the pencil and nothing it needs).</summary>
    Want Whim(World w)
    {
        var pool = ItemCatalog.All.Where(d => d.Sport == null && !d.Verbs.Contains(Verb.Create) && (!d.Weapon || P.Aggression > 0.6f)
                                              && (!d.Verbs.Contains(Verb.Eat) || Hunger > 0.3f)).ToList();
        float Weight(ItemDef d) => MathF.Max(0.05f, 1 + DefLike(d) * 1.5f) * rng.Range(0.5f, 1.5f);
        var pick = pool.OrderByDescending(Weight).First();
        if (rng.NextDouble() < 0.25 && f.Tastes.Likes(Thing.PlayingBall))
        {
            var k = new[] { PropKind.SoccerBall, PropKind.Basketball, PropKind.BeachBall, PropKind.Ball }[rng.Next(4)];
            return new(Prop.KindName(k), null, k, 0.5f, "");
        }
        return new(pick.Name, pick, null, 0.5f, "");
    }

    void WishOptions(World w, OptionList opts)
    {
        // A present (from you, or drawn): go and enjoy it.
        if (_gift is { } g && w.Items.Contains(g) && g.OnGround && g.Free && GiftVerb(g) is Verb gv)
            opts.Add(4, () => { _gift = null; UseItem(g, gv, w); }, $"Enjoy the {g.Def.Name.ToLowerInvariant()}");
        else if (_gift != null && !w.Items.Contains(_gift)) _gift = null;
        if (_giftBall != null) { if (!w.Props.Contains(_giftBall) || _giftBall.OnGround) _giftBall = null; }

        if (HasPencil)
        {
            // Pencils get passed around: after a while it's someone else's turn.
            if (_t0 - _pencilSince > _pencilKeep)
            {
                f.DropWeapon(new Vector2(f.Facing * 60 * S, -80 * S));
                f.Emote(P.Sociability > 0.5f ? "your turn!" : "done drawing", 1.3f);
                _pencilBreak = _t0 + rng.Range(90, 200);
                _pencilKeep = rng.Range(150, 360);
                return;
            }
            if (_t0 > _nextCreate && w.Items.Count < 40 && Stamina > 0.2f)
            {
                var want = Wanting(w) ?? Whim(w);
                opts.Add((0.6f + P.Playfulness + P.Curiosity * 0.5f) * (0.5f + want.Strength + Boredom), () => BeginCreate(want), $"Draw {(want.Item?.Article ?? "a")} {want.Name.ToLowerInvariant()}");
            }
            return;
        }
        if (!w.Wishes || w.Wish != null || World.Now < w.NextWishAt || _t0 < _nextWish) return;
        if (Wanting(w) is { Strength: > 0.35f } wish)
        {
            // Not an action: a thought that hangs over whatever it does next.
            w.Wish = new Wish { By = f, What = wish, Until = World.Now + 15 };
            w.NextWishAt = World.Now + rng.Range(45, 110);
            _nextWish = _t0 + rng.Range(90, 200);
        }
    }

    /// <summary>Debug: want something right now (ignores cooldowns; falls back to a whim).</summary>
    public string ForceWish(World w)
    {
        var want = Wanting(w) ?? Whim(w);
        if (HasPencil) { BeginCreate(want); return $"drawing {want.Name}"; }
        if (want.Say.Length == 0) want = want with { Say = $"{(want.Item?.Article ?? "a")} {want.Name.ToLowerInvariant()}?" };
        w.Wish = new Wish { By = f, What = want, Until = World.Now + 15 };
        return want.Name;
    }

    static Verb? GiftVerb(Item it) =>
        it.Def.Verbs.Where(v => v is not (Verb.Play or Verb.Create)).Cast<Verb?>().FirstOrDefault();

    /// <summary>You clicked the thought bubble: here's the thing it asked for.</summary>
    public void WishGranted(Want what, Item? item, Prop? ball)
    {
        DiaryWish(what, true);
        f.Emote(rng.NextDouble() < 0.5 ? "yay! thank you!" : "♥ thanks!", 1.6f);
        RememberPlace(World.Current, 0.3f, "a present from you");
        Cheered(0.25f);
        Boredom = MathF.Max(0, Boredom - 0.2f);
        FeelUser(0.06f, $"Gave me the {what.Name.ToLowerInvariant()} I asked for");
        if (f.Mode == Mode.Control && f.Grounded && _g is G.Idle or G.Walk or G.SitFloor or G.SitEdge) f.SetAction(Act.Cheer);
        _gift = item;
        _giftBall = ball;
    }

    /// <summary>The bubble went unanswered. A little disappointing, nothing more.</summary>
    public void WishIgnored(Want what)
    {
        DiaryWish(what, false);
        f.Emote(rng.NextDouble() < 0.5 ? "oh well" : "…", 1.2f);
        Saddened(0.05f);
    }

    // ---------------- drawing with the Creator's Pencil ----------------

    void BeginCreate(Want want)
    {
        _drawing = want.Item;
        _drawingBall = want.Ball;
        _drawingWant = want;
        Go(G.Create, 4);
        f.Emote(want.Say.Length > 0 ? $"✎ {want.Say}" : "✎ hmm…", 1.2f);
    }

    void DoCreate(World w)
    {
        f.DesiredVX = 0;
        if (!HasPencil || (_drawing == null && _drawingBall == null) || !f.Grounded) { _drawing = null; _drawingBall = null; Go(G.Idle, 1); return; }
        Vector2 spot = f.Base + new Vector2(f.Facing * 34 * S, -20 * S);
        f.LookAt = spot;
        // Scribbling in the air, the pencil leaving a sparkly trail.
        f.SetAction(_t % 0.6f < 0.3f ? Act.Wave : Act.Swat);
        var ink = _drawing != null ? _drawing.Color : new Color4(1, 0.85f, 0.3f, 1);
        if ((int)(_t / 0.07f) != (int)((_t - World.Dt) / 0.07f)) w.Fx.Spark(f.ToolTip, S * 0.45f, w.Rng, 0.35f, ink);
        if (_t < 2.6f - Sk(SkillKind.Drawing) * 1.2f) return;

        Vector2 at = spot + new Vector2(f.Facing * 10 * S, 0);
        if (_drawing != null && w.MakeItem?.Invoke(_drawing.Key) is { } it)
        {
            it.Pos = new Vector2(at.X, at.Y + it.Def.H * it.Sc * 0.5f);
            it.Vel = new Vector2(f.Facing * 30 * S, -100 * S);
            it.OnGround = false;
            _gift = it;
        }
        else if (_drawingBall is PropKind k && w.MakeProp != null)
        {
            var p = w.MakeProp(k);
            p.Pos = at;
            p.Vel = new Vector2(f.Facing * 60 * S, -200 * S);
            p.OnGround = false;
        }
        w.Fx.Dust(at, S, 10, 0.7f, w.Rng);
        World.Play(Sfx.TaDa, at, 0.4f);
        f.Emote(rng.NextDouble() < 0.6 ? "ta-da!" : "✨", 1.4f);
        if (_drawingWant != null) DiaryDrew(_drawingWant);
        Practice(SkillKind.Drawing, 0.08f);
        Cheered(0.15f);
        Boredom = MathF.Max(0, Boredom - 0.3f);
        _nextCreate = _t0 + rng.Range(45, 120) * (1.6f - P.Playfulness);
        _drawing = null;
        _drawingBall = null;
        Go(G.Idle, rng.Range(1, 2));
    }
}
