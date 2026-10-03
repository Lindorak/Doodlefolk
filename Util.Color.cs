using Vortice.Mathematics;

namespace Doodlefolk;

// Rendering-only helpers live here so the numeric helpers can also run headlessly.
static partial class M
{
    /// <summary>A colour from hue, saturation and value (all 0..1).</summary>
    public static Color4 Hsv(float h, float s, float v)
    {
        h = (h % 1 + 1) % 1 * 6;
        int i = (int)h;
        float f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        var (r, g, b) = i switch { 0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q) };
        return new Color4(r, g, b, 1);
    }

    public static Color4 Hex(uint rgb, float a = 1) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, a);

    public static Color4 Shade(Color4 c, float k) => new(c.R * k, c.G * k, c.B * k, c.A);
}

static class Palette
{
    public static readonly (string Name, Color4 Color)[] All =
    {
        ("Red", M.Hex(0xE53935)), ("Blue", M.Hex(0x1E88E5)), ("Green", M.Hex(0x43A047)),
        ("Orange", M.Hex(0xFB8C00)), ("Purple", M.Hex(0x8E24AA)), ("Yellow", M.Hex(0xFDD835)),
        ("Cyan", M.Hex(0x00ACC1)), ("Pink", M.Hex(0xEC407A)), ("Black", M.Hex(0x262626)), ("White", M.Hex(0xF4F4F4)),
    };
}
