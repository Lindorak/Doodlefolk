using System.Numerics;

namespace StickFight;

/// <summary>Holding a weapon: drawn in the near hand along the forearm, swung in fights (the tip becomes the
/// striking point), or aimed and fired.</summary>
sealed partial class Figure
{
    public Item? Weapon;
    /// <summary>Where the brain wants a held gun pointed (null: relaxed).</summary>
    public Vector2? AimAt;
    float _fireCd, _recoil;

    public bool Melee => Weapon is { Def.Ranged: false };
    public bool Armed => Weapon is { Def.Ranged: true };
    public float WeaponReach => Weapon != null && !Weapon.Def.Ranged ? Weapon.Def.Reach * Weapon.Sc : 0;

    public void Equip(Item it)
    {
        DropWeapon(Vector2.Zero);
        Weapon = it;
        it.Holder = this;
        it.Pinned = false;
    }

    public void DropWeapon(Vector2 vel)
    {
        if (Weapon is not { } w) return;
        Weapon = null;
        AimAt = null;
        w.Holder = null;
        w.Vel = vel + new Vector2(Facing * 80 * S, -200 * S);
        w.Spin = Facing * 6;
        w.OnGround = false;
    }

    /// <summary>The held weapon's position and angle follow the near hand (forearm direction).</summary>
    public void SyncWeapon(Item it)
    {
        Vector2 hand = Jt[J.HandN], d = hand - Jt[J.ElbowN];
        if (d.LengthSquared() < 1e-4f) d = new Vector2(Facing, 0);
        it.Pos = hand;
        it.Angle = MathF.Atan2(d.X, -d.Y);
        it.Flip = Facing < 0;
    }

    Vector2 WeaponTip() => Weapon is { } w ? Jt[J.HandN] + Vector2.Normalize(Jt[J.HandN] - Jt[J.ElbowN] + new Vector2(0, 1e-3f)) * w.Def.Reach * w.Sc : Jt[J.HandN];

    void TickWeapon(float dt)
    {
        _fireCd -= dt;
        _recoil = MathF.Max(0, _recoil - dt * 6);
        if (Weapon != null && Mode != Mode.Control && Mode != Mode.GetUp) DropWeapon(JVel[J.HandN] * 0.5f);
    }

    /// <summary>Fire the held gun at <paramref name="target"/> if it's ready. Returns whether it fired.</summary>
    public bool Fire(Vector2 target, World w, bool atCursor)
    {
        if (Weapon is not { Def.Ranged: true } gun || _fireCd > 0 || Mode != Mode.Control) return false;
        _fireCd = gun.Def.FireRate;
        Vector2 muzzle = WeaponTip();
        Vector2 d = target - muzzle;
        float dist = d.Length();
        if (dist < 1) return false;
        bool water = gun.Def.Ammo == Ammo.Water;
        float speed = (water ? 1100 : 1700) * S;
        float t = dist / speed;
        float g = (water ? 1400 : 500) * S;
        // Aim a touch high to make up for the drop, with a little wobble.
        Vector2 v = d / t + new Vector2(0, -0.5f * g * t) + new Vector2(0, _rng.Range(-40, 40) * S);
        w.Projectiles.Add(new Projectile(gun.Def.Ammo, muzzle, v, this, S, atCursor));
        _recoil = water ? 0.2f : 1;
        if (!water) w.Fx.Spark(muzzle, S * 0.5f, w.Rng, 0.4f);
        return true;
    }

    /// <summary>Arm targets while holding a weapon: guns point at the aim point; melee weapons arc through swings.</summary>
    void WeaponArms(ref Vector2 hN, ref float handW, ref Vector2 eN)
    {
        if (Weapon is not { } w) return;
        if (w.Def.Ranged)
        {
            if (AimAt is Vector2 at)
            {
                Vector2 d = at - Jt[J.Neck];
                Vector2 local = new(d.X * Facing, d.Y);
                local = local.LengthSquared() > 1 ? Vector2.Normalize(local) * Arm * 0.97f : new Vector2(Arm * 0.97f, 0);
                hN = local - Vector2.Normalize(local) * _recoil * 3 * S;
                eN = new(0, 1);
                handW = 40;
            }
            return;
        }
        if (Atk is not AttackDef a || a.Foot || a.Kind == AttackKind.FlyingKick) return;
        // Melee swing: raise it back over the shoulder, then chop through.
        float t = AtkT;
        Vector2 back = new(-Arm * 0.25f, -Arm * 0.85f), through = new(Arm * 0.95f, -Arm * 0.1f), after = new(Arm * 0.6f, Arm * 0.55f);
        hN = t < a.Windup ? Vector2.Lerp(new Vector2(Arm * 0.3f, -Arm * 0.2f), back, M.Smooth(t / a.Windup))
           : t < a.Windup + a.Active ? Vector2.Lerp(back, through, M.Smooth((t - a.Windup) / a.Active))
           : Vector2.Lerp(through, after, M.Clamp01((t - a.Windup - a.Active) / (a.Recovery * 0.5f)));
        eN = new(-0.4f, 1);
        handW = 70;
    }
}
