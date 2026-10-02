using System.Numerics;

namespace Doodlefolk;

/// <summary>Reacting to things happening on the desktop: riding a window you drag (thrill-seekers love it, the timid
/// hang on for dear life), being shaken off one, the window under them vanishing, you finishing a long stretch of
/// typing, and a notification popping up.</summary>
sealed partial class Brain
{
    float _rideCd, _typingCheerCd;

    /// <summary>The surface it's standing on is moving at <paramref name="v"/>.</summary>
    public void OnRiding(Vector2 v)
    {
        if (_t0 < _rideCd || f.Mode != Mode.Control || _g == G.Sleep) return;
        _rideCd = _t0 + rng.Range(3, 6);
        float thrill = P.Playfulness * 0.5f + P.Bravery * 0.5f + f.Tastes.Of(Thing.Tricks) * 0.3f + f.Tastes.Of(Thing.HighPlaces) * 0.2f;
        if (thrill > 0.55f)
        {
            f.Emote(rng.NextDouble() < 0.5 ? "wheee!" : "★", 1.1f);
            Cheered(0.08f);
            Write("ride", V("Rode a window across the screen!", "You took me for a ride on a window! WHEEE!", "Rode a window. Could've gone faster.", "Rode a window. Held on very tight.", "Rode a window like a magic carpet."), "★", 1800);
        }
        else
        {
            f.Emote(rng.NextDouble() < 0.5 ? "whoa!" : "aaah!", 1.1f);
            f.DuckT = 0.5f;   // crouch and hold on
            Fear = M.Clamp01(Fear + 0.1f);
        }
    }

    /// <summary>Thrown off a window that was shaken or stopped dead.</summary>
    public void OnShakenOff()
    {
        float like = f.Tastes.Of(Thing.BeingThrown);
        FeelUser(like > 0.35f ? 0.02f : -0.04f, "Shook them off a window");
        Write("shaken", like > 0.35f ? V("You shook me off a window. Again!", "You flung me off a window!! Again again!") : V("You shook me off a window. Rude.", "Got shaken off a window. I'm fine!", "You shook me off a window. Noted.", "Fell off a shaking window. My heart."), like > 0.35f ? "★" : "⚡", 900);
    }

    float _comfortAt = -1;
    string _reminderText = "";
    float _reminderUntil = -1;

    /// <summary>A download just finished (only its kind is known: "a picture", "a document"…).</summary>
    public void OnDownload(string kind, World w)
    {
        f.LookAt = new Vector2(w.Env.Virtual.Right - 60, w.Env.Virtual.Bottom - 20);
        f.Emote(V($"download done! ({kind})", $"YOUR DOWNLOAD'S DONE!! {kind}!", $"download finished. {kind}.", $"um, your {kind.Replace("a ", "").Replace("some ", "")} finished downloading…", $"{kind} has arrived"), 2.2f);
        if (_g is G.Idle or G.SitFloor or G.Walk && P.Playfulness > 0.45f) Go(G.Cheer, 0.8f);
    }

    /// <summary>You seem frustrated (rage clicks, windows slammed shut): come over and be kind.</summary>
    public void ComfortUser(World w, string why)
    {
        if (f.Mode != Mode.Control || !f.Grounded) return;
        f.Emote(V("you okay?", "HEY! YOU OKAY?!", "…rough day?", "um… are you alright?", "a storm in you?"), 1.6f);
        ComeToCursor(w);
        _comfortAt = _t0 + 4;
        Write("comfort", V("You seemed frustrated, so I went to check on you.", "You looked SO frustrated! I went to cheer you up!", "You were clicking like mad. I went over. Whatever.", "You seemed upset… I hope you're okay.", "I saw your storm and went to sit by it."), "♥", 1800);
    }

    /// <summary>A reminder you set is due: bring it to you.</summary>
    public void BringReminder(string text, World w)
    {
        if (f.Mode != Mode.Control) return;
        if (_g == G.Sleep) Go(G.Idle, 0.5f);
        ComeToCursor(w);
        _reminderText = text;
        _reminderUntil = _t0 + 25;
        f.Emote($"⏰ {text}", 6);
        World.Play(Sfx.Pip, f.Base, 0.5f, 1.3f);
    }

    void DesktopTick(World w)
    {
        if (_comfortAt > 0 && _t0 > _comfortAt)
        {
            _comfortAt = -1;
            f.Emote(new[] { "deep breaths…", "it'll be okay ♥", "want a hug?", "take a break?", "you've got this!" }[rng.Next(5)], 2.4f);
            f.LookAt = w.Cursor;
            if (_g is G.Idle or G.Watch) Go(G.Wave, 1.2f);
        }
        if (_reminderUntil > 0)
        {
            if (_t0 > _reminderUntil) _reminderUntil = -1;
            else if (f.CurrentEmote == null && Vector2.Distance(f.Jt[J.Head], w.Cursor) < 260 * S) { f.Emote($"⏰ {_reminderText}", 5); f.LookAt = w.Cursor; if (_g is G.Idle or G.Watch) Go(G.Wave, 1); }
        }
    }

    /// <summary>The window it was standing on disappeared (closed or minimised).</summary>
    public void OnFloorVanished()
    {
        if (_g == G.Busy) return;
        f.Emote(P.Aggression > 0.55f ? "hey!" : rng.NextDouble() < 0.5 ? "aaah!" : "!!", 1.1f);
        Write("floor", V("The window I was standing on just vanished!", "My window disappeared and I fell! Ha!", "Somebody closed my window. While I was ON it.", "The floor vanished. I don't trust floors now."), "⚡", 1800);
    }

    /// <summary>You just stopped after typing for a while (only timing is known, never what was typed).</summary>
    public void OnUserFinishedTyping(double seconds, World w)
    {
        if (_t0 < _typingCheerCd || f.Mode != Mode.Control || _g is G.Sleep or G.Fight or G.Sport or G.Hunt) return;
        _typingCheerCd = _t0 + rng.Range(120, 300);
        if (UserFondness < 0.25f || rng.NextDouble() > 0.35 + P.Sociability * 0.3) return;
        f.Emote(seconds > 90 ? (rng.NextDouble() < 0.5 ? "phew!" : "done?") : rng.NextDouble() < 0.5 ? "nice!" : "★", 1.3f);
        f.LookAt = w.Cursor;
        if (_g is G.Idle or G.SitFloor && P.Playfulness > 0.5f) Go(G.Cheer, 0.8f);
        if (seconds > 120) Write("typing", V("You typed for ages. Hope it was something good!", "You typed SO much! Hard worker!", "You typed forever. I counted the clicks. Kidding.", "You typed for a long time. I kept quiet."), "✎", 3600);
    }

    /// <summary>A notification popped up at <paramref name="where"/>.</summary>
    public void OnNotification(RectangleF where, World w)
    {
        if (f.Mode != Mode.Control || _g is G.Sleep or G.Fight or G.Sport or G.CursorFight) return;
        var c = new Vector2(where.Left + where.Width / 2, where.Top + where.Height / 2);
        float d = Vector2.Distance(c, f.Base);
        if (d > 1600 * S) return;
        f.LookAt = c;
        f.Emote(rng.NextDouble() < 0.5 ? "!" : P.Curiosity > 0.6f ? "ooh, mail?" : "?", 1.3f);
        // The curious and brave go and stand on it (it's a little window: it's a ledge while it's up).
        if (P.Curiosity + P.Bravery > 1.1f && d < 1100 * S && rng.NextDouble() < 0.5 && _g is G.Idle or G.Walk or G.SitFloor or G.Watch)
        {
            var target = new Vector2(c.X + rng.Range(-where.Width * 0.3f, where.Width * 0.3f), where.Top);
            Navigate(() => target, 10 * S, true, () => { f.Emote("★", 1); Go(G.Idle, rng.Range(2, 4)); }, WalkPurpose.Explore);
            Write("notify", V("A notification popped up and I stood on it!", "Climbed onto a notification! Ding!", "Notifications. Always beeping. I sat on one."), "★", 3600);
        }
    }
}
