using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Elders lean on a cane; sleepers dream in little clouds.</summary>
sealed partial class Figure
{
    void DrawCane(Renderer r, float fade)
    {
        if (!Elder || Mode != Mode.Control || !Grounded || HoldN != null || Action is not (Act.Stand or Act.Talk or Act.HandsHips)) return;
        Vector2 hand = Jt[J.HandN];
        var foot = new Vector2(hand.X + Facing * 5 * S, Base.Y);
        if (foot.Y - hand.Y < 6 * S) return;
        var wood = Lit(new Color4(0.45f, 0.3f, 0.17f, fade));
        r.Line(hand + new Vector2(-Facing * 3 * S, -1 * S), hand + new Vector2(Facing * 1 * S, 0), wood, 2.4f * S);
        r.Line(hand, foot, wood, 2 * S);
        r.Disc(foot, 1.3f * S, Gfx.Darker(wood, 0.3f));
    }

    /// <summary>Colour-blind help: each colour team wears its own shape on its chest.</summary>
    public static string TeamSymbol(string team) => team switch
    {
        "Red" => "▲", "Blue" => "●", "Green" => "■", "Orange" => "◆", "Purple" => "★", "Yellow" => "✚",
        "Cyan" => "⬟", "Pink" => "♥", "Black" => "✖", "White" => "○", _ => "•",
    };

    void DrawTeamBadge(Renderer r, float fade)
    {
        if (!World.ColourBlind || Mode is Mode.Spawning || fade < 0.5f) return;
        Vector2 at = Vector2.Lerp(Jt[J.Neck], Jt[J.Pelvis], 0.38f);
        float sz = 7.5f * S;
        r.Disc(at, sz * 0.62f, new Color4(1, 1, 1, 0.85f * fade));
        r.Text(TeamSymbol(Team), at + new Vector2(0, -0.2f * S), sz, new Color4(0.1f, 0.1f, 0.1f, fade));
    }

    void DrawDream(Renderer r)
    {
        if (Dream is not { } text || Mode != Mode.Control) return;
        float bob = MathF.Sin(Environment.TickCount64 / 600f) * 1.5f * S;
        Vector2 head = Jt[J.Head];
        float s = S;
        var ink = Ui.Ink.A(0.7f);
        var fill = Nightmare ? Color4.Lerp(Ui.Fill, new Color4(0.42f, 0.3f, 0.5f, 1), 0.45f).A(0.95f) : Ui.Fill.A(0.95f);
        // Little bubbles rising from the head.
        Vector2 b1 = head + new Vector2(Facing * 5 * s, -HeadR - 3 * s), b2 = head + new Vector2(Facing * 9 * s, -HeadR - 9 * s + bob);
        foreach (var (p, rad) in new[] { (b1, 1.6f * s), (b2, 2.6f * s) }) { r.Disc(p, rad + 0.9f * s, ink); r.Disc(p, rad, fill); }
        float w = MathF.Max(22, 8 + text.Length * 5f) * s, h = 13 * s;
        Vector2 c = head + new Vector2(Facing * (12 * s + w * 0.35f), -HeadR - 22 * s + bob);
        // A puffy cloud: overlapping puffs round a card.
        int n = Math.Max(3, (int)(w / (7 * s)));
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i <= n; i++)
            {
                float x = c.X - w / 2 + w * i / n;
                float pr = (i % 2 == 0 ? 5.2f : 4.4f) * s;
                foreach (float y in new[] { c.Y - h / 2, c.Y + h / 2 })
                    r.Disc(new Vector2(x, y), pass == 0 ? pr + 0.9f * s : pr, pass == 0 ? ink : fill);
            }
        r.RoundRect(c, w + 6 * s, h + 4 * s, 4 * s, fill, new Color4(0, 0, 0, 0), 0);
        r.Text(text, c + new Vector2(0, -0.3f * s), 8.5f * s, Ui.Ink.A(0.88f), true);
    }
}
