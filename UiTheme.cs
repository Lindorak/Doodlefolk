using System.Numerics;
using Microsoft.Win32;
using Vortice.Mathematics;

namespace StickFight;

/// <summary>The sketchbook look for everything drawn on the desktop (speech bubbles, link bubbles, scoreboards, health
/// bars), matching the Studio: "paper" (cream card, dark ink) or "chalkboard" (dark slate, chalk-white). "Auto" follows
/// Windows' light/dark app setting.</summary>
static class Ui
{
    public static bool Chalk { get; private set; }

    // Same colours as the Studio's CSS (--card, --ink, --pencil, --accent).
    public static Color4 Fill => Chalk ? new Color4(0.165f, 0.2f, 0.18f, 1) : new Color4(1, 0.992f, 0.969f, 1);
    public static Color4 Ink => Chalk ? new Color4(0.925f, 0.922f, 0.894f, 1) : new Color4(0.149f, 0.141f, 0.122f, 1);
    public static Color4 Pencil => Chalk ? new Color4(0.584f, 0.604f, 0.565f, 1) : new Color4(0.541f, 0.518f, 0.471f, 1);
    public static Color4 Accent => Chalk ? new Color4(1, 0.541f, 0.502f, 1) : new Color4(0.898f, 0.224f, 0.208f, 1);
    public static Color4 Good => Chalk ? new Color4(0.5f, 0.85f, 0.6f, 1) : new Color4(0.18f, 0.545f, 0.341f, 1);

    public static Color4 A(this Color4 c, float alpha) => new(c.R, c.G, c.B, c.A * alpha);

    /// <summary>Re-check which theme applies (cheap; called every couple of seconds so "auto" follows Windows).</summary>
    public static void Update(string theme) => Chalk = theme == "chalk" || (theme != "paper" && WindowsDark());

    static bool WindowsDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch (Exception) { return false; }
    }

    /// <summary>A hand-drawn card: filled rounded box with a pencil outline and a second, slightly offset faint stroke.</summary>
    public static void Card(Renderer r, Vector2 c, float w, float h, float radius, float s, float alpha, Color4? edge = null, float line = 1.2f, Color4? fill = null)
    {
        var e = edge ?? Ink;
        r.RoundRect(c, w, h, radius, (fill ?? Fill).A(0.97f * alpha), e.A(0.85f * alpha), line * s);
        r.RoundRect(c + new Vector2(0.7f * s, -0.5f * s), w - 1.2f * s, h + 0.6f * s, radius * 1.2f, new Color4(0, 0, 0, 0), e.A(0.28f * alpha), 0.8f * s);
    }

    /// <summary>Card plus a little tail pointing down at <paramref name="tip"/>.</summary>
    public static void Bubble(Renderer r, Vector2 c, float w, float h, Vector2 tip, float s, float alpha, Color4? edge = null, float line = 1.2f, Color4? fill = null)
    {
        float tx = M.ClampIn(tip.X, c.X - w / 2 + 6 * s, c.X + w / 2 - 6 * s), by = c.Y + h / 2;
        var f = (fill ?? Fill).A(0.97f * alpha);
        var e = (edge ?? Ink).A(0.85f * alpha);
        Vector2 a = new(tx - 3.2f * s, by - 0.6f), b = new(tx + 3.2f * s, by - 0.6f), t = new(tx + (tip.X - tx) * 0.35f, MathF.Min(tip.Y, by + 6 * s));
        r.Line(a, t, e, line * s);
        r.Line(b, t, e, line * s);
        Card(r, c, w, h, MathF.Min(h / 2, 5 * s), s, alpha, edge, line, fill);
        r.FillPolygon(stackalloc Vector2[] { a + new Vector2(0.4f * s, -0.8f * s), b + new Vector2(-0.4f * s, -0.8f * s), t + new Vector2(0, -1.3f * s) }, f);
    }
}
