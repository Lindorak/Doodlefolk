using System.Diagnostics;
using System.Numerics;

namespace Doodlefolk;

/// <summary>A figure pointing out a link. It only opens if you click the bubble.</summary>
sealed class LinkOffer
{
    public required Figure By;
    public required string Url, Text;
    public double Until;
    public RectangleF Bubble;
    public bool Hot;

    public string Host => Uri.TryCreate(Url, UriKind.Absolute, out var u) ? u.Host.Replace("www.", "") : "";
}

/// <summary>A word, link or picture found on screen, where it is right now.</summary>
readonly record struct SeenNow(Seen Seen, ScreenSnap Snap, RectangleF Rect)
{
    public Vector2 Centre => new(Rect.Left + Rect.Width / 2, Rect.Top + Rect.Height / 2);
}

sealed partial class World
{
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    public static double Now => Clock.Elapsed.TotalSeconds;

    public ScreenSense? Screen;
    /// <summary>What the figures do with what's on screen (all user options).</summary>
    public bool ScreenTerrain = true, ScreenReact = true, ScreenLinks = true, ScreenMedia = true;
    public LinkOffer? Offer;
    /// <summary>A figure's thought bubble asking for something (one at a time), and whether they're allowed to ask.</summary>
    public Wish? Wish;
    public double NextWishAt = 40;
    public bool Wishes = true;
    public double NextOfferAt = 20;

    public MediaNow Media => ScreenMedia && Screen != null ? Screen.Media : MediaNow.Quiet;
    /// <summary>Dance timing: where we are in the music's beat (see ScreenSense.BeatPhase), refreshed each frame.</summary>
    public static float BeatPhase = -1, BarPhase = -1;

    /// <summary>Things on screen of a kind, at their current position, that aren't hidden behind other windows.</summary>
    public IEnumerable<SeenNow> OnScreen(SeenKind kind)
    {
        if (Screen == null) yield break;
        foreach (var s in Screen.Snaps)
        {
            var off = Env.SnapOffset(s);
            if (float.IsNaN(off.X)) continue;
            foreach (var t in s.Things)
            {
                if (t.Kind != kind) continue;
                var r = new RectangleF(t.Rect.X + off.X, t.Rect.Y + off.Y, t.Rect.Width, t.Rect.Height);
                if (Env.Visible(s.Hwnd, r) < 0.8f) continue;
                yield return new SeenNow(t, s, r);
            }
        }
    }

    /// <summary>A ledge to stand on to look at something on screen: the line it's on, or the nearest one just below it.</summary>
    public Platform? LedgeFor(SeenNow it)
    {
        Platform? best = null;
        float cx = it.Centre.X;
        foreach (var p in Env.Platforms)
        {
            if (p.Hwnd != it.Snap.Hwnd || p.Seen == null || cx < p.X1 + 4 || cx > p.X2 - 4) continue;
            if (p.Y < it.Rect.Top - 4 || p.Y > it.Rect.Bottom + 140) continue;
            if (best == null || p.Y < best.Y) best = p;
        }
        return best;
    }
}
