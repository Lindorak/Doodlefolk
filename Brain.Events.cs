using System.Numerics;

namespace StickFight;

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
