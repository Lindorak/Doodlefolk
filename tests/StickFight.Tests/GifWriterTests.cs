using System.Drawing;
using System.Drawing.Imaging;
using Xunit;

namespace StickFight.Tests;

public class GifWriterTests
{
    static byte[] Frame(int w, int h, Func<int, int, Color> pixel)
    {
        var px = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var c = pixel(x, y);
                int o = (y * w + x) * 4;
                px[o] = c.B; px[o + 1] = c.G; px[o + 2] = c.R; px[o + 3] = 255;
            }
        return px;
    }

    static string Encode(int w, int h, params byte[][] frames)
    {
        string path = Path.Combine(Path.GetTempPath(), $"sf-gif-test-{Guid.NewGuid():N}.gif");
        using (var gif = new GifWriter(File.Create(path), w, h))
            foreach (var f in frames) gif.AddFrame(f, 70);
        return path;
    }

    static void AssertClose(Color expected, Color actual, int tolerance)
    {
        Assert.InRange(actual.R, Math.Max(0, expected.R - tolerance), Math.Min(255, expected.R + tolerance));
        Assert.InRange(actual.G, Math.Max(0, expected.G - tolerance), Math.Min(255, expected.G + tolerance));
        Assert.InRange(actual.B, Math.Max(0, expected.B - tolerance), Math.Min(255, expected.B + tolerance));
    }

    [Fact]
    public void FlatColoursSurviveExactlyEnough()
    {
        Color[] cols = { Color.FromArgb(229, 57, 53), Color.FromArgb(30, 136, 229), Color.FromArgb(245, 242, 235), Color.Black };
        Color At(int x, int y) => cols[(x / 10 + y / 7) % cols.Length];
        string path = Encode(64, 48, Frame(64, 48, At), Frame(64, 48, (x, y) => At(y, x)));
        try
        {
            using var img = (Bitmap)Image.FromFile(path);
            Assert.Equal(2, img.GetFrameCount(new FrameDimension(img.FrameDimensionsList[0])));
            for (int y = 0; y < 48; y += 5)
                for (int x = 0; x < 64; x += 5) AssertClose(At(x, y), img.GetPixel(x, y), 9);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void NoisyFramesRoundTripThroughDictionaryResets()
    {
        // 256 distinct colours scattered at random: forces the LZW table to fill and clear many times.
        var rng = new Random(7);
        var palette = Enumerable.Range(0, 256).Select(i => Color.FromArgb((i * 37) % 256 & 0xF8, (i * 91) % 256 & 0xF8, (i * 13) % 256 & 0xF8)).ToArray();
        int w = 300, h = 200;
        var idx = new int[w * h];
        for (int i = 0; i < idx.Length; i++) idx[i] = rng.Next(palette.Length);
        string path = Encode(w, h, Frame(w, h, (x, y) => palette[idx[y * w + x]]));
        try
        {
            using var img = (Bitmap)Image.FromFile(path);
            for (int i = 0; i < 2000; i++)
            {
                int x = rng.Next(w), y = rng.Next(h);
                AssertClose(palette[idx[y * w + x]], img.GetPixel(x, y), 9);
            }
        }
        finally { File.Delete(path); }
    }
}
