using System.Numerics;

namespace StickFight;

enum SocialKind { Chat, HighFive, Follow, Dance }

/// <summary>Figures seeking each other out: chatting, high-fives, following, sitting together.
/// The initiator walks over and asks; the other figure may say no (busy, grumpy, not a fan).</summary>
sealed partial class Brain
{
    Figure? _partner;
    bool _initiator;
    int _turn = -1;
    float _nextBubble;

    static readonly string[] ChatBits = { "…", "?", "!", "♪", "ha", "~", "hm", "♥", "!!", "??" };

    (float, Action)? SocialOption(World w)
    {
        if (w.Figures.Count < 2 || Stamina < 0.15f) return null;
        var cands = new List<(Figure o, float score)>();
        foreach (var o in w.Figures)
        {
            if (o == f || o.Mode != Mode.Control || o.Brain.Asleep || o.Climbing || o.Brain.InFight) continue;
            if (RelationTo(o) is Relation.Ignore or Relation.Enemies) continue;
            float d = Vector2.Distance(o.Base, f.Base);
            if (d > 1600 * S) continue;
            float a = AffinityWith(o);
            if (a < -0.3f) continue;
            // Kindred spirits (shared likes) are the ones it seeks out.
            cands.Add((o, (a + 0.6f + MathF.Max(0, f.Tastes.Similarity(o.Tastes)) * 0.5f) / (1 + d / (600 * S))));
        }
        if (cands.Count == 0) return null;
        float roll = rng.Range(0, cands.Sum(c => c.score));
        var pick = cands[^1].o;
        foreach (var c in cands) { roll -= c.score; if (roll <= 0) { pick = c.o; break; } }
        float weight = P.Sociability * (0.3f + Loneliness * 1.4f) * (1 - Annoyance * 0.7f) * Taste(Thing.Chatting);
        var friend = pick;
        return (weight, () => Approach(friend, w));
    }

    void Approach(Figure o, World w)
    {
        float side = MathF.Sign(f.Base.X - o.Base.X);
        if (side == 0) side = 1;
        Navigate(() => o.Mode is Mode.Control or Mode.GetUp && w.Figures.Contains(o) ? o.Base + new Vector2(side * 22 * S, 0) : null,
                 6 * S, false, () => Meet(o, w), WalkPurpose.Social);
    }

    void Meet(Figure o, World w)
    {
        FaceTo(o.Base.X);
        var ob = o.Brain;
        if (ob._g is G.SitFloor or G.SitEdge or G.SitWith)
        {
            BeginSitWith(o);
            return;
        }
        float a = AffinityWith(o);
        // Shared hobbies first: two ball-lovers play catch, two dancers dance.
        var shared = f.Tastes.SharedLikes(o.Tastes).ToHashSet();
        if (shared.Contains(Thing.PlayingBall) && NearestFreeBall(w, 600 * S) is { SizeMul: <= 1.8f } ball && rng.NextDouble() < 0.6)
        {
            f.Emote("⚽", 1.2f);
            _passFrom = o;
            GoToBall(ball, w, () => ChoosePlay(ball, w, BallPlay.Pass));
            return;
        }
        var kinds = new List<(float, SocialKind)>
        {
            (1f * Taste(Thing.Chatting), SocialKind.Chat),
            ((P.Playfulness * 0.8f + MathF.Max(0, a)) * Taste(Thing.HighFives), SocialKind.HighFive),
            (0.25f + P.Curiosity * 0.2f, SocialKind.Follow),
        };
        if (shared.Contains(Thing.Dancing)) kinds.Add((1.5f, SocialKind.Dance));
        float roll = rng.Range(0, kinds.Sum(k => k.Item1));
        var kind = SocialKind.Chat;
        foreach (var (wgt, k) in kinds) { roll -= wgt; if (roll <= 0) { kind = k; break; } }
        if (!StartSocialWith(o, kind, w))
        {
            f.Emote("?", 1);
            AddAffinity(o, -0.02f);
            Go(G.Idle, 1.5f);
        }
    }

    /// <summary>Ask <paramref name="o"/> to do something together. Follow doesn't need permission.</summary>
    bool StartSocialWith(Figure o, SocialKind kind, World w)
    {
        if (kind == SocialKind.Follow)
        {
            Go(G.Follow, rng.Range(8, 20));
            _partner = o;
            _initiator = true;
            f.Emote("♪", 1);
            return true;
        }
        float dur = kind switch { SocialKind.Chat => rng.Range(4, 9), SocialKind.Dance => rng.Range(4, 8), _ => 1.4f };
        if (!o.Brain.Invite(f, kind, dur)) return false;
        Go(GoalFor(kind), dur);
        _partner = o;
        _initiator = true;
        _turn = -1;
        return true;
    }

    /// <summary>Another figure asks to hang out. Returns whether we said yes.</summary>
    bool Invite(Figure from, SocialKind kind, float dur)
    {
        bool free = f.Mode == Mode.Control && f.Grounded && !f.Climbing && f.Carrying == null &&
                    (_g is G.Idle or G.Watch || (_g == G.Walk && _purpose is WalkPurpose.Wander or WalkPurpose.Explore));
        if (!free) return false;
        float yes = 0.3f + P.Sociability * 0.5f + AffinityWith(from) * 0.4f - Annoyance * 0.6f
                  + (kind == SocialKind.Dance ? f.Tastes.Of(Thing.Dancing) * 0.4f : kind == SocialKind.HighFive ? f.Tastes.Of(Thing.HighFives) * 0.3f : f.Tastes.Of(Thing.Chatting) * 0.3f);
        if (rng.NextDouble() > yes)
        {
            f.Emote("…", 1);
            return false;
        }
        Go(GoalFor(kind), dur);
        _partner = from;
        _initiator = false;
        _turn = -1;
        FaceTo(from.Base.X);
        return true;
    }

    /// <summary>Leaving a social goal; tells the partner if they're still with us.</summary>
    void EndSocial()
    {
        var p = _partner;
        _partner = null;
        if (p != null && p.Brain._partner == f) p.Brain.PartnerLeft(f, _t >= _dur);
    }

    void PartnerLeft(Figure who, bool graceful)
    {
        _partner = null;
        if (_g is not (G.Chat or G.HighFive or G.SitWith or G.DanceWith)) return;
        if (graceful && _g == G.Chat)
        {
            AddAffinity(who, 0.08f + P.Sociability * 0.05f);
            if (AffinityWith(who) > 0.5f) f.Emote("?", 1.2f);
        }
        else if (!graceful) f.Emote("?", 0.8f);
        Go(G.Idle, graceful ? 1.5f : 1);
    }

    bool PartnerOk(World w, G expect) =>
        _partner != null && w.Figures.Contains(_partner) && _partner.Mode == Mode.Control &&
        (expect == G.Follow || _partner.Brain._partner == f);

    void DoChat(World w)
    {
        if (!PartnerOk(w, G.Chat)) { _partner = null; f.Emote("?", 0.8f); Go(G.Idle, 1); return; }
        var o = _partner!;
        FaceTo(o.Base.X);
        f.LookAt = o.Jt[J.Head];
        // Keep a comfortable distance.
        float gap = MathF.Abs(o.Base.X - f.Base.X);
        f.DesiredVX = gap < 16 * S ? -MathF.Sign(o.Base.X - f.Base.X) * f.WalkSpeed * 0.5f : 0;

        int turn = (int)(_t / 1.3f) % 2;
        _chatWith = o;
        bool speaking = (turn == 0) == _initiator;
        if (turn != _turn)
        {
            _turn = turn;
            if (speaking) f.Emote(PickChatBit(o), 1.1f);
        }
        f.SetAction(speaking ? Act.Talk : Act.Stand);
        if (_t > _dur)
        {
            AddAffinity(o, (0.08f + P.Sociability * 0.05f) * (1 + MathF.Max(0, f.Tastes.Similarity(o.Tastes))));
            if (AffinityWith(o) > 0.5f) f.Emote("♥", 1.2f);
            Cheered(0.2f);
            Go(G.Idle, 1.5f);
        }
    }

    static string Symbol(Thing t) => t switch
    {
        Thing.PlayingBall or Thing.SoccerBalls or Thing.Juggling => "⚽", Thing.Basketballs => "🏀", Thing.Dancing => "♫",
        Thing.Climbing or Thing.HighPlaces => "⛰", Thing.Fighting or Thing.Sparring => "⚔", Thing.Napping => "z",
        Thing.Tricks => "★", Thing.Exploring => "?", Thing.YourCursor => "➤", _ => "♪",
    };

    Figure? _chatWith;

    string PickChatBit(Figure o)
    {
        // Talking about something they both love.
        var shared = f.Tastes.SharedLikes(o.Tastes).ToList();
        if (shared.Count > 0 && rng.NextDouble() < 0.4) return Symbol(shared[rng.Next(shared.Count)]);
        float a = AffinityWith(o);
        if (a > 0.5f && rng.NextDouble() < 0.25) return "♥";
        if (Annoyance > 0.5f && rng.NextDouble() < 0.4) return "#@!";
        return ChatBits[rng.Next(ChatBits.Length - 1)];
    }

    static G GoalFor(SocialKind k) => k switch { SocialKind.Chat => G.Chat, SocialKind.Dance => G.DanceWith, _ => G.HighFive };

    /// <summary>Two dance-lovers grooving face to face.</summary>
    void DoDanceWith(World w)
    {
        if (!PartnerOk(w, G.DanceWith)) { _partner = null; Go(G.Idle, 1); return; }
        var o = _partner!;
        FaceTo(o.Base.X);
        f.LookAt = o.Jt[J.Head];
        float gap = MathF.Abs(o.Base.X - f.Base.X);
        f.DesiredVX = gap < 27 * S ? -MathF.Sign(o.Base.X - f.Base.X) * f.WalkSpeed * 0.5f : gap > 40 * S ? MathF.Sign(o.Base.X - f.Base.X) * f.WalkSpeed * 0.6f : 0;
        if (!f.Grounded || f.JumpPending) return;
        if (f.Action != Act.Fidget || f.ActionT >= f.FidgetDur) f.StartFidget(Fidget.Groove);
        if (_t > _nextBubble)
        {
            _nextBubble = _t + rng.Range(1.5f, 3f);
            // Everyone has their own moves: energetic, playful dancers throw in hops and spins.
            double r = rng.NextDouble();
            if (r < P.Playfulness * 0.25f && Stamina > 0.3f) f.RequestFlip(50 * S);
            else if (r < 0.15f + P.Energy * 0.3f) f.RequestJump(new Vector2(0, -(170 + P.Energy * 90) * S), 0.08f);
            else f.Emote(rng.NextDouble() < 0.5 ? "♫" : "♪", 1);
        }
        if (_t > _dur)
        {
            AddAffinity(o, 0.12f);
            Cheered(0.4f);
            Go(G.Cheer, 1);
        }
    }

    void DoHighFive(World w)
    {
        if (!PartnerOk(w, G.HighFive)) { _partner = null; Go(G.Idle, 1); return; }
        var o = _partner!;
        FaceTo(o.Base.X);
        f.LookAt = o.Jt[J.HandN];
        float gap = MathF.Abs(o.Base.X - f.Base.X), want = 2 * f.Arm * 0.62f;
        f.DesiredVX = MathF.Abs(gap - want) > 2 * S && _t < 0.5f ? MathF.Sign(o.Base.X - f.Base.X) * MathF.Sign(gap - want) * f.WalkSpeed * 0.7f : 0;
        f.SetAction(_t > 0.25f ? Act.HighFive : Act.Stand);
        if (_initiator && _t >= 0.62f && _t - World.Dt < 0.62f)
        {
            Vector2 mid = (f.Jt[J.HandN] + o.Jt[J.HandN]) * 0.5f;
            w.Fx.Spark(mid, S, w.Rng);
            f.Emote("♪", 1);
            o.Emote(rng.NextDouble() < 0.5 ? "♪" : "!", 1);
            AddAffinity(o, 0.15f);
            o.Brain.AddAffinity(f, 0.15f);
            Cheered(0.5f);
            o.Brain.Cheered(0.5f);
        }
        if (_t > _dur) Go(P.Playfulness > 0.6f ? G.Cheer : G.Idle, 0.8f);
    }

    void DoFollow(World w)
    {
        if (!PartnerOk(w, G.Follow) || _t > _dur) { _partner = null; Go(G.Idle, 1); return; }
        var o = _partner!;
        f.LookAt = o.Jt[J.Head];
        if (o.Brain._g is G.SitFloor or G.SitEdge) { BeginSitWith(o); return; }
        var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
        var oseg = w.Env.SupportAt(o.Base.X, o.Base.Y, o.GroundHwnd);
        if (!f.Grounded || seg == null || oseg == null) { f.DesiredVX = 0; return; }
        if (!SameSegment(seg, oseg))
        {
            // Partner went somewhere else: go after them, then resume following.
            float left = _dur - _t;
            var who = o;
            Navigate(() => w.Figures.Contains(who) ? who.Base : null, 30 * S, true, () =>
            {
                Go(G.Follow, left);
                _partner = who;
                _initiator = true;
            }, WalkPurpose.Social);
            return;
        }
        float target = o.Base.X - o.Facing * 36 * S;
        _run = MathF.Abs(target - f.Base.X) > 200 * S;
        if (MoveToward(target, 6 * S)) FaceTo(o.Base.X);
    }

    void BeginSitWith(Figure o)
    {
        Go(G.SitWith, rng.Range(8, 20));
        _partner = o;
        _initiator = true;
        _nextBubble = rng.Range(2, 4);
    }

    void DoSitWith(World w)
    {
        if (_partner == null || !w.Figures.Contains(_partner) || _partner.Mode != Mode.Control) { _partner = null; Go(G.Idle, 1); return; }
        var o = _partner;
        bool partnerSitting = o.Brain._g is G.SitFloor or G.SitEdge or G.SitWith;
        if (!partnerSitting && _t > 1) { _partner = null; Go(G.Idle, 1); return; }
        float side = MathF.Sign(f.Base.X - o.Base.X);
        if (side == 0) side = 1;
        float spot = o.Base.X + side * 17 * S;
        if (f.Action != Act.SitFloor && !MoveToward(spot, 3 * S)) return;
        f.DesiredVX = 0;
        f.Facing = o.Action == Act.SitEdge ? o.Facing : (int)-side;
        f.SetAction(Act.SitFloor);
        if (_t > _nextBubble)
        {
            _nextBubble = _t + rng.Range(3, 7);
            f.Emote(AffinityWith(o) > 0.4f && rng.NextDouble() < 0.3 ? "♥" : ChatBits[rng.Next(4)], 1.2f);
            AddAffinity(o, 0.03f);
        }
        if (_t > _dur) Go(G.Idle, 1.5f);
    }
}
