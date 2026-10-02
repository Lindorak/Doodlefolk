using System.Numerics;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>A club: three or more friends who all like each other start one, named after what they love doing
/// together. Members wear the club's colour as a bandana, meet up, and cheer each other on. Friends of the club can
/// join later; anyone who falls out with the rest leaves, and a club of one is no club at all.</summary>
sealed class Club
{
    public string Name = "";
    public Color4 Colour;
    public readonly List<int> Members = new();
    public Thing Thing;
    public DateTime Founded = DateTime.Now;

    public IEnumerable<Figure> People(World w) => w.Figures.Where(f => Members.Contains(f.Id));

    static readonly Dictionary<Thing, string[]> Names = new()
    {
        [Thing.Dancing] = new[] { "The Groove Crew", "The Dance Squad", "Two Left Feet" },
        [Thing.PlayingBall] = new[] { "Ball Buddies", "The Bouncers", "Team Bounce" },
        [Thing.Juggling] = new[] { "The Jugglers", "Keepy-Uppy Club" },
        [Thing.Napping] = new[] { "The Nap Club", "Snooze Squad", "The Sleepyheads" },
        [Thing.Climbing] = new[] { "The Sky Climbers", "Ledge Lords" },
        [Thing.HighPlaces] = new[] { "The Rooftop Gang", "Top of the Windows" },
        [Thing.Fighting] = new[] { "The Brawlers", "Fist Club" },
        [Thing.Sparring] = new[] { "The Dojo", "Spar Stars" },
        [Thing.Exploring] = new[] { "The Explorers' Club", "Desktop Wanderers" },
        [Thing.Eating] = new[] { "The Snack Pack", "Lunch Bunch" },
        [Thing.Reading] = new[] { "The Book Club", "Page Turners" },
        [Thing.Tricks] = new[] { "The Flip Squad", "Backflip Brigade" },
        [Thing.Chatting] = new[] { "The Chatterboxes", "Gossip Club" },
        [Thing.Sitting] = new[] { "The Couch Crew", "Sit-Down Society" },
        [Thing.SoccerBalls] = new[] { "FC Desktop", "Taskbar United" },
        [Thing.Basketballs] = new[] { "The Hoopers", "Net Ninjas" },
        [Thing.YourCursor] = new[] { "The Cursor Fan Club", "Friends of the Pointer" },
    };

    public static string NameFor(Thing t, Random rng, IEnumerable<string> taken)
    {
        var options = Names.TryGetValue(t, out var n) ? n : new[] { "The Stick Gang", "The Doodle Crew", "The Sketch Club" };
        var free = options.Where(o => !taken.Contains(o)).ToList();
        return free.Count > 0 ? free[rng.Next(free.Count)] : options[0] + " " + (taken.Count() + 1);
    }
}

sealed partial class World
{
    public readonly List<Club> Clubs = new();
    double _clubCheck;

    public Club? ClubOf(Figure f) => Clubs.FirstOrDefault(c => c.Members.Contains(f.Id));

    static bool Eligible(Figure f) => f.Mode == Mode.Control && !f.Dead && !f.Hunter && !f.Brain.Baby;

    /// <summary>Every minute or so: new clubs among close friends, newcomers joining, members drifting away.</summary>
    public void UpdateClubs(double now)
    {
        if (now < _clubCheck) return;
        _clubCheck = now + 60;
        bool Mutual(Figure a, Figure b, float t) => a.Brain.AffinityWith(b) > t && b.Brain.AffinityWith(a) > t;

        // Leaving (fell out with the rest) and clubs that have dwindled away.
        foreach (var c in Clubs.ToList())
        {
            var people = c.People(this).ToList();
            c.Members.RemoveAll(id => people.All(p => p.Id != id));
            foreach (var p in people)
            {
                var rest = people.Where(o => o != p).ToList();
                if (rest.Count == 0 || rest.Average(o => p.Brain.AffinityWith(o)) >= 0.05f) continue;
                c.Members.Remove(p.Id);
                p.Emote($"I quit {c.Name}!", 1.6f);
                p.Brain.ClubNote($"Left {c.Name}. We just don't get along any more.");
                News("club", $"{p.Name} quits {c.Name}", 2, p);
            }
            if (c.Members.Count < 2)
            {
                Clubs.Remove(c);
                News("club", $"{c.Name} breaks up", 2);
            }
        }

        var free = Figures.Where(f => Eligible(f) && ClubOf(f) == null).ToList();
        // Joining: friends with at least two members.
        foreach (var f in free.ToList())
            foreach (var c in Clubs)
            {
                var people = c.People(this).ToList();
                if (people.Count(p => Mutual(f, p, 0.45f)) < Math.Min(2, people.Count)) continue;
                c.Members.Add(f.Id);
                free.Remove(f);
                f.Emote($"I joined {c.Name}!", 1.6f);
                f.Brain.ClubNote($"Joined {c.Name}!");
                foreach (var p in people) p.Emote(p.Brain.Welcome(f), 1.3f);
                News("club", $"{f.Name} joins {c.Name}", 2, f);
                break;
            }

        // Founding: three who all really like each other.
        for (int i = 0; i < free.Count; i++)
            for (int j = i + 1; j < free.Count; j++)
            {
                if (!Mutual(free[i], free[j], 0.5f)) continue;
                for (int k = j + 1; k < free.Count; k++)
                {
                    if (!Mutual(free[i], free[k], 0.5f) || !Mutual(free[j], free[k], 0.5f)) continue;
                    Found(new[] { free[i], free[j], free[k] });
                    return;
                }
            }
    }

    void Found(Figure[] founders)
    {
        // Named after what they love most, together.
        var thing = Enum.GetValues<Thing>().OrderByDescending(t => founders.Sum(f => f.Tastes.Of(t))).First();
        var c = new Club { Thing = thing, Name = Club.NameFor(thing, Rng, Clubs.Select(x => x.Name)) };
        var palette = Palette.All.Select(p => p.Color).Where(col => Clubs.All(x => x.Colour != col)).ToList();
        c.Colour = palette.Count > 0 ? palette[Rng.Next(palette.Count)] : Palette.All[Rng.Next(Palette.All.Length)].Color;
        c.Members.AddRange(founders.Select(f => f.Id));
        Clubs.Add(c);
        foreach (var f in founders)
        {
            f.Emote($"{c.Name}!", 2);
            f.Brain.ClubNote($"We started a club: {c.Name}! It's me, {string.Join(" & ", founders.Where(o => o != f).Select(o => o.Name))}.");
        }
        News("club", $"New club: {c.Name} ({string.Join(", ", founders.Select(f => f.Name))})", 3, founders);
        Sticker("club");
    }
}

sealed partial class Brain
{
    float _clubMeetCd = 90;

    public void ClubNote(string text) => Write("club", text, "★", 60);

    public string Welcome(Figure who) => V($"welcome, {who.Name}!", $"YAY {who.Name.ToUpperInvariant()}!!", $"…welcome, I guess.", $"h-hi {who.Name}!", $"welcome, {who.Name}");

    /// <summary>Club members meet up now and then: wherever most of them are.</summary>
    void ClubOptions(World w, OptionList opts)
    {
        if (w.ClubOf(f) is not { } c || _clubMeetCd > _t0) return;
        var others = c.People(w).Where(o => o != f && o.Mode == Mode.Control).ToList();
        if (others.Count == 0) return;
        var near = others.OrderBy(o => Vector2.Distance(o.Base, f.Base)).First();
        if (Vector2.Distance(near.Base, f.Base) < 180 * S) return;
        opts.Add(0.35f + P.Sociability * 0.5f + Loneliness * 0.6f, () =>
        {
            _clubMeetCd = _t0 + rng.Range(150, 300);
            float side = MathF.Sign(f.Base.X - near.Base.X);
            Navigate(() => w.Figures.Contains(near) ? near.Base + new Vector2(side * 42 * S, 0) : null, 16 * S, false, () =>
            {
                FaceTo(near.Base.X);
                f.Emote(V($"{c.Name}!", $"{c.Name.ToUpperInvariant()} FOREVER!!", $"{c.Name}. sup.", $"h-hi club!", $"{c.Name} gathers"), 1.4f);
                near.Emote(near.Brain.V("hey!", "CLUB TIME!!", "yo.", "hi!", "well met"), 1.2f);
                AddAffinity(near, 0.03f); near.Brain.AddAffinity(f, 0.03f);
                Loneliness = MathF.Max(0, Loneliness - 0.25f);
                Cheered(0.1f);
                if (f.Tastes.Likes(Thing.Dancing) && near.Tastes.Likes(Thing.Dancing)) { Go(G.Idle, 3); f.StartFidget(Fidget.Groove); }
                else Go(G.Cheer, 1);
            }, WalkPurpose.Social);
        }, $"Meet up with {c.Name}");
    }

    /// <summary>Clubmates get a warm start with each other.</summary>
    float ClubBond(Figure o) => World.Current?.ClubOf(f) is { } c && c.Members.Contains(o.Id) ? 0.15f : 0;
}
