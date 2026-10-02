using System.Numerics;
namespace StickFight;

/// <summary>Bad habits a pet can learn to resist.</summary>
enum Habit { ChaseBirds, ChasePets, FightPets, Scratching, KnockingThings, Noise, Soiling, Chewing }

/// <summary>Good things a pet can be taught (with treats, right after it does them).</summary>
enum PetSkill { Housetrained, Sit, Come, ScratchPost, Talk, RollOver, HighFive, PlayDead, Spin }

/// <summary>Training, the way it works with real animals: timing is everything. A squirt from the spray bottle within a
/// couple of seconds of the misdeed teaches a little (sooner is better); a late one only confuses and upsets them, and
/// spraying for nothing damages trust. Treats work the same way for good behaviour (and, given at the wrong moment,
/// reward the wrong thing). Lessons sink in gradually and need the odd refresher. Parrots, by the way, love a misting:
/// spraying one is a bath, not a telling-off.</summary>
sealed partial class Pet
{
    public readonly Dictionary<Habit, float> Restraint = new();
    public readonly Dictionary<PetSkill, float> Skills = new();
    (Habit h, double at)? _misdeed;
    (PetSkill s, double at)? _goodDeed;
    public int Sprays, TreatsGiven;

    /// <summary>Which habits apply to this animal.</summary>
    public IEnumerable<Habit> Habits => Kind switch
    {
        PetKind.Cat => new[] { Habit.ChaseBirds, Habit.FightPets, Habit.Scratching, Habit.KnockingThings, Habit.Noise, Habit.Soiling },
        PetKind.Dog => new[] { Habit.ChasePets, Habit.ChaseBirds, Habit.FightPets, Habit.Noise, Habit.Soiling },
        PetKind.Rabbit => new[] { Habit.Chewing, Habit.Soiling, Habit.FightPets },
        PetKind.Hamster => new[] { Habit.Chewing },
        _ => new[] { Habit.Noise, Habit.FightPets },
    };

    public IEnumerable<PetSkill> SkillList => Kind switch
    {
        PetKind.Cat => new[] { PetSkill.Housetrained, PetSkill.ScratchPost, PetSkill.Come, PetSkill.Sit, PetSkill.HighFive, PetSkill.Spin },
        PetKind.Dog => new[] { PetSkill.Housetrained, PetSkill.Sit, PetSkill.Come, PetSkill.RollOver, PetSkill.HighFive, PetSkill.PlayDead, PetSkill.Spin },
        PetKind.Rabbit => new[] { PetSkill.Housetrained, PetSkill.Come, PetSkill.Spin },
        PetKind.Hamster => new[] { PetSkill.Come, PetSkill.Spin },
        _ => new[] { PetSkill.Talk, PetSkill.Come, PetSkill.HighFive, PetSkill.Spin },
    };

    public static string HabitGood(Habit h) => h switch
    {
        Habit.ChaseBirds => "Leaves the birds alone", Habit.ChasePets => "Doesn't chase the other pets", Habit.FightPets => "Doesn't start fights",
        Habit.Scratching => "Doesn't scratch the furniture", Habit.KnockingThings => "Doesn't knock things off", Habit.Noise => "Keeps the noise down",
        Habit.Chewing => "Leaves the plants alone",
        _ => "Goes in the right place",
    };

    static string HabitDoing(Habit h) => h switch
    {
        Habit.ChaseBirds => "going after a bird", Habit.ChasePets => "chasing another pet", Habit.FightPets => "fighting", Habit.Scratching => "scratching the furniture",
        Habit.KnockingThings => "knocking something off", Habit.Noise => "making a racket", Habit.Chewing => "chewing the plants", _ => "an accident",
    };

    public static string SkillName(PetSkill s) => s switch
    {
        PetSkill.Housetrained => "House-trained", PetSkill.Sit => "Sits when asked", PetSkill.Come => "Comes when called",
        PetSkill.ScratchPost => "Uses the scratching post", PetSkill.RollOver => "Rolls over", PetSkill.HighFive => "High five", PetSkill.PlayDead => "Plays dead", PetSkill.Spin => "Spins", _ => "Learns words",
    };

    static string SkillDoing(PetSkill s) => s switch
    {
        PetSkill.Housetrained => "going in the right place", PetSkill.Sit => "sitting", PetSkill.Come => "coming when called",
        PetSkill.ScratchPost => "using the scratching post", PetSkill.RollOver => "rolling over", PetSkill.HighFive => "the high five", PetSkill.PlayDead => "playing dead", PetSkill.Spin => "spinning", _ => "talking",
    };

    public float R(Habit h) => Restraint.GetValueOrDefault(h);
    public float Sk(PetSkill s) => Skills.GetValueOrDefault(s);
    /// <summary>How reliably it waits for the right place.</summary>
    float HouseScore => MathF.Max(R(Habit.Soiling), Sk(PetSkill.Housetrained));

    public void ResetTraining() => InitTraining();

    void InitTraining()
    {
        bool young = Age < 0.6f;
        foreach (var h in Enum.GetValues<Habit>()) Restraint[h] = young ? _rng.Range(0, 0.1f) : _rng.Range(0.1f, 0.35f);
        Restraint[Habit.Soiling] = young ? 0.05f : Kind is PetKind.Cat or PetKind.Rabbit ? 0.85f : 0.7f;
        Skills[PetSkill.Housetrained] = young ? 0.1f : Kind == PetKind.Cat ? 0.9f : 0.75f;
        Skills[PetSkill.Sit] = young ? 0 : Kind == PetKind.Dog ? 0.35f : 0.05f;
        Skills[PetSkill.Come] = young ? 0.05f : Kind == PetKind.Cat ? 0.15f : 0.3f;
        Skills[PetSkill.ScratchPost] = Kind == PetKind.Cat ? 0.2f : 0;
        Skills[PetSkill.Talk] = Kind == PetKind.Parrot ? 0.3f : 0;
    }

    /// <summary>Is it going to give in? (Strong urges still win sometimes, even after a lot of training.)</summary>
    bool Tempted(Habit h, float drive) => _rng.NextDouble() < drive * Tm(Temperament.Mischievous, 1.5f) * Tm(Temperament.Easygoing, 0.8f) * (1 - 0.93f * R(h));

    void Misdeed(Habit h, World w)
    {
        _misdeed = (h, World.Now);
        _misdeedCount++;
    }

    int _misdeedCount;

    void GoodDeed(PetSkill s)
    {
        _goodDeed = (s, World.Now);
        Skills[s] = MathF.Min(1, Sk(s) + 0.01f * (1 - Sk(s)));   // practice makes (slightly more) perfect
        if (s == PetSkill.ScratchPost) Restraint[Habit.Scratching] = MathF.Min(1, R(Habit.Scratching) + 0.01f);
        if (s == PetSkill.Housetrained) Restraint[Habit.Soiling] = MathF.Min(1, R(Habit.Soiling) + 0.01f);
    }

    void Learn(Habit h, float amount) => Restraint[h] = MathF.Min(1, R(h) + amount * Tm(Temperament.Mischievous, 0.7f) * Tm(Temperament.Affectionate, 1.2f) * (1 - R(h)));

    void TrainingDecay(float dt)
    {
        // Lessons fade very slowly without the odd reminder (about a tenth of the way in a day).
        foreach (var h in Restraint.Keys.ToArray()) if (h != Habit.Soiling) Restraint[h] = MathF.Max(0, Restraint[h] - Restraint[h] * dt / (10 * 24 * Hour));
    }

    /// <summary>A squirt from the spray bottle. Returns what happened, for you.</summary>
    public string Sprayed(World w)
    {
        Sprays++;
        Wet = 1;
        double now = World.Now;
        w.Fx.Spark(Centre, S * 0.8f, w.Rng, 0.5f, new Vortice.Mathematics.Color4(0.6f, 0.8f, 1, 0.9f));
        if (Kind == PetKind.Parrot)
        {
            Stress = MathF.Max(0, Stress - 0.15f);
            Boredom = MathF.Max(0, Boredom - 0.1f);
            UserBond = MathF.Min(1, UserBond + 0.02f);
            Go(State.Bath, 3.5f);
            Log("Had a lovely misting bath");
            Chirp(w);
            if (_misdeed is { h: Habit.Noise } m && now - m.at < 3) { Learn(Habit.Noise, 0.08f); _misdeed = null; return $"{Name} quietened down for a bath (parrots love a misting)."; }
            return $"{Name} fluffs up happily: parrots love a misting!";
        }
        if (_misdeed is { } md && now - md.at <= 3)
        {
            float q = 1 - (float)(now - md.at) / 3 * 0.6f;
            float before = R(md.h);
            Learn(md.h, 0.16f * q);
            if (before < 0.75f && R(md.h) >= 0.75f) { w.Sticker("trained"); Log($"Learned: {HabitGood(md.h).ToLowerInvariant()}"); }
            Stress = M.Clamp01(Stress + 0.12f);
            UserBond = MathF.Max(-1, UserBond - 0.005f);
            _misdeed = null;
            Startle(w, md.h);
            string pct = $"{R(md.h) * 100:0}%";
            Log($"Sprayed right after {HabitDoing(md.h)}: learning ({pct})");
            return q > 0.8f ? $"Perfect timing! {Name} is learning not to keep {HabitDoing(md.h)} ({pct})." : $"Good. {Name} is learning about {HabitDoing(md.h)} ({pct}). Sooner is even better.";
        }
        if (_misdeed is { } late && now - late.at <= 15)
        {
            Learn(late.h, 0.02f);
            Stress = M.Clamp01(Stress + 0.2f);
            UserBond = MathF.Max(-1, UserBond - 0.03f);
            Startle(w, null);
            Log("Sprayed too late, and didn't understand why");
            return $"Too late: {Name} doesn't connect it with {HabitDoing(late.h)} any more. Spray within a couple of seconds.";
        }
        Stress = M.Clamp01(Stress + 0.3f);
        UserBond = MathF.Max(-1, UserBond - 0.06f);
        Startle(w, null);
        Log("Sprayed for no reason. Scared and upset.");
        return $"{Name} wasn't doing anything wrong. Now they're scared and upset with you.";
    }

    /// <summary>A treat from you. Right after something good, it reinforces it.</summary>
    public string GiveTreat(World w)
    {
        TreatsGiven++;
        double now = World.Now;
        Ate(0.06f, treat: true);
        Hunger = MathF.Max(0, Hunger - 0.06f);
        UserBond = MathF.Min(1, UserBond + 0.04f);
        Stress = MathF.Max(0, Stress - 0.05f);
        World.Play(Sfx.Munch, HeadPos, 0.35f, 1.5f);
        if (_goodDeed is { } g && now - g.at <= 4)
        {
            Skills[g.s] = MathF.Min(1, Sk(g.s) + 0.15f * (1 - Sk(g.s)));
            if (g.s == PetSkill.ScratchPost) Learn(Habit.Scratching, 0.08f);
            if (g.s == PetSkill.Housetrained) Learn(Habit.Soiling, 0.08f);
            _goodDeed = null;
            string pct = $"{Sk(g.s) * 100:0}%";
            Log($"Got a treat for {SkillDoing(g.s)} ({pct})");
            Happy(w);
            return $"Good timing! {Name} links the treat with {SkillDoing(g.s)} ({pct}).";
        }
        if (_misdeed is { } m && now - m.at <= 4)
        {
            Restraint[m.h] = MathF.Max(0, R(m.h) - 0.06f);
            Log($"Got a treat right after {HabitDoing(m.h)}…");
            return $"Oops: that rewarded {HabitDoing(m.h)}.";
        }
        Happy(w);
        Log("Got a treat");
        if (Kind == PetKind.Dog && Grounded && _rng.NextDouble() < 0.5 + Sk(PetSkill.Sit) * 0.4) { Go(State.Trick, 1.6f); Vel = new Vector2(0, -420 * S); Grounded = false; }
        else if (Kind == PetKind.Cat && _rng.NextDouble() < 0.35) { Say(w, false); return $"{Name} sniffs the treat… and eventually eats it. Cats."; }
        else if (Kind == PetKind.Parrot) Speak(w, "thank you!");
        return $"{Name} loved that.";
    }

    /// <summary>A command from you (sit, come). Whether it listens depends on training, mood and species.</summary>
    public string Command(string cmd, World w)
    {
        var skill = cmd == "sit" ? PetSkill.Sit : PetSkill.Come;
        float chance = (0.12f + 0.85f * Sk(skill)) * (Kind == PetKind.Cat ? 0.55f : 1) * (Young ? 0.7f : 1) * (Stress > 0.6f ? 0.5f : 1) * (_st == State.Sleep ? 0.3f : 1);
        if (_rng.NextDouble() > chance)
        {
            Log($"Ignored \"{cmd}\"");
            if (Kind == PetKind.Cat) Facing = w.Cursor.X >= Pos.X ? -1 : 1;
            return Kind == PetKind.Cat ? $"{Name} pretends not to hear you. ({Sk(skill) * 100:0}% trained)" : $"{Name} is too distracted. ({Sk(skill) * 100:0}% trained)";
        }
        if (cmd == "sit")
        {
            if (Kind == PetKind.Parrot || !Grounded) return $"{Name} can't sit right now.";
            Go(State.Sit, 6);
            Facing = w.Cursor.X >= Pos.X ? 1 : -1;
            GoodDeed(PetSkill.Sit);
            Log("Sat when asked");
            return $"{Name} sits! Give a treat now to reinforce it.";
        }
        var under = w.Env.Below(w.Cursor.X, w.Cursor.Y - 2 * S);
        var spot = under != null ? new Vector2(w.Cursor.X, under.Y) : w.Cursor;
        if (Kind == PetKind.Parrot) spot = w.Cursor + new Vector2(0, 20 * S);
        Travel(() => spot, 1.6f, 14 * S, 15, () => { GoodDeed(PetSkill.Come); Log("Came when called"); Go(State.Sit, 3); Happy(w); });
        return $"{Name} is coming! Treat on arrival to reinforce it.";
    }
}
