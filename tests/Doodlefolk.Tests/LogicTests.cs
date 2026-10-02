using Xunit;

namespace Doodlefolk.Tests;

public class CatalogTests
{
    [Theory]
    [InlineData("fish", "fish")]
    [InlineData("fish tank", "fishtank")]
    [InlineData("aquarium", "fishtank")]
    [InlineData("stage", "stage")]
    [InlineData("fort", "fort")]
    [InlineData("tent", "tent")]
    [InlineData("bike", "bike")]
    [InlineData("go-kart", "gokart")]
    [InlineData("pond", "pond")]
    [InlineData("fairy lights", "fairylights")]
    public void ExactNamesWinOverSharedWords(string word, string key) => Assert.Equal(key, ItemCatalog.Find(word)?.Key);

    [Fact]
    public void TypedDescriptionsFindTheObject()
    {
        var (def, size, colour, _) = ItemCatalog.Parse("a giant red couch");
        Assert.Equal("couch", def?.Key);
        Assert.True(size > 1);
        Assert.NotNull(colour);
        Assert.Equal("couch", ItemCatalog.Parse("comfy old couches").def?.Key);
    }

    [Fact]
    public void EveryObjectIsSane()
    {
        foreach (var d in ItemCatalog.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(d.Name), d.Key);
            Assert.True(d.W > 0 && d.H > 0, d.Key);
            Assert.NotEmpty(d.Shapes);
            Assert.All(d.Shapes, s => Assert.InRange(s.Col, 0, ItemDef.Fixed.Length - 1));
            if (d.Verbs.Contains(Verb.Sit)) Assert.NotEmpty(d.Seats);
        }
        Assert.Equal(ItemCatalog.All.Length, ItemCatalog.All.Select(d => d.Key).Distinct().Count());
    }

    [Fact]
    public void EveryStickerHasArt() =>
        Assert.All(StickerDefs(), s => Assert.False(string.IsNullOrEmpty(s)));

    static IEnumerable<string> StickerDefs() => new[] { "built", "ride", "swim", "fish", "festival", "talent", "race", "realweather" }
        .Select(k => Stickers.Find(k)?.Art ?? "");
}

public class LearningTests
{
    [Theory]
    [InlineData("Ride the bike", "ride the")]
    [InlineData("Ride the go-kart", "ride the")]
    [InlineData("Go fishing", "go fishing")]
    [InlineData("Dance", "dance")]
    public void ActivitiesGroupByKind(string label, string key) => Assert.Equal(key, Brain.ActivityKey(label));
}

public class TimeTests
{
    [Fact]
    public void QuietHoursDuringTheDay()
    {
        var weekdays = new[] { 1, 2, 3, 4, 5 };
        var thu = new DateTime(2026, 10, 1, 10, 0, 0);   // a Thursday
        Assert.True(App.InQuietHours(thu, "09:00", "17:00", weekdays));
        Assert.False(App.InQuietHours(thu.AddHours(8), "09:00", "17:00", weekdays));
        Assert.False(App.InQuietHours(new DateTime(2026, 10, 3, 10, 0, 0), "09:00", "17:00", weekdays));   // Saturday
    }

    [Fact]
    public void OvernightQuietHoursBelongToTheDayTheyStart()
    {
        var fridayOnly = new[] { 5 };
        Assert.True(App.InQuietHours(new DateTime(2026, 10, 2, 23, 0, 0), "22:00", "07:00", fridayOnly));   // Friday night
        Assert.True(App.InQuietHours(new DateTime(2026, 10, 3, 6, 0, 0), "22:00", "07:00", fridayOnly));    // early Saturday
        Assert.False(App.InQuietHours(new DateTime(2026, 10, 2, 6, 0, 0), "22:00", "07:00", fridayOnly));   // early Friday = Thursday night
        Assert.False(App.InQuietHours(new DateTime(2026, 10, 2, 23, 0, 0), "nonsense", "07:00", fridayOnly));
    }

    [Fact]
    public void RepeatsStepPastNow()
    {
        var past = DateTime.Now.AddDays(-3).Date.AddHours(9);
        var next = App.NextAfter(past, d => d.AddDays(1));
        Assert.True(next > DateTime.Now);
        Assert.True(next - DateTime.Now <= TimeSpan.FromDays(1));
        Assert.Equal(9, next.Hour);
    }

    [Theory]
    [InlineData(0, "Clear")]
    [InlineData(3, "Clear")]
    [InlineData(61, "Rain")]
    [InlineData(81, "Rain")]
    [InlineData(73, "Snow")]
    [InlineData(86, "Snow")]
    [InlineData(95, "Storm")]
    [InlineData(99, "Storm")]
    public void WeatherCodesMap(int code, string kind) => Assert.Equal(kind, App.WeatherFromCode(code).kind.ToString());

    [Theory]
    [InlineData(".jpg", "a picture")]
    [InlineData(".pdf", "a document")]
    [InlineData(".zip", "a zip file")]
    public void DownloadKinds(string ext, string kind) => Assert.Equal(kind, App.KindOf(ext));
}
