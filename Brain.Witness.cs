using System.Numerics;

namespace Doodlefolk;

/// <summary>Something one figure (or you, when actor is null) did to another.</summary>
enum SocialAct { Hurt, Help, Kind }

/// <summary>Seeing how others treat each other. Feelings follow who you care about: hurting a friend of mine
/// makes you my enemy, hurting my enemy wins you a little favour, being kind to my friend makes me like you,
/// being chummy with someone I can't stand makes me jealous. Bystanders react visibly too: defend a friend,
/// glare, laugh, cheer, or come over for your attention when you're nice to someone else.</summary>
sealed partial class Brain
{
    float _witnessCd;

    /// <summary>Called by World.Witness for everyone nearby who isn't involved.</summary>
    public void Saw(Figure? actor, Figure target, SocialAct act, float mag, World w)
    {
        if (_g == G.Sleep || f.Mode != Mode.Control || target == f || actor == f) return;
        float aT = AffinityWith(target);

        if (actor == null)
        {
            // You did it.
            if (act == SocialAct.Hurt) WitnessUserHurt(target, mag);
            else
            {
                if (aT > 0.3f) FeelUser(0.012f * mag, $"Was nice to {target.Name}");
                else if (aT < -0.3f) FeelUser(-0.008f * mag, $"Fussed over {target.Name}");
                // Your biggest fans get jealous when you dote on someone else.
                if (UserFondness > 0.5f && P.Sociability > 0.45f && _witnessCd <= 0 && rng.NextDouble() < 0.25 && _g is G.Idle or G.Watch or G.Walk)
                {
                    _witnessCd = 12;
                    f.Emote(rng.NextDouble() < 0.5 ? "hmph" : "!", 1.1f);
                    ComeToCursor(w);
                }
            }
            return;
        }

        // Balance: the actor is judged by what it did to someone we care (or don't care) about.
        float sign = act == SocialAct.Hurt ? -1 : 1;
        float k = act == SocialAct.Hurt ? 0.12f : act == SocialAct.Help ? 0.08f : 0.04f;
        AddAffinity(actor, sign * aT * k * mag);
        // Teaming up on someone we both dislike bonds us.
        if (act == SocialAct.Hurt && aT < -0.3f) AddAffinity(actor, 0.03f * mag);
        if (RomanceSaw(actor, target, act, w)) return;

        if (_witnessCd > 0 || _g is not (G.Idle or G.Watch or G.Walk or G.SitFloor or G.SitEdge or G.Chat)) return;
        float aA = AffinityWith(actor);
        if (act == SocialAct.Hurt && aT > 0.4f && mag >= 0.5f)
        {
            _witnessCd = 6;
            // Somebody's picking on my friend.
            if (Rules.Enabled && P.Bravery > 0.5f && P.Aggression > 0.45f && aA < 0.3f && !InFight && rng.NextDouble() < 0.45)
            {
                f.Emote("#@!", 1.2f);
                Engage(actor, RelationTo(actor) is Relation.Friends or Relation.Rivals, w);
            }
            else
            {
                f.Emote("!", 1);
                _glareAt = actor;
                Go(G.Annoyed, 1.6f);
            }
        }
        else if (act == SocialAct.Hurt && aT < -0.3f && P.Aggression > 0.4f)
        {
            _witnessCd = 5;
            f.Emote("ha", 1);
        }
        else if (act != SocialAct.Hurt && aT > 0.5f && aA < -0.2f)
        {
            _witnessCd = 8;
            // My friend is being all friendly with *them*?
            f.Emote("…", 1.1f);
            _glareAt = actor;
            Go(G.Annoyed, AnnoyedFor);
        }
        else if (act != SocialAct.Hurt && aT > 0.5f && rng.NextDouble() < 0.3)
        {
            _witnessCd = 6;
            f.Emote("♥", 0.9f);
        }
    }
}

sealed partial class World
{
    /// <summary>Let everyone nearby see <paramref name="actor"/> (null: the user) do <paramref name="act"/> to
    /// <paramref name="target"/>. <paramref name="mag"/>: how big a deal it was (0..1).</summary>
    public void Witness(Figure? actor, Figure target, SocialAct act, float mag)
    {
        foreach (var o in Figures.ToArray())
            if (o != actor && o != target && Vector2.Distance(o.Base, target.Base) < 900 * Scale)
                o.Brain.Saw(actor, target, act, mag, this);
    }
}
