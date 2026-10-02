using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>Body condition and feelings about comfort. Weight goes up with overeating (a bowl that's always full,
/// lots of treats) and comes down with exercise; heavier animals are slower, tire sooner and jump lower. Stamina
/// drains with running, chasing and flying and comes back with rest (it's a setting: turn it off and they never
/// tire). And comfort matters: a pet who wants to nap and has only the floor will make do, grumpily; a proper bed
/// makes it content (and rests it faster).</summary>
sealed partial class Pet
{
    /// <summary>0 lean … 0.25 fit … 0.5 chubby … 0.75 fat … 1 obese.</summary>
    public float Weight = 0.18f;
    /// <summary>Short-term puff: 1 fresh, 0 worn out (separate from Energy, which is sleepiness).</summary>
    public float Stamina = 1;
    /// <summary>Wanting something it can't have (a bed, a seat, its bowl filled, out).</summary>
    public float Frustration;
    /// <summary>How nice where it's resting is: a bed 1, a soft seat 0.8, the floor 0.3.</summary>
    public float Comfort = 0.5f;

    public static bool StaminaOn = true, WeightOn = true;

    public string WeightWord => Weight < 0.08f ? "Skinny" : Weight < 0.3f ? "Fit" : Weight < 0.5f ? "Chubby" : Weight < 0.75f ? "Fat" : "Obese";
    /// <summary>Speed multiplier from weight and stamina.</summary>
    float BodyPace => HealthPace * (WeightOn ? 1 - 0.45f * MathF.Max(0, Weight - 0.3f) / 0.7f : 1) * (StaminaOn ? 0.55f + 0.45f * MathF.Min(1, Stamina * 1.5f) : 1);
    /// <summary>How wide the body is drawn.</summary>
    float Girth => WeightOn ? 1 + MathF.Max(0, Weight - 0.15f) * 0.75f : 1;

    void UpdateBody(World w, float dt)
    {
        bool exert = _st is State.Zoomies or State.Chase or State.ChasePet or State.Flee or State.Scuffle or State.Play or State.Pounce or State.PounceBird || (Flying && !OnCursor) || (_st == State.Walk && MathF.Abs(Vel.X) > 40 * S);
        bool resting = _st is State.Sleep or State.Sit or State.Petted or State.Groom or State.Perch or State.Ask or State.Idle || Held || OnCursor;
        if (StaminaOn)
        {
            float heavy = WeightOn ? 1 + MathF.Max(0, Weight - 0.3f) * 2.5f : 1;
            if (exert) Stamina = MathF.Max(0, Stamina - dt * (Flying ? 0.005f : 0.028f) * heavy * (Young ? 0.7f : 1));
            else if (resting) Stamina = MathF.Min(1, Stamina + dt * (_st == State.Sleep ? 0.08f : 0.04f) * (0.6f + Comfort * 0.6f));
            else Stamina = MathF.Min(1, Stamina + dt * 0.012f);
            // Out of puff: stop and pant.
            if (Stamina < 0.12f && exert && Grounded && Kind != PetKind.Parrot) { Go(State.Sit, 6); _panting = 6; Log("Ran out of puff"); }
            if (Stamina < 0.12f && Flying && Kind == PetKind.Parrot) PerchSomewhere(w);
        }
        else Stamina = 1;
        _panting = MathF.Max(0, _panting - dt);
        // Weight: exercise burns it off, slowly; a slow metabolism (and age) lets it creep back.
        if (WeightOn)
        {
            if (exert) Weight = MathF.Max(0, Weight - dt * 0.000012f);
            else if (resting && Hunger < 0.2f) Weight = MathF.Min(1, Weight + dt * 0.0000015f);
        }
        // Comfort: what it's sitting or lying on.
        if (_st is State.Sleep or State.Sit or State.Hide)
        {
            Comfort = _thing?.Def.Key == "petbed" && Vector2.Distance(_thing.Pos, Pos) < 30 * S ? 1
                : _thing != null && Vector2.Distance(_thing.Pos, Pos) < 60 * S && (_thing.Def.Verbs.Contains(Verb.Lie) || _thing.Def.Verbs.Contains(Verb.Sit)) ? 0.8f
                : _perchFig != null || Kind == PetKind.Parrot && _thing?.Def.Key == "perch" ? 0.9f
                : 0.3f;
            // Contentment (or a grumble) from where it ended up.
            Stress = MathF.Max(0, Stress - dt * 0.004f * Comfort);
            Frustration = MathF.Max(0, Frustration - dt * 0.006f * Comfort);
            if (Comfort < 0.4f && _st == State.Sleep && Kind != PetKind.Parrot) Frustration = MathF.Min(1, Frustration + dt * 0.0015f);
        }
        else Frustration = MathF.Max(0, Frustration - dt * 0.002f);
        // Asking and not getting: frustration builds.
        if (_st == State.Ask) Frustration = MathF.Min(1, Frustration + dt * 0.01f);
        // Very frustrated pets act out.
        if (Frustration > 0.7f && _st is State.Idle && _rng.NextDouble() < dt * 0.2) { Log("Frustrated"); Go(State.Noise, 2); Misdeed(Habit.Noise, w); Frustration -= 0.2f; }
    }

    float _panting;

    /// <summary>Eating adds weight: more when it wasn't really hungry (free-feeding), and treats add up.</summary>
    void Ate(float amount, bool treat = false)
    {
        if (!WeightOn) return;
        float over = Hunger < 0.25f ? 3 : 1;
        Weight = MathF.Min(1, Weight + amount * (treat ? 0.08f : 0.012f) * over);
    }
}
