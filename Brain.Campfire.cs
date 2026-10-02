using System.Numerics;

namespace StickFight;

/// <summary>Campfire evenings: at night friends light a fire, gather round, one of them tells a (very desktop) story,
/// and afterwards they sleep over by the embers.</summary>
sealed partial class Brain
{
    Figure? _storyTeller;
    readonly List<string> _story = new();
    int _storyLine;
    float _storyBeat;
    int _fireNight = -1;

    static readonly string[] Monsters = { "a giant spider", "a cursor with teeth", "a talking pizza", "the ghost of a deleted file", "the Recycle Bin monster", "a very angry pop-up", "a blue screen", "a loading wheel that never stopped" };
    static readonly string[] Places = { "the Recycle Bin", "the Start menu", "the very top of the tallest window", "under the taskbar", "a forgotten folder", "the system tray" };
    static readonly string[] Endings = { "shared a snack with it", "danced with it until sunrise", "threw it a ball, and it fetched!", "gave it a hug. it was just lonely.", "ran SO fast they ended up on another monitor", "pressed Ctrl+Z and it was gone" };

    /// <summary>Make up a story: who it's about, where, what lurked there, and how it ended.</summary>
    void MakeStory(World w, List<Figure> listeners)
    {
        _story.Clear();
        var hero = listeners.Count > 0 && rng.NextDouble() < 0.6 ? listeners[rng.Next(listeners.Count)].Name : f.Name;
        var loves = Enum.GetValues<Thing>().OrderByDescending(t => f.Tastes.Of(t)).First();
        string monster = Monsters[rng.Next(Monsters.Length)], place = Places[rng.Next(Places.Length)], end = Endings[rng.Next(Endings.Length)];
        _story.Add(V("once upon a time…", "OKAY OKAY. once upon a time…", "fine. once upon a time…", "um… once upon a time…", "long ago, on a desktop far away…"));
        _story.Add($"there was {(hero == f.Name ? "me" : hero)}, who loved {ThingWords(loves)}");
        _story.Add($"one night, a noise came from {place}…");
        _story.Add($"it was… {monster}!");
        _story.Add($"but {(hero == f.Name ? "I" : hero)} {end}");
        _story.Add(V("the end!", "THE END!!!", "the end. obviously.", "…the end", "and that is the end"));
    }

    /// <summary>Warming up by a fire with company: the storyteller type might start a story.</summary>
    void MaybeStartStory(Item fire, World w)
    {
        if (_storyTeller != null || _t < 4) return;
        var round = w.Figures.Where(o => o != f && o.Brain._g == G.UseItem && o.Brain._item == fire && o.Brain._verb == Verb.Warm && o.Brain._storyTeller == null).ToList();
        if (round.Count == 0) return;
        float chance = (0.02f + P.Sociability * 0.03f + (Hobby == Hobby.Storytelling ? 0.08f : 0)) * (0.5f + w.Night);
        if (rng.NextDouble() > World.Dt * chance) return;
        MakeStory(w, round);
        _storyTeller = f;
        _storyLine = 0;
        _storyBeat = 0.5f;
        _dur = MathF.Max(_dur, _t + _story.Count * 3.8f + 6);
        foreach (var o in round) o.Brain.ListenTo(f);
    }

    void ListenTo(Figure teller)
    {
        _storyTeller = teller;
        _dur = MathF.Max(_dur, _t + 30);
    }

    /// <summary>While warming by the fire: tell the next line, or react to the teller's.</summary>
    void StoryStep(Item fire, World w)
    {
        if (_storyTeller == null) { MaybeStartStory(fire, w); return; }
        if (_storyTeller != f)
        {
            // Listening; if the teller's gone, so is the story.
            if (!w.Figures.Contains(_storyTeller) || _storyTeller.Brain._storyTeller != _storyTeller) _storyTeller = null;
            else f.LookAt = _storyTeller.Jt[J.Head];
            return;
        }
        _storyBeat -= World.Dt;
        f.SetAction(_storyBeat > 2.2f ? Act.Talk : Act.Warm);
        if (_storyBeat > 0) return;
        if (_storyLine >= _story.Count) { EndStory(fire, w); return; }
        string line = _story[_storyLine++];
        f.Emote(line, 3.4f);
        f.SetAction(Act.Talk);
        _storyBeat = 3.8f;
        // The audience reacts to the scary bit, and to the ending.
        bool scary = _storyLine == 4, last = _storyLine == _story.Count;
        foreach (var o in w.Figures)
        {
            if (o == f || o.Brain._storyTeller != f) continue;
            if (scary && rng.NextDouble() < 0.8) o.Emote(o.Brain.P.Bravery < 0.4f ? "eek!" : o.Brain.V("ooh!", "WHOA!!", "pfft.", "eep…", "!"), 1.4f);
            else if (last) o.Emote(o.Brain.V("yay!", "AGAIN!!", "not bad.", "that was nice…", "beautiful"), 1.4f);
        }
    }

    void EndStory(Item fire, World w)
    {
        var round = w.Figures.Where(o => o != f && o.Brain._storyTeller == f).ToList();
        string about = _story.Count > 3 ? _story[3].Replace("it was… ", "") : "something";
        Write("story", V($"Told a campfire story about {about}.", $"I told the BEST story! About {about}!!", $"Told a story about {about}. They loved it. Obviously.", $"I told a story by the fire… about {about}. My voice only shook a little.", $"By the fire I spoke of {about}."), "★", 600);
        foreach (var o in round)
        {
            o.Brain.Write("story:" + f.Name, o.Brain.V($"{f.Name} told a story about {about} by the fire.", $"{f.Name}'s campfire story!! {about}!!! SCARY!", $"{f.Name} told a story about {about}. Eh.", $"{f.Name} told a story about {about}. I hid behind my hands.", $"{f.Name}'s tale of {about}, under the stars."), "★", 600);
            o.Brain.AddAffinity(f, 0.05f); AddAffinity(o, 0.04f);
            o.Brain.Cheered(0.15f);
            o.Brain.Loneliness = MathF.Max(0, o.Brain.Loneliness - 0.3f);
        }
        Cheered(0.2f);
        World.Current?.Sticker("story");
        // Late enough: everyone beds down by the embers.
        if (w.Night > 0.4f && round.Count > 0)
        {
            var all = round.Append(f).ToList();
            string names = string.Join(" & ", round.Select(o => o.Name));
            w.News("sleepover", $"Sleepover by the campfire: {string.Join(", ", all.Select(o => o.Name))}", 2, all.ToArray());
            World.Current?.Sticker("sleepover");
            foreach (var o in all)
            {
                var others = string.Join(" & ", all.Where(x => x != o).Select(x => x.Name));
                o.Brain.Write("sleepover", o.Brain.V($"Sleepover by the campfire with {others}.", $"SLEEPOVER with {others}!!!", $"Slept by the fire with {others}. Fine.", $"We had a sleepover with {others}… I felt safe.", $"Slept under the stars with {others}."), "♥", 3600);
                o.Brain._storyTeller = null;
                o.Brain.LeaveItem();
                o.Emote(o.Brain.V("g'night!", "NIGHT NIGHT!!", "night.", "good night…", "sweet dreams"), 1.6f);
                o.Brain.Go(G.Sleep, rng.Range(60, 140));
            }
            return;
        }
        foreach (var o in round) o.Brain._storyTeller = null;
        _storyTeller = null;
    }

    /// <summary>At night, with friends around and no fire going: light one and call them over.</summary>
    void CampfireOptions(World w, OptionList opts)
    {
        if (w.Night < 0.45f || Baby || f.Hunter || _fireNight == DateTime.Now.DayOfYear || P.Sociability < 0.35f) return;
        if (w.Items.Any(i => i.Def.Key == "campfire" && Vector2.Distance(i.Pos, f.Base) < 1600 * S)) return;
        var friends = w.Figures.Where(o => o != f && o.Mode == Mode.Control && !o.Brain.Asleep && AffinityWith(o) > 0.3f && Vector2.Distance(o.Base, f.Base) < 1200 * S).ToList();
        if (friends.Count == 0) return;
        opts.Add((0.4f + P.Sociability * 0.6f + (Hobby == Hobby.Storytelling ? 0.6f : 0)) * w.Night, () =>
        {
            _fireNight = DateTime.Now.DayOfYear;
            if (w.MakeItem?.Invoke("campfire") is not { } fire) return;
            var seg = w.Env.SupportAt(f.Base.X, f.Base.Y, f.GroundHwnd);
            float x = f.Base.X + f.Facing * 60 * S;
            if (seg != null) x = M.ClampIn(x, seg.X1 + 30 * S, seg.X2 - 30 * S);
            fire.Pos = new Vector2(x, f.Base.Y - 4 * S);
            fire.Vel = Vector2.Zero;
            fire.OnGround = false;
            fire.Temporary = true;
            f.Emote(V("campfire time!", "CAMPFIRE!!! everyone come!", "fire's lit.", "I made a little fire…", "a fire against the dark"), 1.6f);
            UseItem(fire, Verb.Warm, w);
            foreach (var o in friends) o.Brain.CallToFire(fire, w);
        }, "Light a campfire for friends");
    }

    void CallToFire(Item fire, World w)
    {
        if (f.Mode != Mode.Control || !f.Grounded || _g is G.Sleep or G.Fight or G.Sport or G.Game or G.Tourney || InFight) return;
        if (rng.NextDouble() > 0.4 + P.Sociability * 0.5) return;
        f.Emote(V("ooh, a fire!", "CAMPFIRE!!", "…fine, I'm coming.", "a fire! wait for me", "the fire calls"), 1.2f);
        UseItem(fire, Verb.Warm, w);
    }
}
