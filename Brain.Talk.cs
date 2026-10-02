using System.Text.RegularExpressions;

namespace StickFight;

/// <summary>Talk to them: type something in their right-click menu and they answer in their own voice, from how they
/// feel, what they like and who they know. All local: simple keyword understanding, no AI and nothing sent anywhere.</summary>
sealed partial class Brain
{
    static bool Has(string s, string pattern) => Regex.IsMatch(s, @"\b(" + pattern + @")\b", RegexOptions.IgnoreCase);

    public string Talk(string said, World w)
    {
        said = said.Trim();
        if (said.Length == 0) return "";
        if (said.Length > 140) said = said[..140];
        string s = said.ToLowerInvariant();
        f.LookAt = w.Cursor;
        if (f.Mode == Mode.Control && f.Grounded) FaceTo(w.Cursor.X);
        if (_g == G.Sleep)
        {
            if (P.Aggression > 0.5f || Stamina < 0.3f) return Say("z… mmh… five more minutes…", 0, "Woke up to you talking");
            Go(G.Idle, 1);
            return Say(V("huh? oh! hi.", "WHA- oh hi!!", "I was sleeping.", "mm? oh… hello", "I was dreaming…"), 0.01f);
        }
        f.SetAction(Act.Talk);
        string reply = Reply(s, w, out float fond);
        Write("talk", V($"You talked to me. You said \"{Clip(said)}\".", $"YOU TALKED TO ME!! \"{Clip(said)}\"", $"You said \"{Clip(said)}\". Okay then.", $"You said \"{Clip(said)}\" to me. I didn't know what to say.", $"Your words drifted by: \"{Clip(said)}\""), fond < 0 ? "…" : "♥", 120);
        return Say(reply, fond);
    }

    static string Clip(string s) => s.Length > 50 ? s[..48] + "…" : s;

    string Say(string text, float fond, string? memory = null)
    {
        if (fond != 0) FeelUser(fond, memory ?? (fond > 0 ? "Said something nice" : "Said something mean"));
        f.Emote(text, Math.Clamp(0.9f + text.Length * 0.05f, 1.4f, 4));
        return text;
    }

    string Reply(string s, World w, out float fond)
    {
        fond = 0.005f;
        float like = UserFondness;
        // Someone else's name: what it thinks of them.
        foreach (var o in w.Figures)
            if (o != f && !o.Dead && Has(s, Regex.Escape(o.Name.ToLowerInvariant())) && !Has(s, "fight|hit|punch|attack"))
            {
                if (Sweetheart(w) == o) return $"♥ {o.Name} ♥";
                if (Crush(w) == o) { f.Blush = 1; return V($"w-what about {o.Name}?", $"{o.Name}?!?! n-nothing!!", $"{o.Name}? don't care.", $"oh… {o.Name}… um…", $"{o.Name} is… nice."); }
                float a = AffinityWith(o);
                return a > 0.55f ? V($"{o.Name} is my best friend!", $"{o.Name} is the BEST!!", $"{o.Name}'s alright.", $"I really like {o.Name}", $"{o.Name} gets me.")
                     : a > 0.15f ? V($"{o.Name}'s nice.", $"{o.Name}? fun!", $"{o.Name} is fine.", $"{o.Name} is kind to me", $"{o.Name}… a good soul")
                     : a < -0.4f ? V($"ugh, {o.Name}.", $"{o.Name}?! NO.", $"don't talk to me about {o.Name}.", $"{o.Name} scares me…", $"{o.Name} and I… don't get along")
                     : V($"{o.Name}? don't know them much.", $"{o.Name}? haven't played with them yet!", $"{o.Name}. whatever.", $"I don't really know {o.Name}", $"{o.Name} is a mystery");
            }
        if (Has(s, "i love you|love you|luv you|ily"))
        {
            fond = 0.06f;
            if (like > 0.5f) { f.Blush = 1; return V("♥ love you too!", "♥♥♥ LOVE YOU TOO!!", "…yeah, you're okay. ♥", "♥ …me too", "♥ always"); }
            return like > 0 ? V("aw! ♥", "AWW!", "…thanks?", "oh! um… ♥", "how kind") : V("…uh huh.", "oh! um. ok!", "sure you do.", "…", "words are wind");
        }
        if (Has(s, "stupid|dumb|ugly|idiot|loser|stinky|hate you|shut up|suck|useless|weird|bad"))
        {
            fond = -0.06f;
            Sadness = M.Clamp01(Sadness + 0.15f);
            Annoyance = M.Clamp01(Annoyance + 0.2f);
            return P.Aggression > 0.55f ? V("#@!", "HEY!!! #@!", "say that again.", "…that's mean", "how rude")
                                        : V("…that's mean.", "hey!! that's not nice!", "whatever.", "…", "…ouch.");
        }
        if (Has(s, "good (boy|girl|job|one)|well done|nice|great|awesome|cool|cute|amazing|the best|beautiful|clever|smart|funny|brave|strong|pretty|handsome|adorable|fast"))
        {
            fond = 0.04f;
            f.Blush = 1;
            Cheered(0.15f);
            return V("aw, thanks!", "REALLY?! THANKS!!", "I know.", "o-oh… thank you…", "you're too kind");
        }
        if (Has(s, "hi|hello|hey|yo|sup|howdy|hiya|morning|evening|afternoon|greetings"))
        {
            if (f.Mode == Mode.Control && f.Grounded && _g is G.Idle or G.Walk or G.SitFloor) Go(G.Wave, 1.2f);
            fond = 0.015f;
            return like > 0.4f ? V($"hi!! it's me, {f.Name}!", "HI HI HI!!", "oh. hey.", "h-hi!", "hello, friend") : V("hi.", "hi!", "…hey.", "oh! hi…", "hello");
        }
        if (Has(s, "bye|goodbye|good night|goodnight|see you|see ya|later|night night"))
        {
            if (f.Mode == Mode.Control && f.Grounded) Go(G.Wave, 1.2f);
            return V("bye!", "BYE!! come back soon!", "later.", "bye bye…", "until next time");
        }
        if (Has(s, "thank|thanks|thx|ty")) { fond = 0.02f; return V("you're welcome!", "anytime!!", "sure.", "o-oh, no problem", "of course"); }
        if (Has(s, "how are you|how're you|you ok|are you ok|you alright|how do you feel|how are u|hru|what's up|whats up|how's it going"))
            return HowIFeel(w);
        if (Has(s, "your name|who are you|what are you"))
            return V($"I'm {f.Name}!", $"I'm {f.Name}!!! nice to meet you!", $"{f.Name}. remember it.", $"um… {f.Name}", $"they call me {f.Name}");
        if (Has(s, "favou?rite colou?r|what colou?r"))
            return f.Tastes.FavoriteColour is { Length: > 0 } c ? V($"{c.ToLowerInvariant()}!", $"{c.ToUpperInvariant()}!!!", $"{c.ToLowerInvariant()}. obviously.", $"I like {c.ToLowerInvariant()}…", $"{c.ToLowerInvariant()}, like the sky sometimes") : "all of them!";
        if (Has(s, "what do you like|favou?rite|what do you love|what's fun|whats fun"))
        {
            var top = Enum.GetValues<Thing>().OrderByDescending(t => f.Tastes.Of(t)).First();
            return V($"I love {ThingWords(top)}!", $"{ThingWords(top).ToUpperInvariant()}!!!", $"{ThingWords(top)}. if you must know.", $"I like {ThingWords(top)}…", $"{ThingWords(top)}, mostly");
        }
        if (Has(s, "do you like|you like|do you hate|you hate"))
        {
            foreach (var t in Enum.GetValues<Thing>())
                if (ThingKeys(t).Any(k => s.Contains(k)))
                {
                    float o = f.Tastes.Of(t);
                    return o > 0.5f ? V($"I love {ThingWords(t)}!", $"{ThingWords(t).ToUpperInvariant()}!! YES!", $"{ThingWords(t)}? yeah, it's good.", $"I really like {ThingWords(t)}", $"{ThingWords(t)} makes me happy")
                         : o < -0.4f ? V($"ew, {ThingWords(t)}.", $"{ThingWords(t)}?! NO WAY", $"{ThingWords(t)}. hate it.", $"{ThingWords(t)} scares me", $"{ThingWords(t)}… not for me")
                         : V($"{ThingWords(t)}? it's okay.", $"{ThingWords(t)} is fine!", $"{ThingWords(t)}. meh.", $"{ThingWords(t)} is alright", $"{ThingWords(t)}… sometimes");
                }
            return V("hmm… maybe?", "YES! …wait, what?", "no.", "um… I don't know", "who can say");
        }
        if (Has(s, "dance|boogie|groove"))
        {
            if (f.Mode == Mode.Control && f.Grounded) { Go(G.Idle, 3); f.StartFidget(Fidget.Groove); }
            Practice(SkillKind.Dancing, 0.01f);
            return f.Tastes.Likes(Thing.Dancing) ? "♪ ♫" : V("…fine. ♪", "OK! ♪♪", "ugh. fine.", "um… ♪", "♪");
        }
        if (Has(s, "jump|flip|trick|backflip"))
        {
            if (f.Mode == Mode.Control && f.Grounded && Stamina > 0.3f) { Go(G.Trick, 3); f.RequestFlip(70 * S); return V("hup!", "WHEEE!", "easy.", "eep!", "behold"); }
            return "too tired…";
        }
        if (Has(s, "sleep|nap|bed|rest|tired"))
        {
            if (Stamina < 0.6f && f.Mode == Mode.Control && f.Grounded) { Go(G.Sleep, rng.Range(15, 30)); return V("ok… z", "nap time!!", "fine. z", "g'night…", "to dreams"); }
            return V("not sleepy!", "SLEEP?! NO WAY!", "no.", "I'm not tired…", "the night is young");
        }
        if (Has(s, "sit|sit down"))
        {
            if (f.Mode == Mode.Control && f.Grounded) Go(G.SitFloor, rng.Range(5, 12));
            return V("ok!", "sitting!!", "fine.", "ok…", "as you wish");
        }
        if (Has(s, "come here|come|over here|follow"))
            return CalledByUser(w) is var r && r.Contains("on the way") || r.Contains("running") ? V("coming!", "COMING!!", "ugh, fine.", "o-okay!", "on my way") : V("nah.", "busy!!", "no.", "um… later?", "not now");
        if (Has(s, "hungry|eat|food|snack"))
            return V("I could eat!", "PIZZA!!", "food? sure.", "a cookie would be nice…", "a snack sounds lovely");
        if (Has(s, "joke|funny|make me laugh"))
        {
            string[] jokes =
            {
                "why are stick figures bad at hiding? we're always in plain sight!",
                "I tried to diet… but I'm already all bones!",
                "what's a stick figure's favourite music? heavy metal… no, STICK rock!",
                "I'd tell you a joke about pencils but it's pointless.",
                "how do you draw a stick figure? you just… line up.",
            };
            World.Play(Sfx.Laugh, f.Jt[J.Head], 0.3f, 1.1f);
            return jokes[rng.Next(jokes.Length)];
        }
        if (Has(s, "sorry|apologi[sz]e")) { fond = 0.04f; return V("it's ok!", "FORGIVEN!!", "…fine.", "it's okay, really", "all is forgiven"); }
        if (Has(s, "fight|punch|hit|attack|beat")) return P.Aggression > 0.5f ? V("who?! I'm ready!", "FIGHT!! WHO?!", "point me at them.", "um… do I have to?", "violence is a last resort") : V("I'd rather not…", "no fighting!! let's play!", "no.", "fighting is scary…", "let's not");
        if (Has(s, "play|game|bored")) return V("let's play! right-click me and pick a game", "GAMES!! right-click me!", "right-click me if you want to play.", "w-we could play a game? right-click me", "right-click me, and let's play");
        if (Has(s, "where|what are you doing|doing")) return $"{Activity.ToLowerInvariant()}!";
        if (s.Contains('?')) return V("hmm… dunno!", "YES! …wait, what?", "no idea.", "um… maybe?", "the stars know");
        return like > 0.3f ? V("ok!", "YAY!", "sure.", "oh! okay.", "mm, interesting") : V("ok.", "uh huh!", "…", "oh…", "mm");
    }

    string HowIFeel(World w)
    {
        if (f.HP < 40) return V("ouch… I'm hurt", "I'M HURT!! ow ow ow", "I've been better.", "it hurts…", "wounded, but alive");
        if (Heartbroken) return V("my heart hurts.", "I'm SO sad…", "don't ask.", "…not good", "heartbroken");
        if (Sweetheart(w) is { } sw && Joy > 0.5f) return $"great! I've got {sw.Name} ♥";
        if (Stamina < 0.3f) return V("tired…", "SO SLEEPY", "exhausted.", "sleepy…", "weary");
        if (Sadness > 0.45f) return V("kinda sad.", "sad today :(", "fine. whatever.", "a bit down…", "melancholy");
        if (Annoyance > 0.5f) return V("annoyed!", "GRRR!", "don't push it.", "a bit grumpy…", "vexed");
        if (Loneliness > 0.6f) return V("lonely… play with me?", "LONELY! play with me!!", "alone. it's fine.", "a little lonely…", "solitary");
        if (Boredom > 0.6f) return V("bored!", "SO BORED!!", "bored out of my mind.", "a bit bored…", "restless");
        if (Joy > 0.6f) return V("great!", "AMAZING!!!", "good.", "really good!", "wonderful");
        return V("good!", "GOOD!!", "fine.", "okay, I think!", "at peace");
    }

    static string ThingWords(Thing t) => t switch
    {
        Thing.PlayingBall => "playing ball", Thing.HighFives => "high fives", Thing.HighPlaces => "high places", Thing.SoccerBalls => "soccer",
        Thing.BeachBalls => "beach balls", Thing.YourCursor => "you", Thing.BeingPickedUp => "being picked up", Thing.BeingThrown => "being thrown",
        Thing.Taskbar => "the taskbar", _ => Regex.Replace(t.ToString(), "([a-z])([A-Z])", "$1 $2").ToLowerInvariant(),
    };

    static string[] ThingKeys(Thing t) => t switch
    {
        Thing.PlayingBall => new[] { "ball" }, Thing.Juggling => new[] { "juggl" }, Thing.Climbing => new[] { "climb" }, Thing.Exploring => new[] { "explor", "adventur" },
        Thing.Chatting => new[] { "chat", "talk" }, Thing.HighFives => new[] { "high five", "high-five" }, Thing.Fighting => new[] { "fight" }, Thing.Sparring => new[] { "spar" },
        Thing.Napping => new[] { "nap", "sleep" }, Thing.Tricks => new[] { "trick", "flip" }, Thing.Dancing => new[] { "danc" }, Thing.Sitting => new[] { "sit" },
        Thing.Eating => new[] { "eat", "food" }, Thing.Reading => new[] { "read", "book" }, Thing.HighPlaces => new[] { "high", "heights" }, Thing.Taskbar => new[] { "taskbar" },
        Thing.Ledges => new[] { "ledge", "edge" }, Thing.SoccerBalls => new[] { "soccer", "football" }, Thing.Basketballs => new[] { "basketball" }, Thing.BeachBalls => new[] { "beach" },
        Thing.YourCursor => new[] { " me", "cursor", "mouse" }, Thing.BeingPickedUp => new[] { "picked up", "pick you up" }, Thing.BeingThrown => new[] { "thrown", "throw you" },
        _ => new[] { t.ToString().ToLowerInvariant() },
    };
}
