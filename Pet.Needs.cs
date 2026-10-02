using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>What a pet is asking for (shown in a little thought bubble).</summary>
enum PetNeed { None, Food, Water, Potty, Walk, Attention, Play, Sleep }

/// <summary>Needs and care. Everything runs on real time (scaled by the care setting): a full bowl lasts a few
/// hours, the bathroom calls every couple of hours (kittens and puppies more often), and when you're away it all
/// carries on, gently (nothing is allowed to get desperate while the app is closed).</summary>
sealed partial class Pet
{
    /// <summary>How quickly needs build (relaxed 0.5, normal 1, realistic 1.6) and whether pets can have accidents.</summary>
    public static float CarePace = 1;
    public static bool Accidents = true;

    public float Hunger = 0.2f, Thirst = 0.2f, Bladder = 0.2f, Bowel = 0.1f, Energy = 1, Attention = 0.3f, Boredom = 0.3f, Stress, Wet;
    public PetNeed Want;

    const float Hour = 3600;

    public float Happiness => M.Clamp01(1 - (0.3f * Hunger * Hunger + 0.3f * Thirst * Thirst + 0.2f * MathF.Max(Bladder, Bowel) * MathF.Max(Bladder, Bowel)
                                              + 0.22f * Attention * Attention + 0.15f * Boredom * Boredom + 0.45f * Stress + (Energy < 0.15f ? 0.1f : 0)));

    public string Mood => Stress > 0.6f ? "Frightened" : Stress > 0.35f ? "Stressed" : Hunger > 0.8f ? "Starving" : Thirst > 0.8f ? "Very thirsty"
        : MathF.Max(Bladder, Bowel) > 0.85f ? "Desperate for the bathroom" : Energy < 0.15f ? "Exhausted" : Attention > 0.8f ? "Lonely" : Boredom > 0.8f ? "Bored"
        : Happiness > 0.8f ? "Very happy" : Happiness > 0.6f ? "Content" : Happiness > 0.4f ? "A bit fed up" : "Unhappy";

    float GrowSeconds => (Kind == PetKind.Parrot ? 3 : 6) * Hour;

    void UpdateNeeds(World w, float dt)
    {
        float p = CarePace * dt;
        float young = Young ? 1 + (1 - Age) * 0.8f : 1;
        bool asleep = _st == State.Sleep;
        Hunger = M.Clamp01(Hunger + p / (3 * Hour) * young * (Kind == PetKind.Parrot ? 1.2f : 1));
        Thirst = M.Clamp01(Thirst + p / (2.5f * Hour) * (_st is State.Zoomies or State.Chase or State.ChasePet ? 3 : 1));
        if (Kind is not (PetKind.Parrot or PetKind.Hamster)) Bladder = MathF.Min(1, Bladder + p / ((Kind is PetKind.Cat or PetKind.Rabbit ? 2 : 2.5f) * Hour) * young * (asleep ? 0.5f : 1));
        Bowel = MathF.Min(1, Bowel + p / (Kind is PetKind.Parrot or PetKind.Hamster ? 0.7f * Hour : Kind == PetKind.Rabbit ? 3 * Hour : 6 * Hour) * young * (asleep ? 0.5f : 1));
        Attention = M.Clamp01(Attention + p / (1.2f * Hour) * (Kind switch { PetKind.Dog => 1.2f, PetKind.Cat => 0.7f, _ => 1.5f }) * (asleep ? 0.2f : 1));
        Boredom = M.Clamp01(Boredom + p / (1.5f * Hour) * young * (asleep ? 0.2f : 1));
        float busy = _st is State.Zoomies or State.Chase or State.ChasePet or State.Play or State.Flee or State.Scuffle ? 4 : Flying ? 2 : 1;
        Energy = M.Clamp01(Energy + (asleep ? dt * 0.012f * (Young ? 1.4f : 1) : -dt / (1.4f * Hour) * busy * (Young ? 1.5f : 1)));
        Stress = MathF.Max(0, Stress - dt * 0.006f * (asleep || _st == State.Petted ? 3 : 1));
        Wet = MathF.Max(0, Wet - dt * 0.04f);
        TrainingDecay(dt);
        UpdateBody(w, dt);
        UpdateHealth(w, dt);
        UpdateBreeding(w, dt);
        TickPouch(dt);
        if (Young)
        {
            float before = Age;
            Age = MathF.Min(1, Age + dt / GrowSeconds);
            if (before < 0.6f && Age >= 0.6f) Log($"Growing up fast: not a {Species()} for much longer");
            if (Age >= 1) { Log($"All grown up! A proper {Species(false)} now."); w.Sticker("grownup"); w.News("pets", $"{Name} is all grown up", 2); }
        }
        if (!Accidents) { if (Bladder >= 1 || Bowel >= 1) Stress = MathF.Min(1, Stress + dt * 0.01f); return; }
        if ((Bladder >= 1 || Bowel >= 1) && _st is not (State.Potty or State.Accident) && !Held && !OnCursor && (Grounded || Kind == PetKind.Parrot)) Accident(w);
    }

    /// <summary>Couldn't hold it any longer (or a parrot just being a parrot).</summary>
    void Accident(World w)
    {
        bool tiny = Kind is PetKind.Parrot or PetKind.Hamster;
        bool pee = !tiny && Bladder >= Bowel;
        // Parrots on their perch: there's a tray for that.
        bool caught = Kind == PetKind.Parrot && Grounded && w.Items.Any(i => i.Def.Key == "perch" && MathF.Abs(i.Pos.X - Pos.X) < 30 * S);
        if (!caught && w.MakeItem?.Invoke(tiny ? "dropping" : pee ? "puddle" : "poop") is { } mess)
        {
            float y = Grounded ? Pos.Y : w.Env.Below(Pos.X, Pos.Y)?.Y ?? Pos.Y;
            mess.Pos = new Vector2(Pos.X - Facing * Length * 0.3f, y);
            mess.Vel = Vector2.Zero;
            mess.OnGround = false;
        }
        if (pee) Bladder = 0; else Bowel = 0;
        if (tiny) return;
        Misdeed(Habit.Soiling, w);
        string where = w.Env.SupportAt(Pos.X, Pos.Y, GroundHwnd) is { Solid: true } ? "the floor" : "a window";
        Log(pee ? $"Had an accident on {where}" : $"Pooped on {where}");
        Stress = M.Clamp01(Stress + 0.1f);
        Go(State.Accident, 2.2f);
        _pose = pee && Kind == PetKind.Dog ? Pose.LegLift : Pose.Squat;
    }

    /// <summary>The app was closed for a while: needs carry on, but never past "quite needy".</summary>
    public void TimeAway(double hours)
    {
        if (hours < 0.05) return;
        float h = (float)hours * CarePace;
        Hunger = MathF.Min(0.85f, Hunger + h / 3);
        Thirst = MathF.Min(0.85f, Thirst + h / 2.5f);
        if (Kind != PetKind.Parrot) Bladder = MathF.Min(0.8f, Bladder + h / 2.5f);
        Bowel = MathF.Min(0.8f, Bowel + h / 6);
        Attention = MathF.Min(0.9f, Attention + h / 1.5f);
        Boredom = MathF.Min(0.8f, Boredom + h / 2);
        Energy = MathF.Min(1, Energy + h * 0.3f);
        if (hours >= 1) Log($"You were away {(hours >= 2 ? $"{hours:0} hours" : "an hour")}. Missed you.");
    }

    // ---------------- care log ----------------

    public readonly List<(DateTime When, string Text)> CareLog = new();

    public void Log(string text)
    {
        if (CareLog.Count > 0 && CareLog[^1].Text == text && (DateTime.Now - CareLog[^1].When).TotalMinutes < 5) return;
        CareLog.Add((DateTime.Now, text));
        if (CareLog.Count > 60) CareLog.RemoveAt(0);
        World.Log($"pet {Name}: {text}");
    }

    // ---------------- care items ----------------

    static bool Mess(Item i) => i.Def.Key is "puddle" or "poop" or "dropping";

    /// <summary>The nearest care item of a kind (food bowl, litter box…) it could get to.</summary>
    Item? Nearest(World w, string key, Func<Item, bool>? ok = null) =>
        w.Items.Where(i => i.Def.Key == key && i.Holder == null && (ok == null || ok(i)) && Vector2.Distance(i.Pos, Pos) < 3000 * _s)
               .OrderBy(i => Vector2.Distance(i.Pos, Pos)).FirstOrDefault();

    /// <summary>Where to stand to use something on the floor beside it.</summary>
    Vector2 Beside(Item it) => new(it.Pos.X + MathF.Sign(Pos.X - it.Pos.X == 0 ? 1 : Pos.X - it.Pos.X) * (it.Def.W * it.Sc * 0.5f + Length * 0.45f), it.Pos.Y);
}
