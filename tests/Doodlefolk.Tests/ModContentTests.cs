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

[Collection("data")]
public class BehaviourModTests
{
    static string Write(string json)
    {
        string dir = Path.Combine(Path.GetTempPath(), $"df-beh-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "mod.json");
        File.WriteAllText(file, json);
        return file;
    }

    [Fact]
    public void ABehaviourIsDescribedNotProgrammed()
    {
        var (ok, problems, _, summary) = Mods.Check(Write("""
            { "behaviours": [ { "key": "stargazing", "name": "Watching the stars", "weight": 0.6, "cooldown": 1200,
                                "when": { "night": true, "weather": "clear", "traits": { "curiosity": 0.5 }, "hours": [21, 3] },
                                "steps": [ { "go": "high" }, { "say": ["look at them all", "so many stars"] },
                                           { "act": "sitfloor", "seconds": 20 }, { "feel": { "joy": 0.2 } },
                                           { "diary": "Watched the stars from up high.", "mood": "★" } ] } ] }
            """));
        Assert.True(ok, string.Join("; ", problems));
        Assert.Contains("1 behaviour", summary);
    }

    [Theory]
    [InlineData("""{ "behaviours": [ { "key": "x", "steps": [] } ] }""", "needs some steps")]
    [InlineData("""{ "behaviours": [ { "key": "x", "steps": [ { "run": "rm -rf" } ] } ] }""", "each step is one of")]
    [InlineData("""{ "behaviours": [ { "key": "x", "steps": [ { "act": "explode" } ] } ] }""", "unknown pose")]
    [InlineData("""{ "behaviours": [ { "key": "x", "steps": [ { "go": "the moon" } ] } ] }""", "go where?")]
    [InlineData("""{ "behaviours": [ { "key": "x", "when": { "traits": { "evil": 1 } }, "steps": [ { "wait": 1 } ] } ] }""", "unknown trait")]
    public void AnythingOutsideTheFixedStepsIsRefused(string json, string expect)
    {
        var (ok, problems, _, _) = Mods.Check(Write(json));
        Assert.False(ok);
        Assert.Contains(problems, p => p.Contains(expect));
    }

    [Fact]
    public void TooManyStepsAreRefused()
    {
        string steps = string.Join(",", Enumerable.Repeat("""{ "wait": 1 }""", 21));
        var (ok, problems, _, _) = Mods.Check(Write($$"""{ "behaviours": [ { "key": "long", "steps": [ {{steps}} ] } ] }"""));
        Assert.False(ok);
        Assert.Contains(problems, p => p.Contains("twenty steps"));
    }
}
