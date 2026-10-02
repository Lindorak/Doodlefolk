using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

enum Illness { None, Cold, Tummy, Fleas, Sprain }

/// <summary>Health and hygiene. Neglect (going hungry or thirsty for long), stress, fights and bad falls wear health
/// down; an unwell or grubby pet can catch something: a cold (sneezing, tired), an upset tummy (more accidents, off its
/// food), fleas (scratching, worse when it's dirty) or a sprain (a limp). Good care heals in a few hours; the vet
/// cures it at once (cats are not fans, and some come home in a cone). Cats keep themselves clean; dogs get grubby,
/// especially on rainy walks, and need a bath now and then (cats will not thank you for one). Brushing helps everyone.</summary>
sealed partial class Pet
{
    public float Health = 1, Clean = 1;
    public Illness Sick;
    float _sickFor, _illCheck = 300, _sneezeAt;
    double _coneUntil;
    public bool InCone => World.Now < _coneUntil;

    public string IllnessName => Sick switch { Illness.Cold => "a cold", Illness.Tummy => "an upset tummy", Illness.Fleas => "fleas", Illness.Sprain => "a sprained leg", _ => "" };

    void UpdateHealth(World w, float dt)
    {
        bool neglected = Hunger > 0.9f || Thirst > 0.9f;
        if (neglected) Health -= dt / (2 * Hour);
        if (Stress > 0.7f) Health -= dt / (6 * Hour);
        if (!neglected && Stress < 0.5f && Sick == Illness.None) Health += dt / (3 * Hour);
        Health = M.Clamp01(Health);
        // Getting grubby (cats groom it off themselves).
        float dirt = dt / (10 * Hour) * (_st == State.Walk ? 3 : 1) * (Kind == PetKind.Cat ? 0.5f : 1);
        if (_st == State.Walk && w.Weather.Raining) dirt += dt * 0.004f;
        Clean = M.Clamp01(Clean - dirt + (_st == State.Groom ? dt * 0.01f : 0));
        // Catching something, now and then (likelier when run down or grubby).
        _illCheck -= dt;
        if (_illCheck <= 0)
        {
            _illCheck = 600;
            if (Sick == Illness.None && _rng.NextDouble() < 0.01 + (1 - Health) * 0.15 + (Clean < 0.25f ? 0.08 : 0))
            {
                var options = new List<Illness> { Illness.Tummy };
                if (Wet > 0.1f || w.Weather.Raining || w.Weather.Snowing) options.Add(Illness.Cold);
                if (Clean < 0.4f && Kind != PetKind.Parrot) { options.Add(Illness.Fleas); options.Add(Illness.Fleas); }
                if (Kind == PetKind.Parrot) options.Add(Illness.Cold);
                FallIll(options[_rng.Next(options.Count)], w);
            }
        }
        if (Sick == Illness.None) return;
        _sickFor += dt;
        // Symptoms.
        switch (Sick)
        {
            case Illness.Cold:
                Energy = MathF.Max(0, Energy - dt / (2 * Hour));
                if (_t0 > _sneezeAt) { _sneezeAt = _t0 + _rng.Range(20, 70); World.Play(Sfx.Sneeze, HeadPos, 0.25f, 1 / MathF.Sqrt(S / _s)); Shout("achoo!"); }
                break;
            case Illness.Tummy:
                Bowel = MathF.Min(1, Bowel + dt / (1.5f * Hour));
                Hunger = MathF.Max(0, Hunger - dt / (8 * Hour));   // off its food
                break;
            case Illness.Fleas:
                Stress = MathF.Min(1, Stress + dt / (4 * Hour));
                if (_st is State.Idle or State.Sit && _rng.NextDouble() < dt * 0.15) { Go(State.Groom, 2); Shout("*scratch*"); }
                break;
        }
        // Getting better with good care (fleas need treating).
        bool cared = Hunger < 0.6f && Thirst < 0.6f && Stress < 0.5f && Health > 0.5f;
        if (Sick != Illness.Fleas && cared && _sickFor > (Sick == Illness.Sprain ? 1.5f : 2.5f) * Hour) Recover("Feeling better");
    }

    void FallIll(Illness ill, World w)
    {
        Sick = ill;
        _sickFor = 0;
        Health = MathF.Max(0, Health - 0.15f);
        Log($"Came down with {IllnessName}");
        w.News("pets", $"{Name} is under the weather ({IllnessName})", 1);
    }

    void Recover(string why)
    {
        Log($"{why}: over {IllnessName}");
        Sick = Illness.None;
        _sickFor = 0;
    }

    /// <summary>A bad landing or a scrap can leave it limping.</summary>
    void MaybeHurt(World w, float severity)
    {
        Health = MathF.Max(0, Health - 0.08f * severity);
        if (Sick == Illness.None && _rng.NextDouble() < 0.25 * severity) FallIll(Illness.Sprain, w);
    }

    /// <summary>Off to the vet: cured, checked over, and (for an upset tummy or a sprain) sent home in a cone.</summary>
    public string Vet(World w)
    {
        string what = IllnessName;
        bool wasSick = Sick != Illness.None;
        if (wasSick) Recover("Went to the vet");
        Health = 1;
        if (Kind == PetKind.Cat) { Stress = M.Clamp01(Stress + 0.35f); UserBond = MathF.Max(-1, UserBond - 0.03f); }
        else Stress = M.Clamp01(Stress + 0.1f);
        if (wasSick && what is "an upset tummy" or "a sprained leg") _coneUntil = World.Now + 180;
        Log(wasSick ? $"Vet visit: treated for {what}" : "Vet check-up: healthy");
        w.Fx.Spark(Centre, S, w.Rng, 0.5f, new Color4(0.6f, 0.9f, 1, 1));
        return wasSick ? $"{Name} is all better (treated for {what})." + (Kind == PetKind.Cat ? " Not impressed with the vet, though." : "") : $"{Name} had a check-up: healthy as can be.";
    }

    /// <summary>Bath time: squeaky clean. Dogs might love it; cats certainly won't.</summary>
    public string Bath(World w)
    {
        Clean = 1;
        Wet = 1;
        Go(State.Bath, 4);
        if (Kind == PetKind.Cat) { Stress = M.Clamp01(Stress + 0.4f); UserBond = MathF.Max(-1, UserBond - 0.04f); Say(w, true); Log("Had a bath (furious about it)"); return $"{Name} is clean… and absolutely furious."; }
        bool loves = Kind == PetKind.Parrot || (Id % 2 == 0);
        if (loves) { Stress = MathF.Max(0, Stress - 0.1f); Happy(w); Log("Had a bath (loved it)"); return $"{Name} loved the bath!"; }
        Stress = M.Clamp01(Stress + 0.15f);
        Log("Had a bath (put up with it)");
        return $"{Name} is clean (and shaking water everywhere).";
    }

    /// <summary>A good brush: cleaner, calmer, closer.</summary>
    public string Brush(World w)
    {
        Clean = MathF.Min(1, Clean + 0.3f);
        Stress = MathF.Max(0, Stress - 0.1f);
        UserBond = MathF.Min(1, UserBond + 0.03f);
        Attention = MathF.Max(0, Attention - 0.2f);
        Go(State.Petted, 3);
        Log("Got brushed");
        return Kind == PetKind.Cat ? $"{Name} purrs through the whole brush." : $"{Name} leans into the brush.";
    }

    /// <summary>Grubby patches, fleas hopping, a cone: drawn over the animal.</summary>
    void DrawHealth(Renderer r)
    {
        float s = S;
        if (Clean < 0.45f && Kind != PetKind.Parrot)
        {
            var mud = new Color4(0.42f, 0.3f, 0.18f, 0.55f * (1 - Clean / 0.45f) + 0.2f);
            for (int i = 0; i < 4; i++) r.Oval(Pos + new Vector2(Facing * (-0.3f + i * 0.18f) * Length, -Height * (0.15f + (i % 2) * 0.12f)), 2.4f * s, 1.5f * s, mud);
        }
        if (Sick == Illness.Fleas)
            for (int i = 0; i < 4; i++)
            {
                float ph = (_tail * 3 + i * 0.37f) % 1;
                r.Disc(Centre + new Vector2(MathF.Sin(i * 7.1f + _tail * 5) * Length * 0.4f, -ph * 8 * s - Height * 0.2f), 0.8f * s, new Color4(0.1f, 0.1f, 0.1f, 0.9f));
            }
        if (Sick == Illness.Cold && Kind != PetKind.Parrot) r.Disc(HeadPos + new Vector2(Facing * Height * 0.42f, Height * 0.12f), 0.9f * s, new Color4(0.6f, 0.85f, 1, 0.8f));
        if (Sick == Illness.Sprain && Grounded && Kind != PetKind.Parrot) r.Line(Pos + new Vector2(Facing * Length * 0.28f, -Height * 0.25f), Pos + new Vector2(Facing * Length * 0.28f, -Height * 0.05f), new Color4(0.97f, 0.97f, 0.95f, 1), 3.4f * s);   // a bandage
        if (InCone && Kind != PetKind.Parrot)
        {
            Vector2 h = HeadPos;
            float hr = Height * 0.33f;
            var cone = new Color4(0.95f, 0.95f, 0.98f, 0.55f);
            r.FillPolygon(stackalloc Vector2[] { h + new Vector2(-Facing * hr * 0.4f, -hr * 0.9f), h + new Vector2(Facing * hr * 1.7f, -hr * 1.9f), h + new Vector2(Facing * hr * 1.7f, hr * 1.9f), h + new Vector2(-Facing * hr * 0.4f, hr * 0.9f) }, cone);
            r.Line(h + new Vector2(Facing * hr * 1.7f, -hr * 1.9f), h + new Vector2(Facing * hr * 1.7f, hr * 1.9f), Ui.Ink.A(0.5f), 1 * s);
        }
        if (_st == State.Bath && Kind != PetKind.Parrot)
            for (int i = 0; i < 7; i++)
            {
                float a = i * 0.9f + _tail * 1.5f;
                r.Ring(Centre + new Vector2(MathF.Cos(a) * Length * 0.5f, MathF.Sin(a * 1.3f) * Height * 0.5f - Height * 0.2f), (1.6f + (i % 3)) * s, new Color4(0.75f, 0.9f, 1, 0.8f), 0.7f * s);
            }
    }

    /// <summary>A sprain or a cold slows it down.</summary>
    float HealthPace => Sick switch { Illness.Sprain => 0.55f, Illness.Cold => 0.85f, _ => 1 } * (0.7f + 0.3f * Health);
}
