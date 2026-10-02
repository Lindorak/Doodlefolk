using Xunit;

namespace Doodlefolk.Tests;

[Collection("data")]   // touches Seasons.South, a static
public class FishTests
{
    [Fact]
    public void SomethingIsAlwaysBiting()
    {
        Seasons.South = false;
        for (int month = 1; month <= 12; month++)
            for (int hour = 0; hour < 24; hour++)
            {
                var when = new DateTime(2026, month, 10, hour, 0, 0);
                Assert.NotEmpty(Fishes.Biting(when, raining: false));
                Assert.NotEmpty(Fishes.Biting(when, raining: true));
            }
    }

    [Fact]
    public void CatchesComeFromWhatsBitingAndFitTheirSize()
    {
        Seasons.South = false;
        var rng = new Random(1);
        var winterNight = new DateTime(2026, 1, 15, 22, 0, 0);
        var biting = Fishes.Biting(winterNight, false).Select(k => k.Key).ToHashSet();
        Assert.Contains("smelt", biting);
        Assert.DoesNotContain("bluegill", biting);   // a summer-day fish
        for (int i = 0; i < 500; i++)
        {
            var (kind, cm) = Fishes.Catch(rng, winterNight, false);
            Assert.Contains(kind.Key, biting);
            Assert.InRange(cm, kind.MinCm, kind.MaxCm);
        }
    }

    [Fact]
    public void TheSouthernHemisphereTurnsTheMonthsRound()
    {
        try
        {
            Seasons.South = true;
            Assert.Equal(7, Fishes.SeasonalMonth(new DateTime(2026, 1, 1)));
            Assert.Equal(6, Fishes.SeasonalMonth(new DateTime(2026, 12, 1)));
            // A January afternoon down south is summer: bluegill (a summer-day fish) are about.
            Assert.Contains(Fishes.Biting(new DateTime(2026, 1, 10, 14, 0, 0), false), k => k.Key == "bluegill");
        }
        finally { Seasons.South = false; }
    }

    [Fact]
    public void RainBringsTheEelsOut()
    {
        Seasons.South = false;
        var summerNight = new DateTime(2026, 7, 10, 23, 0, 0);
        Assert.DoesNotContain(Fishes.Biting(summerNight, false), k => k.Key == "eel");
        Assert.Contains(Fishes.Biting(summerNight, true), k => k.Key == "eel");
    }

    [Theory]
    [InlineData(0f, "tiny")]
    [InlineData(0.5f, "decent")]
    [InlineData(1f, "HUGE")]
    public void SizesAreRelativeToTheKind(float t, string expected)
    {
        var pike = Fishes.Find("pike")!;
        Assert.Equal(expected, Fishes.Size(pike, pike.MinCm + (pike.MaxCm - pike.MinCm) * t));
    }
}
