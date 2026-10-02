using System.Drawing;
using Xunit;

namespace Doodlefolk.Tests;

public class MemeTests
{
    static readonly (int w, int h)[] Sizes =
    {
        (1200, 1200), (600, 908), (1200, 800), (680, 438), (580, 282), (298, 403), (480, 601), (702, 395), (500, 375), (568, 335),
        (482, 361), (400, 387), (857, 1202), (500, 494), (700, 449), (1587, 1425), (700, 325), (923, 768), (750, 750), (900, 645),
    };

    [Fact]
    public void EveryTemplateHasItsSizeAndBoxesInsideThePicture()
    {
        Assert.Equal(Sizes.Length, Memes.Templates.Length);
        foreach (var t in Memes.Templates)
        {
            Assert.StartsWith("https://i.imgflip.com/", t.Url);
            foreach (var b in t.Boxes) Assert.True(b.Left >= 0 && b.Top >= 0 && b.Right <= 1 && b.Bottom <= 1, t.Name);
        }
    }

    [Fact]
    public void LongCaptionsShrinkToFitWithoutThrowing()
    {
        for (int i = 0; i < Memes.Templates.Length; i++)
        {
            var t = Memes.Templates[i];
            using var img = new Bitmap(Sizes[i].w, Sizes[i].h);
            using (var g = Graphics.FromImage(img)) g.Clear(Color.SteelBlue);
            var lines = t.Boxes.Select((_, k) => k == 0 ? "a really quite long caption that has to shrink a lot to fit in here" : "short").ToArray();
            Memes.Caption(img, t, lines);
        }
    }

    [Fact]
    public void ChoosingFitsTheMomentAndFillsInNames()
    {
        var c = new MemeContext(new HashSet<string> { "pet" }, "Mo", "Bea", "Pudding", "20°C", "");
        var rng = new Random(3);
        for (int i = 0; i < 20; i++)
        {
            var pick = Memes.Choose(c, rng);
            Assert.NotNull(pick);
            Assert.Equal("pet", pick!.Value.why);
            Assert.DoesNotContain(pick.Value.lines, l => l.Contains('{'));
        }
        // Nothing special going on: one of the everyday ones.
        var plain = Memes.Choose(new MemeContext(new HashSet<string>(), "Mo", "", "", "", ""), rng);
        Assert.Equal("any", plain!.Value.why);
    }

    [Theory]
    [InlineData("line one\nline two", 2, true)]
    [InlineData("- line one\n\"line two\"\n", 2, true)]
    [InlineData("only one", 2, false)]
    [InlineData("", 1, false)]
    public void AiRepliesAreCleanedOrRefused(string reply, int boxes, bool ok)
    {
        var lines = Memes.CleanAiLines(reply, boxes);
        Assert.Equal(ok, lines != null);
        if (ok) Assert.DoesNotContain(lines!, l => l.StartsWith('-') || l.StartsWith('"'));
    }
}
