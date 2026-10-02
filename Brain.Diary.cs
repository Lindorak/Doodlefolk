namespace StickFight;

/// <summary>One line in a figure's diary.</summary>
sealed class DiaryEntry
{
    public DateTime At { get; set; }
    public string Text { get; set; } = "";
    /// <summary>A little mood mark next to the entry (♥, ★, 💔, ☁, ⚡...).</summary>
    public string Mood { get; set; } = "";
}

/// <summary>The diary: first-person notes about the day, written when something worth remembering happens, in the
/// figure's own voice (cheery, grumpy, shy, dreamy or plain), without repeating itself.</summary>
sealed partial class Brain
{
    public readonly List<DiaryEntry> Diary = new();
    readonly Dictionary<string, float> _diaryCd = new();

    enum Voice { Plain, Cheery, Grumpy, Shy, Dreamy }

    Voice MyVoice => P.Aggression > 0.62f ? Voice.Grumpy
        : P.Playfulness > 0.65f && P.Aggression < 0.45f ? Voice.Cheery
        : P.Sociability < 0.4f && P.Bravery < 0.5f ? Voice.Shy
        : P.Curiosity > 0.72f ? Voice.Dreamy
        : Voice.Plain;

    /// <summary>Pick the line that fits this figure's voice (falls back to the plain one).</summary>
    public string V(string plain, string? cheery = null, string? grumpy = null, string? shy = null, string? dreamy = null) => MyVoice switch
    {
        Voice.Cheery when cheery != null => cheery,
        Voice.Grumpy when grumpy != null => grumpy,
        Voice.Shy when shy != null => shy,
        Voice.Dreamy when dreamy != null => dreamy,
        _ => plain,
    };

    /// <summary>Write something down, unless the same kind of thing was written recently.</summary>
    void Write(string key, string text, string mood = "", float cooldown = 300)
    {
        if (_diaryCd.TryGetValue(key, out var until) && _t0 < until) return;
        _diaryCd[key] = _t0 + cooldown;
        Diary.Add(new DiaryEntry { At = DateTime.Now, Text = text, Mood = mood });
        if (Diary.Count > 200) Diary.RemoveRange(0, Diary.Count - 200);
        World.Log($"diary {f.Name}: {text}");
    }

    public void DiaryBorn() =>
        Write("born", V($"I was drawn today! My name is {f.Name}.", $"Hello world!! I'm {f.Name} and I just got drawn!", $"Got drawn today. Name's {f.Name}. Don't make it weird.",
                        $"I was drawn today. I'm {f.Name}. Hi, diary.", $"I was drawn today. Everything is so big. I'm {f.Name}."), "★", 1e9f);

    // ---------------- friends and foes ----------------

    void DiaryFeelings(Figure o, float before, float after)
    {
        if (before < 0.5f && after >= 0.5f)
            Write("friend:" + o.Name, V($"{o.Name} and I are friends now.", $"{o.Name} is my new best friend!!", $"I guess {o.Name} is alright.", $"{o.Name} talked to me. I think we're friends?", $"{o.Name} gets me."), "♥", 1800);
        else if (before > -0.5f && after <= -0.5f)
            Write("foe:" + o.Name, V($"I can't stand {o.Name}.", $"{o.Name} was mean. Not cool.", $"{o.Name}. Ugh. Don't get me started.", $"I'm staying away from {o.Name}.", $"{o.Name} and I will never understand each other."), "⚡", 1800);
    }

    void DiaryFightWon(Figure o, bool spar) => Write("won:" + o.Name, spar
        ? V($"Sparred with {o.Name} and won.", $"Beat {o.Name} at sparring! Good fun!", $"Sparred with {o.Name}. Easy.", $"Sparred with {o.Name}. I won, somehow.")
        : V($"Won a fight against {o.Name}.", $"Won a fight against {o.Name}. Didn't love it though.", $"Flattened {o.Name}. They had it coming.", $"Had to fight {o.Name}. I won. My hands are shaking."), spar ? "★" : "⚔", 600);

    public void DiaryKnockedOut(Figure? by) => Write("ko", by != null
        ? V($"Got knocked out by {by.Name}.", $"{by.Name} knocked me out. Ouch! Rematch later?", $"{by.Name} got a lucky hit. Next time.", $"{by.Name} knocked me out. I want to go home.")
        : V("Got knocked out today.", "Got knocked out! Seeing stars!", "Got knocked out. Not talking about it."), "☁", 600);

    // ---------------- you ----------------

    void DiaryThrown(float k) => Write("thrown", UserFondness > 0.4f || f.Tastes.Likes(Thing.BeingThrown)
        ? V("You threw me across the screen. Wheee!", "You threw me SO far! Do it again!", "You threw me. Fine. It was a bit fun.", "You threw me. I screamed a little.")
        : V("You threw me around today. Not nice.", "You threw me. I'm trying to stay positive.", "You threw me. I'll remember that.", "You threw me. I hid for a while after."), UserFondness > 0.4f ? "★" : "⚡", 900);

    void DiaryPetted() => Write("petted", V("You patted my head today.", "You petted me!! Best day!", "You petted me. I didn't hate it.", "You petted me. I went all warm.", "You petted me. I felt the whole screen glow."), "♥", 3600);

    // ---------------- games, play, things ----------------

    void DiaryMatch(Match m, bool won, bool tie)
    {
        string game = m.Name.ToLowerInvariant();
        if (tie) Write("match", $"Played {game}. It was a draw.", "", 900);
        else if (won) Write("match", V($"Won at {game}!", $"WE WON at {game}!! ({m.ScoreText})", $"Won at {game}. Obviously.", $"We won at {game}! I scored... maybe.", $"Won at {game}. The ball moved like a comet."), "★", 900);
        else Write("match", V($"Lost at {game}.", $"Lost at {game} but it was fun!", $"Lost at {game}. The ref was blind.", $"Lost at {game}. Sorry, team."), "☁", 900);
    }

    void DiaryAte(Item it) => Write("ate", f.Tastes.Likes(Thing.Eating)
        ? V($"Ate {it.Def.Article} {it.Def.Name.ToLowerInvariant()}. Delicious.", $"Had {it.Def.Article} {it.Def.Name.ToLowerInvariant()}!! Yum yum yum.", $"Ate {it.Def.Article} {it.Def.Name.ToLowerInvariant()}. Not bad.")
        : $"Ate {it.Def.Article} {it.Def.Name.ToLowerInvariant()}.", "♥", 1200);

    void DiaryNapped(Item? where) => Write("nap", where != null
        ? V($"Had a nap on the {where.Def.Name.ToLowerInvariant()}.", $"Best nap ever on the {where.Def.Name.ToLowerInvariant()}!", $"Napped. The {where.Def.Name.ToLowerInvariant()} was lumpy.")
        : V("Had a nap.", "Nap time! Feel brand new.", "Napped. Leave me alone."), "☾", 1800);

    void DiaryClimbed(float rise) => Write("view", V("Climbed all the way up a window. What a view.", "Climbed SO high today! I could see everything!", "Climbed up a window. Whatever. Nice view though.",
                                                       "Climbed up high. Didn't look down.", "Climbed up high. The windows looked like little islands."), "★", 1800);

    // ---------------- screen ----------------

    void DiaryWord(string word, float opinion) => Write("word:" + word, opinion > 0.35f
        ? V($"Saw \"{word}\" on your screen. I love {word}!", $"\"{word}\" on your screen!! My favourite!", $"Saw \"{word}\" on your screen. Okay, that's cool.")
        : opinion < -0.35f ? V($"Saw \"{word}\" on your screen. Gross.", $"Saw \"{word}\" on your screen. Let's not.", $"\"{word}\". On YOUR screen. Ugh.")
        : $"Read the word \"{word}\" on your screen today.", opinion > 0.35f ? "♥" : opinion < -0.35f ? "⚡" : "", 1800);

    void DiaryVideo() => Write("video", V("Watched a video on your screen.", "Watched a video with everyone! So good!", "Watched your video. Seen better.", "Watched a video from the back row.", "Watched a video. I wonder who makes them."), "★", 1800);

    void DiaryDanced() => Write("dance", V("Danced to your music.", "Danced and DANCED to your music!", "Your music was okay. I moved a bit."), "♪", 1800);

    public void DiaryLinkOpened() => Write("link", V("Showed you a link and you opened it!", "You opened my link!! We're basically a team.", "You opened my link. Took you long enough."), "♥", 1800);

    // ---------------- wishes and the pencil ----------------

    void DiaryWish(Want what, bool granted) => Write("wish:" + what.Name, granted
        ? V($"Wanted {Article(what)} and you gave me one!", $"Asked for {Article(what)} and you gave me one!! You're the best!", $"Asked for {Article(what)}. Got one. Fine, thanks.", $"I asked for {Article(what)}, really quietly, and you heard!")
        : V($"Wanted {Article(what)}. Oh well.", $"Wanted {Article(what)}. Maybe tomorrow!", $"Asked for {Article(what)}. Nothing. Typical.", $"Wanted {Article(what)}. I didn't ask very loudly."), granted ? "♥" : "☁", 900);

    static string Article(Want w) => $"{(w.Item?.Article ?? "a")} {w.Name.ToLowerInvariant()}";

    void DiaryPencil() => Write("pencil", V("Found the Creator's Pencil!", "I HAVE THE MAGIC PENCIL!!", "Got the pencil. Mine now.", "I'm holding the Creator's Pencil. I'm scared to use it.", "The pencil hums when I hold it."), "★", 1800);

    void DiaryDrew(Want what) => Write("drew", V($"Drew {Article(what)} with the magic pencil.", $"Drew {Article(what)} out of NOTHING!", $"Drew {Article(what)}. Pretty good, if I say so.",
                                                 $"Drew {Article(what)}. It came out a bit wobbly.", $"Drew {Article(what)}. Where do drawings come from, anyway?"), "✎", 300);

    // ---------------- heart ----------------

    void DiaryCrush(Figure o) => Write("crush:" + o.Name, V($"I think I like {o.Name}...", $"I like {o.Name}. Like LIKE like!", $"{o.Name} is... fine. Don't read this.", $"I can't stop looking at {o.Name}.", $"{o.Name} has stars in their eyes. Or I do."), "♥", 3600);

    void DiaryConfessed(Figure o, bool yes) => Write("confess:" + o.Name, yes
        ? V($"Told {o.Name} how I feel. They said yes!", $"{o.Name} SAID YES!!!", $"Told {o.Name}. They said yes. I'm not smiling, you're smiling.", $"I told {o.Name}. They said yes. I can't breathe.")
        : V($"Told {o.Name} how I feel. They said no.", $"{o.Name} said no. Plenty of fish!", $"{o.Name} said no. Their loss.", $"{o.Name} said no. I want to hide in a box."), yes ? "♥" : "💔", 600);

    void DiaryAskedOut(Figure o, bool yes) => Write("asked:" + o.Name, yes
        ? V($"{o.Name} told me they like me. We're together now!", $"{o.Name} likes me!! We're dating!!", $"{o.Name} asked me out. I said yes. Obviously.")
        : $"{o.Name} told me they like me. I said no.", yes ? "♥" : "", 600);

    void DiaryBreakup(Figure o) => Write("breakup:" + o.Name, V($"{o.Name} and I broke up.", $"{o.Name} and I broke up. I'll be okay. I think.", $"Done with {o.Name}. Good riddance.", $"{o.Name} and I broke up. I'm going to sit in the corner for a bit."), "💔", 600);
}
