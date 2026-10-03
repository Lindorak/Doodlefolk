using System.Numerics;

namespace Doodlefolk;

/// <summary>Moving furniture. Light things (a chair, a lamp, a box, the radio) one figure lifts over its head and
/// carries; heavy things (a couch, a bed, a table) take two: the mover asks a friend, each takes an end, they crouch,
/// lift together and shuffle it across (one walking backwards), then set it down where it's wanted. Anything going
/// wrong on the way (you grab it, one of them is knocked over, the friend wanders off) and it's put down, or dropped.</summary>
sealed partial class Brain
{
    enum HaulStep { Approach, Ready, Lift, Carry, Lower }

    Item? _haulIt;
    Figure? _haulMate;
    bool _haulLead;
    int _haulSide;
    float _haulDestX, _haulStepT, _haulStuckT;
    HaulStep _haulStep;
    string _haulWhy = "";
    Action<bool>? _haulDone;

    /// <summary>Things that can be moved at all: not water, not a stall or a stage, not something somebody's using.</summary>
    public static bool Movable(Item it) =>
        !it.Def.Carry && !it.IsWater && !it.IsVehicle && it.Def.Key is not ("stage" or "shopstall" or "foodcart" or "schoolboard" or "buildsite" or "memorial" or "fishtank" or "treehouse" or "fort")
        && it.Def.Mass <= 8 && it.Def.Verbs.Length > 0;

    /// <summary>Heavy (or long) enough to need two.</summary>
    public static bool NeedsTwo(Item it) => it.Def.Mass > 2.5f || it.Def.W * it.SizeMul * it.ScaleX > 52;

    bool FreeToMove(Item it) => it.Free && it.OnGround && it.User == null && it.Seated.All(s => s == null) && it.HaulA == null && it.Rider == null;

    /// <summary>Move <paramref name="it"/> so it stands at <paramref name="destX"/> (on the surface it's on). <paramref name="done"/>
    /// hears whether it worked.</summary>
    void BeginHaul(Item it, float destX, World w, string why, Action<bool>? done = null)
    {
        _haulDone = done;
        _haulWhy = why;
        if (!FreeToMove(it)) { HaulFinished(false); return; }
        Figure? mate = null;
        if (NeedsTwo(it))
        {
            mate = w.Figures.Where(o => o != f && o.Mode == Mode.Control && o.Grounded && !o.Brain.Engaged && o.Brain._g is G.Idle or G.Watch or G.SitFloor or (G.Walk) && !o.Brain.InFight
                                         && MathF.Abs(o.Base.Y - it.Pos.Y) < 6 * S && MathF.Abs(o.Base.X - it.Pos.X) < 900 * S && AffinityWith(o) > -0.1f)
                            .OrderByDescending(o => AffinityWith(o) + o.Brain.P.Sociability * 0.3f).FirstOrDefault();
            if (mate == null || !mate.Brain.AgreeToHelp(f, it, w))
            {
                f.Emote(V("too heavy…", "Anyone?! Help?!", "Heavy. Forget it.", "I can't lift it alone…", "It won't move for one."), 1.6f);
                Flash(Manpu.SweatDrop, 2.5f);
                HaulFinished(false);
                return;
            }
        }
        _haulIt = it; _haulMate = mate; _haulLead = true;
        _haulSide = MathF.Sign(f.Base.X - it.Pos.X) is var s && s != 0 ? (int)s : 1;
        _haulDestX = destX;
        _haulStep = HaulStep.Approach; _haulStepT = 0; _haulStuckT = 0;
        it.HaulA = f; it.HaulB = mate;
        f.Emote(V(mate != null ? $"{mate.Name}, grab that end!" : "Hup!", mate != null ? "TEAM LIFT!!" : "I GOT THIS!", mate != null ? "You. Other end." : "Fine. I'll do it.", mate != null ? "C-could you help me?" : "I can do this…", "Let's move this."), 1.6f);
        Go(G.Haul, 60);
        if (mate != null) mate.Brain.JoinHaul(f, it, -_haulSide);
    }

    /// <summary>A friend asks for a hand with something heavy.</summary>
    bool AgreeToHelp(Figure from, Item it, World w)
    {
        if (Engaged || InFight || Asleep) return false;
        float yes = 0.35f + P.Sociability * 0.3f + AffinityWith(from) * 0.5f + P.Energy * 0.15f - (1 - Stamina) * 0.4f + (A is Archetype.Deredere or Archetype.Genki ? 0.25f : 0);
        if (rng.NextDouble() > yes) { f.Emote(V("not now", "maybe later!", "Do it yourself.", "s-sorry…", "Not today."), 1.2f); return false; }
        return true;
    }

    void JoinHaul(Figure lead, Item it, int side)
    {
        _haulIt = it; _haulMate = lead; _haulLead = false; _haulSide = side;
        _haulStep = HaulStep.Approach; _haulStepT = 0;
        SayInCharacter("thanks", lead, World.Current!, V("Sure!", "ON IT!", "Fine.", "o-okay!", "Of course."));
        Go(G.Haul, 60);
    }

    float HaulEndX(Item it, int side) => it.Pos.X + side * (it.Def.W * it.Sc * it.ScaleX * 0.5f + 5 * S);

    void DoHaul(World w)
    {
        var it = _haulIt;
        if (it == null || !w.Items.Contains(it) || it.Pinned || it.Holder != null || f.Mode != Mode.Control) { DropHaul(w, false); return; }
        var mate = _haulMate;
        bool team = _haulLead ? mate != null : true;
        if (team && (mate == null || !w.Figures.Contains(mate) || mate.Mode != Mode.Control || mate.Brain._haulIt != it)) { DropHaul(w, false); return; }
        _haulStepT += World.Dt;
        bool solo = !team;
        f.Hauling = it;
        switch (_haulStep)
        {
            case HaulStep.Approach:
            {
                float x = solo ? it.Pos.X + _haulSide * 2 * S : HaulEndX(it, _haulSide);
                if (MoveToward(x, 4 * S)) { _haulStep = HaulStep.Ready; _haulStepT = 0; }
                if (_haulStepT > 20) DropHaul(w, false);
                break;
            }
            case HaulStep.Ready:
                f.DesiredVX = 0;
                FaceTo(it.Pos.X);
                // Wait for the other end to be ready.
                if (solo || (mate!.Brain._haulStep >= HaulStep.Ready)) { _haulStep = HaulStep.Lift; _haulStepT = 0; }
                else if (_haulStepT > 15) DropHaul(w, false);
                break;
            case HaulStep.Lift:
                f.DesiredVX = 0;
                f.SetAction(Act.Scoop);
                ReachFor(it, solo);
                if (_haulStepT > 0.55f && (solo || mate!.Brain._haulStep >= HaulStep.Lift))
                {
                    _haulStep = HaulStep.Carry; _haulStepT = 0;
                    f.SetAction(Act.Stand);
                    if (_haulLead) { it.Lifted = true; it.HaulSolo = solo; World.Play(Sfx.Thud, it.Pos, 0.15f, 1.4f); }
                }
                break;
            case HaulStep.Carry:
            {
                f.SetAction(Act.Stand);
                f.KeepFacing = !solo;
                ReachFor(it, solo);
                if (_haulLead)
                {
                    // Walk it over; slower with something heavy (and slower again together).
                    float target = _haulDestX + (solo ? 0 : _haulSide * (it.Def.W * it.Sc * it.ScaleX * 0.5f + 5 * S));
                    float dx = target - f.Base.X;
                    float speed = f.WalkSpeed * (solo ? 0.6f : 0.42f);
                    if (MathF.Abs(dx) < 4 * S) { f.DesiredVX = 0; _haulStep = HaulStep.Lower; _haulStepT = 0; if (mate != null) { mate.Brain._haulStep = HaulStep.Lower; mate.Brain._haulStepT = 0; } break; }
                    f.DesiredVX = MathF.Sign(dx) * speed * MathF.Max(0.35f, M.Clamp01(MathF.Abs(dx) / (30 * S)));
                    if (solo) FaceTo(f.Base.X + MathF.Sign(dx) * 100);
                    _haulStuckT = MathF.Abs(f.Vel.X) < 4 * S ? _haulStuckT + World.Dt : 0;
                    if (_haulStuckT > 2.5f || _haulStepT > 45) { _haulStep = HaulStep.Lower; _haulStepT = 0; if (mate != null) { mate.Brain._haulStep = HaulStep.Lower; mate.Brain._haulStepT = 0; } }
                }
                else
                {
                    // Keep our end: the far side of the thing from the mover.
                    float lead = mate!.Base.X, half = it.Def.W * it.Sc * it.ScaleX * 0.5f + 5 * S;
                    float want = lead - mate.Brain._haulSide * half * 2;
                    f.DesiredVX = Math.Clamp((want - f.Base.X) * 7, -f.RunSpeed * 0.6f, f.RunSpeed * 0.6f);
                    FaceTo(lead);
                }
                if (rng.NextDouble() < World.Dt * 0.25) f.Emote(solo ? "hup" : V("heave…", "HEAVE HO!", "this is heavy.", "nngh…", "steady…"), 0.9f);
                break;
            }
            case HaulStep.Lower:
                f.DesiredVX = 0;
                f.SetAction(Act.Scoop);
                ReachFor(it, solo);
                if (_haulStepT > 0.5f) { if (_haulLead) SetDown(it, w, true); return; }
                break;
        }
    }

    /// <summary>Hands to the thing: both up under it (overhead), or to our end of it.</summary>
    void ReachFor(Item it, bool solo)
    {
        float h = it.Def.H * it.Sc * it.ScaleY, half = it.Def.W * it.Sc * it.ScaleX * 0.5f;
        if (solo)
        {
            var under = it.Lifted ? it.Pos : it.Pos + new Vector2(0, -h * 0.4f);
            f.HoldN = under + new Vector2(half * 0.5f * f.Facing, 0);
            f.HoldF = under - new Vector2(half * 0.5f * f.Facing, 0);
        }
        else
        {
            var end = new Vector2(it.Pos.X - _haulSide * -half, it.Pos.Y - h * (it.Lifted ? 0.45f : 0.3f));
            end.X = it.Pos.X + _haulSide * half * 0.92f;
            f.HoldN = end; f.HoldF = end + new Vector2(0, -h * 0.2f);
        }
    }

    /// <summary>Put it down here (and say so), or just let go.</summary>
    void SetDown(Item it, World w, bool placed)
    {
        it.Lifted = false; it.HaulA = it.HaulB = null; it.HaulSolo = false;
        it.Vel = Vector2.Zero; it.OnGround = false;
        var mate = _haulMate;
        if (mate != null && mate.Brain._haulIt == it) mate.Brain.HaulLetGo(placed, f);
        _haulIt = null; _haulMate = null; f.Hauling = null; f.KeepFacing = false;
        if (placed)
        {
            World.Play(Sfx.Thud, it.Pos, 0.25f, 1.1f);
            w.Fx.Dust(it.Pos, S, 4, 0.4f, rng);
            f.Emote(V("there!", "PERFECT!", "Done.", "there… ♪", "Just right."), 1.3f);
            Cheered(0.15f);
            string what = it.Def.Name.ToLowerInvariant();
            Write("moved:" + it.Def.Key, mate != null
                ? V($"Moved the {what} with {mate.Name}{_haulWhy}.", $"{mate.Name} and I moved the {what}{_haulWhy}! Teamwork!", $"Moved the {what}{_haulWhy}. {mate.Name} helped. A bit.", $"{mate.Name} helped me move the {what}{_haulWhy}…", $"Together we moved the {what}{_haulWhy}.")
                : V($"Moved the {what}{_haulWhy}.", $"Moved the {what}{_haulWhy} all by myself!", $"Moved the {what}{_haulWhy}. Better.", $"I moved the {what}{_haulWhy}. It looks nice now.", $"The {what} has a new home{_haulWhy}."), "★", 300);
            if (mate != null) { AddAffinity(mate, 0.08f); mate.Brain.AddAffinity(f, 0.08f); }
            Go(G.Idle, 1.5f);
        }
        else Go(G.Idle, 1);
        HaulFinished(placed);
    }

    void HaulLetGo(bool placed, Figure lead)
    {
        _haulIt = null; _haulMate = null; f.Hauling = null; f.KeepFacing = false;
        if (placed) Write("helpedmove:" + lead.Name, V($"Helped {lead.Name} move some furniture.", $"Helped {lead.Name} move stuff! Strong!", $"Helped {lead.Name} lug furniture about.", $"I helped {lead.Name} carry something heavy."), "♪", 600);
        Go(G.Idle, placed ? 1.2f : 0.8f);
    }

    /// <summary>Something went wrong on the way: put it down where it is (or drop it, if we were knocked over).</summary>
    void DropHaul(World w, bool _)
    {
        var it = _haulIt;
        if (it == null) { f.Hauling = null; Go(G.Idle, 1); return; }
        if (!_haulLead)
        {
            // Losing either end drops the team load; it cannot become a solo haul.
            var lead = _haulMate;
            _haulIt = null; _haulMate = null; f.Hauling = null; f.KeepFacing = false;
            if (lead != null && lead.Brain._haulIt == it) lead.Brain.DropHaul(w, false);
            Go(G.Idle, 1);
            return;
        }
        bool heldUp = it.Lifted;
        SetDown(it, w, false);
        if (heldUp) f.Emote(V("whoops!", "OOPS!", "Ugh.", "eep!", "…it slipped."), 1);
    }

    void HaulFinished(bool ok)
    {
        var d = _haulDone;
        _haulDone = null;
        d?.Invoke(ok);
    }

    /// <summary>Leaving the haul goal for anything else: let go of it.</summary>
    void LeaveHaul()
    {
        if (_haulIt == null) return;
        var w = World.Current;
        if (w != null) DropHaul(w, false);
    }

    // ---------------- redecorating ----------------

    /// <summary>Somewhere better for a piece of furniture: next to something it goes with (a seat by the radio, the fire
    /// or a lamp; a bed out of the way at the end), on the same surface, and not where it already is.</summary>
    (Item it, float x, string why)? RedecorateIdea(World w)
    {
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        if (seg == null) return null;
        var mine = w.Items.Where(i => Movable(i) && FreeToMove(i) && MathF.Abs(i.Pos.Y - seg.Y) < 3 && i.Pos.X > seg.X1 && i.Pos.X < seg.X2 && !Unreachable(i)).ToList();
        if (mine.Count == 0) return null;
        var anchors = w.Items.Where(i => i.Def.Key is "radio" or "campfire" or "lamp" or "fairylights" or "tv" or "table" or "fishtank" && MathF.Abs(i.Pos.Y - seg.Y) < 3 && i.Pos.X > seg.X1 && i.Pos.X < seg.X2).ToList();
        (Item, float, string)? best = null;
        float bestScore = 0;
        foreach (var it in mine)
        {
            bool seat = it.Def.Verbs.Contains(Verb.Sit) || it.Def.Verbs.Contains(Verb.Lie);
            foreach (var a in anchors)
            {
                if (a == it || !seat) continue;
                float half = (it.Def.W * it.Sc * it.ScaleX + a.Def.W * a.Sc * a.ScaleX) * 0.5f + 10 * S;
                float side = MathF.Sign(it.Pos.X - a.Pos.X); if (side == 0) side = 1;
                float x = a.Pos.X + side * half;
                if (MathF.Abs(x - it.Pos.X) < 40 * S || x < seg.X1 + 20 * S || x > seg.X2 - 20 * S) continue;
                if (w.Items.Any(o => o != it && o != a && !o.Def.Carry && MathF.Abs(o.Pos.Y - seg.Y) < 3 && MathF.Abs(o.Pos.X - x) < (o.Def.W * o.Sc + it.Def.W * it.Sc) * 0.5f)) continue;
                float like = it.Def.Verbs.Contains(Verb.Sit) ? Taste(Thing.Sitting) : Taste(Thing.Napping);
                float score = like * (a.Def.Key is "radio" ? Taste(Thing.Dancing) : 1) * (1 + MathF.Abs(x - it.Pos.X) / (800 * S));
                if (score > bestScore) { bestScore = score; best = (it, x, $" next to the {a.Def.Name.ToLowerInvariant()}"); }
            }
        }
        return best;
    }

    void RedecorateOptions(World w, OptionList opts)
    {
        if (Stamina < 0.4f || _t0 < _redecorateCd || w.Items.Count == 0) return;
        if (RedecorateIdea(w) is not { } idea) return;
        _redecorateCd = _t0 + 120;
        float weight = (0.12f + P.Energy * 0.15f + P.Curiosity * 0.1f) * (0.6f + Boredom);
        opts.Add(weight, () => BeginHaul(idea.it, idea.x, w, idea.why, ok => { if (!ok) _redecorateCd = _t0 + 300; }), $"Move the {idea.it.Def.Name.ToLowerInvariant()}");
    }

    float _redecorateCd = 60;

    /// <summary>For the contact sheets and tests: move this, to here.</summary>
    public void HaulNow(Item it, float x, World w) => BeginHaul(it, x, w, "");
}
