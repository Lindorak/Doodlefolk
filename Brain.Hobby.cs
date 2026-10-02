namespace Doodlefolk;

/// <summary>What a figure loves to do in its spare time.</summary>
enum Hobby { None, Gardening, Collecting, Storytelling }

sealed partial class Brain
{
    Hobby? _hobby;

    /// <summary>Picked once from its nature (gentle souls garden, the curious collect, the chatty tell stories);
    /// changeable in the Studio.</summary>
    public Hobby Hobby
    {
        get => _hobby ??= PickHobby();
        set => _hobby = value;
    }

    Hobby PickHobby()
    {
        float garden = (1 - P.Energy) * 0.6f + (1 - P.Aggression) * 0.4f;
        float collect = P.Curiosity * 0.9f + P.Playfulness * 0.2f;
        float story = P.Sociability * 0.7f + P.Playfulness * 0.3f;
        float none = 0.45f;
        float roll = (float)rng.NextDouble() * (garden + collect + story + none);
        if ((roll -= garden) < 0) return Hobby.Gardening;
        if ((roll -= collect) < 0) return Hobby.Collecting;
        if ((roll -= story) < 0) return Hobby.Storytelling;
        return Hobby.None;
    }

    public static string HobbyName(Hobby h) => h switch { Hobby.Gardening => "gardening", Hobby.Collecting => "collecting trinkets", Hobby.Storytelling => "telling stories", _ => "" };
}
