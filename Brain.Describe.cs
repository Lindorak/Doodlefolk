namespace StickFight;

/// <summary>Plain-English descriptions of what a figure is up to, for the Studio.</summary>
sealed partial class Brain
{
    public string Activity
    {
        get
        {
            if (f.Dead) return "Gone";
            if (f.KO) return "Knocked out";
            if (f.Mode == Mode.Spawning) return "Being drawn";
            if (f.Mode == Mode.Ragdoll) return f.Held ? "Being held by you" : "Tumbling";
            if (f.Mode == Mode.GetUp) return "Getting up";
            if (f.Climbing) return f.Grappling ? "Climbing a rope" : "Climbing";
            if (f.GrappleBusy) return "Throwing a grappling hook";
            string? with = _partner?.Name;
            return _g switch
            {
                G.Idle => "Hanging out",
                G.Walk when _item != null && _itemPending => $"Heading for the {_item.Def.Name.ToLowerInvariant()}",
                G.Walk => _fleeing ? "Running away" : _purpose switch
                {
                    WalkPurpose.Explore => "Exploring",
                    WalkPurpose.Look => "Going to look at something on your screen",
                    WalkPurpose.Watch => "Finding a spot to watch from",
                    WalkPurpose.Social => with != null ? $"Going to see {with}" : "Going to say hi",
                    WalkPurpose.Ball => "Going for the ball",
                    _ => _run ? "Running somewhere" : "Wandering",
                },
                G.SitEdge => "Sitting on a ledge",
                G.SitFloor => "Sitting down",
                G.Sleep => "Napping",
                G.Watch => "Watching you",
                G.Swat => "Swatting at you",
                G.Annoyed => "Annoyed",
                G.Wave => "Waving",
                G.Cheer => "Celebrating",
                G.Startled => "Startled",
                G.Trick => "Showing off",
                G.Chat => with != null ? $"Chatting with {with}" : "Chatting",
                G.HighFive => with != null ? $"High-fiving {with}" : "High-fiving",
                G.Follow => with != null ? $"Following {with}" : "Following someone",
                G.SitWith => with != null ? $"Sitting with {with}" : "Sitting with a friend",
                G.DanceWith => with != null ? $"Dancing with {with}" : "Dancing",
                G.Kick => "Kicking the ball",
                G.Dribble => "Dribbling",
                G.Juggle => "Juggling",
                G.Carry => _bringToUser ? "Bringing you the ball" : "Carrying the ball",
                G.Throw => "Throwing",
                G.Catch => "Catching",
                G.Fight => _foe != null ? (_spar ? $"Sparring with {_foe.Name}" : $"Fighting {_foe.Name}") : "Fighting",
                G.Victory => "Celebrating a win",
                G.CursorFight => "Boxing your cursor",
                G.Revive => "Helping a friend up",
                G.Hunt => "Hunting your cursor",
                G.Groove => "Dancing to your music",
                G.Create => _drawing != null ? $"Drawing {_drawing.Article} {_drawing.Name.ToLowerInvariant()}" : "Drawing something",
                G.WatchScreen => "Watching your video",
                G.LookAtScreen => _look?.Seen.Kind == SeenKind.Link ? "Showing you a link" : _look != null ? $"Reading \"{_look.Value.Seen.Text}\"" : "Reading your screen",
                G.Sport => Match != null ? $"Playing {Match.Name} ({Match.ScoreText})" : "Playing",
                G.UseItem => _item == null ? "Busy" : _verb switch
                {
                    Verb.Sit => $"Sitting in the {_item.Def.Name.ToLowerInvariant()}",
                    Verb.Lie => $"Napping on the {_item.Def.Name.ToLowerInvariant()}",
                    Verb.Hammock => "Swinging in the hammock",
                    Verb.Bounce => "Bouncing on the trampoline",
                    Verb.Eat => $"Eating {_item.Def.Article} {_item.Def.Name.ToLowerInvariant()}",
                    Verb.Hide => $"Hiding in the {_item.Def.Name.ToLowerInvariant()}",
                    Verb.Dance => "Dancing to the radio",
                    Verb.Read => $"Reading {_item.Def.Article} {_item.Def.Name.ToLowerInvariant()}",
                    Verb.Warm => "Warming up by the fire",
                    _ => $"Standing on the {_item.Def.Name.ToLowerInvariant()}",
                },
                _ => "Busy",
            };
        }
    }

    /// <summary>After an internal error: drop whatever it was doing and start fresh.</summary>
    public void Reset()
    {
        try { LeaveItem(); } catch (Exception) { }
        _item = null;
        _partner = null;
        _foe = null;
        _g = G.Idle;
        _t = 0;
        _dur = 1;
        f.DesiredVX = 0;
    }

    /// <summary>Set how this figure feels about <paramref name="o"/> overall (-1..1).</summary>
    public void SetAffinity(Figure o, float v) => Affinity[o.Id] = Math.Clamp(v, -1, 1) - Baseline(o);
}
