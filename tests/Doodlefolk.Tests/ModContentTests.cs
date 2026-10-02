using Xunit;

namespace Doodlefolk.Tests;

[Collection("data")]
public class ModContentTests
{
    const string Good = """
        {
          "name": "Seaside town",
          "characters": [
            { "name": "Captain Jo", "color": "#1E88E5", "size": 1.1, "look": { "hat": "pirate" } },
            { "name": "Gull", "color": "#F4F4F4", "diary": [ { "text": "should be dropped" } ] }
          ],
          "storytellers": [ { "key": "seaside", "name": "Seaside", "blurb": "Calm, with lots of visitors", "drama": 0.5, "visitors": 2 } ],
          "events": [ { "key": "regatta", "title": "The regatta", "news": "Boats on the pond!", "decor": ["pennant", "lantern"], "food": ["fish", "no-such-dish"], "fireworks": true } ],
          "scenarios": [ { "key": "harbour", "name": "Harbour town", "characters": ["Captain Jo", "Gull", "Nobody"], "items": [ { "key": "pond", "x": 0.3 } ], "pets": ["cat"], "mood": "seaside" } ],
          "songs": [ { "title": "Sea shanty", "lyrics": "yo ho\nheave ho" } ],
          "colour scheme": "blue"
        }
        """;

    static string Write(string json)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"df-modcheck-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "mod.json");
        File.WriteAllText(file, json);
        return file;
    }

    [Fact]
    public void CheckingAGoodModSummarisesItAndWarnsAboutOddThings()
    {
        var (ok, problems, warnings, summary) = Mods.Check(Write(Good));
        Assert.True(ok, string.Join("; ", problems));
        Assert.Contains("2 characters", summary);
        Assert.Contains("1 storyteller", summary);
        Assert.Contains("1 festival", summary);
        Assert.Contains("1 scenario", summary);
        Assert.Contains("1 song", summary);
        Assert.Contains(warnings, w => w.Contains("no-such-dish"));
        Assert.Contains(warnings, w => w.Contains("\"Nobody\""));
        Assert.Contains(warnings, w => w.Contains("colour scheme"));
        // Checking doesn't load anything.
        Assert.DoesNotContain(Mods.Storytellers, s => s.Key == "seaside");
    }

    [Theory]
    [InlineData("""{ "characters": [ { "name": "", "color": "#123456" } ] }""", "needs a name")]
    [InlineData("""{ "characters": [ { "name": "Bad", "color": "blue" } ] }""", "colour should look like")]
    [InlineData("""{ "storytellers": [ { "key": "cozy" } ] }""", "built-in name")]
    [InlineData("""{ "scenarios": [ { "key": "empty", "characters": [] } ] }""", "needs some characters")]
    [InlineData("""{ "songs": [ { "title": "Silent", "lyrics": "" } ] }""", "has no words")]
    [InlineData("""{ "items": [ """, "not valid JSON")]
    public void BrokenModsAreExplained(string json, string expect)
    {
        var (ok, problems, _, _) = Mods.Check(Write(json));
        Assert.False(ok);
        Assert.Contains(problems, p => p.Contains(expect));
    }
}
