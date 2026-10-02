using System.Numerics;

namespace Doodlefolk;

/// <summary>Prank mode (see App.Pranks): what the playful ones get up to.</summary>
sealed partial class Brain
{
    float _prankCd = 400;

    bool Prankster => !Baby && P.Playfulness > 0.55f && P.Aggression < 0.8f && f.Visitor == VisitorKind.None;

    void PrankOptions(World w, OptionList opts)
    {
        if (!World.Pranks || !Prankster || _t0 < _prankCd || w.Items.Count > 50) return;
        float mischief = 0.25f + (P.Playfulness - 0.55f) * 1.5f + Boredom * 0.3f;
        // A sticky note on a window.
        if (w.Items.Count(i => i.Def.Key == "stickynote") < 4 && w.Env.Platforms.Where(p => p.Hwnd != IntPtr.Zero && p.Item == null && p.Seen == null && p.X2 - p.X1 > 120 * S).OrderBy(_ => rng.Next()).FirstOrDefault() is { } ledge)
            opts.Add(mischief, () =>
            {
                _prankCd = _t0 + rng.Range(900, 2400);
                float x = rng.Range(ledge.X1 + 30 * S, ledge.X2 - 30 * S);
                Navigate(() => w.Env.Platforms.Contains(ledge) ? new Vector2(x, ledge.Y) : null, 16 * S, false, () =>
                {
                    if (w.MakeItem?.Invoke("stickynote") is not { } note) return;
                    note.Pos = f.Base + new Vector2(f.Facing * 14 * S, -1); note.Vel = Vector2.Zero; note.OnGround = false;
                    note.Label = w.NoteFor?.Invoke(f) ?? "hi :)";
                    note.Color = new[] { M.Hex(0xFFF176), M.Hex(0xF8BBD0), M.Hex(0xB3E5FC), M.Hex(0xC5E1A5) }[rng.Next(4)];
                    w.PrankLeft?.Invoke(note, 30);
                    f.SetAction(Act.Tap);
                    f.Emote(Gestures ? "📝" : V("hehe", "HEHEHE!", "heh.", "hee hee…", "a little note"), 1.4f);
                    Write("prank:note", V($"Left a sticky note on a window: \"{note.Label}\".", $"STUCK A NOTE UP: \"{note.Label}\" HAHA", $"Left a note. \"{note.Label}\". You're welcome.", $"I left a little note… \"{note.Label}\"", $"A note on the glass: \"{note.Label}\""), "★", 1800);
                    w.Sticker("prank");
                }, WalkPurpose.Other);
            }, "Leave a sticky note");
        // A whoopee cushion on a seat nobody's using.
        if (!w.Items.Any(i => i.Def.Key == "whoopee") && w.Items.Where(i => i.Def.Verbs.Contains(Verb.Sit) && i.Seated.All(s => s == null) && !Unreachable(i)).OrderBy(_ => rng.Next()).FirstOrDefault() is { } seat)
            opts.Add(mischief * 0.6f, () =>
            {
                _prankCd = _t0 + rng.Range(900, 2400);
                Navigate(() => w.Items.Contains(seat) ? seat.Pos + new Vector2(-f.Facing * 20 * S, 0) : null, 14 * S, false, () =>
                {
                    if (!w.Items.Contains(seat) || seat.Seated.Any(s => s != null) || w.MakeItem?.Invoke("whoopee") is not { } c) return;
                    c.Pos = seat.Pos + new Vector2(0, -seat.Def.H * seat.Sc * 0.45f); c.Vel = Vector2.Zero; c.OnGround = false;
                    w.PrankLeft?.Invoke(c, 20);
                    f.SetAction(Act.Tap);
                    f.Emote(Gestures ? "🤫" : V("shh…", "HEHEHEHE", "…nobody saw that.", "sh-shh…", "a trap is laid"), 1.6f);
                    Write("prank:whoopee", V("Hid a whoopee cushion on a seat. Waiting…", "WHOOPEE CUSHION DEPLOYED!!!", "Hid a whoopee cushion. Classic.", "I hid a whoopee cushion… I feel bad. A bit.", "The cushion waits."), "★", 1800);
                    Go(G.Idle, rng.Range(3, 6));
                }, WalkPurpose.Other);
            }, "Hide a whoopee cushion");
        // A meme, framed, hung where you'll see it.
        if (w.MemeReady?.Invoke() == true && w.Env.Platforms.Where(p => p.Hwnd != IntPtr.Zero && p.Item == null && p.Seen == null && p.X2 - p.X1 > 160 * S).OrderBy(_ => rng.Next()).FirstOrDefault() is { } wall)
            opts.Add(mischief * 1.4f, () =>
            {
                _prankCd = _t0 + rng.Range(900, 2400);
                float x = rng.Range(wall.X1 + 50 * S, wall.X2 - 50 * S);
                Navigate(() => w.Env.Platforms.Contains(wall) ? new Vector2(x, wall.Y) : null, 16 * S, false, () =>
                {
                    if (w.TakeMeme?.Invoke() is not { } path || w.MakeItem?.Invoke("memeframe") is not { } frame) return;
                    frame.Pos = f.Base + new Vector2(f.Facing * 30 * S, -1); frame.Vel = new Vector2(0, -120 * S); frame.OnGround = false;
                    frame.Label = path;
                    w.Fx.Dust(frame.Pos, S, 6, 1, rng);
                    w.PrankLeft?.Invoke(frame, 45);
                    FaceTo(frame.Pos.X);
                    f.Emote(Gestures ? "😂" : V("look at THIS", "LOOK! LOOK!! HAHAHA", "…this one's good.", "um… I f-found a funny one", "behold, a meme"), 2);
                    Write("prank:meme", V("Hung up a meme for you. Hope you laughed.", "PUT UP A MEME!!! SO FUNNY!!!", "Hung a meme. It's funny. Fact.", "I put up a funny picture… I hope you like it.", "A picture, for laughing at."), "★", 1800);
                    w.Sticker("meme");
                }, WalkPurpose.Other);
            }, "Hang up a meme");
    }
}
