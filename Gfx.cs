using System.Numerics;
using Vortice.Mathematics;

namespace Doodlefolk;

/// <summary>Graphics quality: soft shadows, drop shadows onto the windows behind, shading and highlights, faces and
/// motion trails. Presets set everything at once; each part can also be changed on its own (the preset then reads
/// "custom"). Light comes from the top left, like the Studio's paper.</summary>
sealed class GfxSettings
{
    public string Preset { get; set; } = "high";
    /// <summary>0 off, 1 simple, 2 soft.</summary>
    public int Shadows { get; set; } = 2;
    /// <summary>Faint shadows cast onto the window behind figures and things.</summary>
    public bool DropShadows { get; set; } = true;
    /// <summary>Light and shade on heads, limbs and objects.</summary>
    public bool Shading { get; set; } = true;
    /// <summary>0 none, 1 eyes, 2 eyes and mouth.</summary>
    public int Faces { get; set; } = 2;
    /// <summary>Cartoon smears behind fast punches, kicks and flips.</summary>
    public bool Trails { get; set; } = false;
    /// <summary>Detailed art for things (textures, stitching, extra parts) instead of the simple flat look.</summary>
    public bool DetailedArt { get; set; } = true;

    public GfxSettings Clone() => (GfxSettings)MemberwiseClone();

    public static GfxSettings For(string preset, int faces) => preset switch
    {
        "low" => new() { Preset = "low", Shadows = 1, DropShadows = false, Shading = false, Faces = faces, Trails = false, DetailedArt = false },
        "medium" => new() { Preset = "medium", Shadows = 2, DropShadows = false, Shading = true, Faces = faces, Trails = false },
        "ultra" => new() { Preset = "ultra", Shadows = 2, DropShadows = true, Shading = true, Faces = faces, Trails = true },
        _ => new() { Preset = "high", Shadows = 2, DropShadows = true, Shading = true, Faces = faces, Trails = false },
    };
}

static class Gfx
{
    public static GfxSettings Q = new();
    /// <summary>Where light comes from (top left), and the matching offset for shadows thrown onto windows.</summary>
    public static readonly Vector2 DropOffset = new(4.5f, 6f);
    /// <summary>Drop shadows are drawn in solid ink inside a layer of this opacity (see Renderer.BeginShadowLayer).</summary>
    public const float DropOpacity = 0.12f;
    public static readonly Color4 DropInk = new(0, 0, 0, 1);

    /// <summary>A shadow on the ground: a soft, layered blur (or one plain oval on low), nudged away from the light.</summary>
    public static void GroundShadow(Renderer r, Vector2 c, float rx, float ry, float a)
    {
        if (a <= 0.003f || Q.Shadows == 0) return;
        if (Q.Shadows == 1) { r.Oval(c, rx, ry, new Color4(0, 0, 0, a)); return; }
        c.X += rx * 0.08f;
        r.SoftShadow(c, rx * 1.4f, ry * 1.55f, MathF.Min(1, a * 1.25f));
        r.SoftShadow(c - new Vector2(rx * 0.05f, 0), rx * 0.45f, ry * 0.6f, a * 0.45f);   // contact: darkest right underneath
    }

    public static Color4 Lighter(Color4 c, float k) => new(c.R + (1 - c.R) * k, c.G + (1 - c.G) * k, c.B + (1 - c.B) * k, c.A);
    public static Color4 Darker(Color4 c, float k) => new(c.R * (1 - k), c.G * (1 - k), c.B * (1 - k), c.A);
    public static float Luma(Color4 c) => c.R * 0.3f + c.G * 0.59f + c.B * 0.11f;
}
