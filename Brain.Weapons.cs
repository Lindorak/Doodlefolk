using System.Numerics;

namespace StickFight;

/// <summary>Weapons in the brain: who picks them up, shooting from range in a fight, hunters shooting your
/// cursor, and getting soaked by a water gun.</summary>
sealed partial class Brain
{
    float _splashCd;

    float WeaponWant(Item it, Verb v)
    {
        if (f.Weapon != null || it.Holder != null) return 0;
        if (v == Verb.Shoot)
            return (P.Aggression * 0.4f + P.Playfulness * 0.3f + (f.Hunter ? 6 : 0) + (InFight ? 2 : 0)) * (it.Def.Ammo == Ammo.Water ? Taste(Thing.Tricks) : Taste(Thing.Fighting));
        // Pillows are for pillow fights (playful, social); swords and bats for the feisty.
        return it.Def.Damage < 0.3f
            ? P.Playfulness * 0.35f * Taste(Thing.Sparring)
            : (P.Aggression * 0.5f + (InFight ? 3 : 0) + (f.Hunter ? 1 : 0)) * Taste(Thing.Fighting);
    }

    /// <summary>Picked up a weapon: a little flourish.</summary>
    void Equipped(Item it)
    {
        f.Equip(it);
        f.Emote(it.Def.Ranged ? (it.Def.Ammo == Ammo.Water ? "♪" : "!") : P.Aggression > 0.6f ? "#@!" : "!", 0.9f);
        _item = null;
        Go(G.Idle, rng.Range(0.6f, 1.2f));
    }

    /// <summary>In a fight with a gun: keep some distance and shoot. Returns true if it handled this frame.</summary>
    bool ShootFoe(Figure o, float d, float dx, World w)
    {
        if (!f.Armed || d < 55 * S) { f.AimAt = null; return false; }
        float want = 230 * S;
        f.DesiredVX = d < want - 70 * S ? -MathF.Sign(dx) * f.WalkSpeed : d > want + 140 * S ? MathF.Sign(dx) * f.WalkSpeed * 1.2f : 0;
        f.KeepFacing = true;
        FaceTo(o.Base.X);
        Vector2 aim = o.Jt[J.Neck] + (o.Jt[J.Pelvis] - o.Jt[J.Neck]) * 0.4f;
        f.AimAt = aim;
        if (f.Fire(aim + o.Vel * 0.15f, w, false) && rng.NextDouble() < 0.08) f.Emote(rng.NextDouble() < 0.5 ? "pew" : "!", 0.6f);
        return true;
    }

    /// <summary>Hunting with a gun: plant and fire at the cursor. Returns true if it handled this frame.</summary>
    bool ShootCursor(World w)
    {
        Vector2 cur = w.Cursor;
        if (!f.Armed || !Rules.Enabled || !Rules.PunchCursor || Vector2.Distance(cur, f.Jt[J.Neck]) > 950 * S || !f.Grounded) { f.AimAt = null; return false; }
        f.DesiredVX = 0;
        FaceTo(cur.X);
        f.SetAction(Act.Stand);
        f.AimAt = cur;
        if (f.Fire(cur + w.CursorVel * 0.12f, w, true) && rng.NextDouble() < 0.06) f.Emote(rng.NextDouble() < 0.5 ? "pew" : "ha", 0.6f);
        return true;
    }

    /// <summary>Hit by a squirt of water.</summary>
    public void OnSplashed(Figure? by, World w)
    {
        if (f.Mode != Mode.Control) return;
        _splashCd -= 0.1f;
        if (_splashCd > 0) return;
        _splashCd = 1.5f;
        bool fun = P.Playfulness > 0.55f || f.Tastes.Likes(Thing.Tricks);
        if (by != null && by != f) AddAffinity(by, fun ? 0.01f : -0.03f);
        if (fun) { f.Emote(rng.NextDouble() < 0.5 ? "!!" : "ha", 0.9f); Cheered(0.05f); }
        else
        {
            f.Emote(P.Aggression > 0.5f ? "#@!" : "!", 0.9f);
            Annoyance = M.Clamp01(Annoyance + 0.05f);
            if (by != null && _g is G.Idle or G.Watch) { _glareAt = by; Go(G.Annoyed, 1); }
        }
    }
}
