using System.Text.Json;
using Xunit;

namespace StickFight.Tests;

/// <summary>Tests that use the data folder (run one at a time: they point it at a scratch folder).</summary>
[CollectionDefinition("data", DisableParallelization = true)]
public class DataCollection { }

[Collection("data")]
public class ModsTests
{
    [Fact]
    public void ModsLoadObjectsHatsNamesAndJokesAndReportMistakes()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"sf-mods-test-{Guid.NewGuid():N}");
        AppPaths.DataDir = dir;
        Directory.CreateDirectory(Mods.Dir);
        File.WriteAllText(Path.Combine(Mods.Dir, "good.json"), """
            {
              // comments and trailing commas are fine
              "items": [
                { "key": "ut-lamp", "name": "UT lamp", "w": 12, "h": 28, "verbs": ["Dance", "Stand"], "colour": "#E040FB",
                  "svg": "<svg viewBox='0 0 12 28'><rect x='3' y='24' width='6' height='4' fill='#5D6670'/><circle cx='6' cy='12' r='2' fill='main'/><path d='M4 24 L2 8 L6 2 Z' fill='light'/><polyline points='0,0 12,0' stroke='black' fill='none'/></svg>" },
                { "key": "ut-box", "name": "UT box", "w": 20, "h": 20, "shapes": [ { "k": "r", "p": [-10, 0, 10, 20], "c": "#123456" } ] },
              ],
              "hats": [ { "key": "ut-hat", "name": "UT hat", "svg": "<svg viewBox='-12 -16 24 8'><path d='M-8 -9 L0 -15 L8 -9 Z' fill='#E53935'/></svg>" } ],
              "names": { "figures": ["Doodle"], "cats": ["Sir Pounce"] },
              "jokes": ["unit test joke"],
            }
            """);
        File.WriteAllText(Path.Combine(Mods.Dir, "bad.json"), """{ "items": [ { "key": "BAD KEY", "w": 5, "h": 5, "shapes": [] } ] }""");
        File.WriteAllText(Path.Combine(Mods.Dir, "sneaky.json"), """{ "items": [ { "key": "sneaky", "w": 5, "h": 5, "svg": "../../secret.svg" } ] }""");
        File.WriteAllText(Path.Combine(Mods.Dir, "taken.json"), """{ "items": [ { "key": "couch", "w": 5, "h": 5, "shapes": [ { "k": "e", "p": [0, 2, 2, 2] } ] } ] }""");
        try
        {
            Mods.Load();
            var lamp = ItemCatalog.Find("ut-lamp");
            Assert.NotNull(lamp);
            Assert.Equal(4, lamp!.Shapes.Length);
            Assert.Contains(Verb.Dance, lamp.Verbs);
            Assert.True(lamp.Surface > 0);                                   // Stand gives it a top
            Assert.Contains(lamp.Shapes, s => s.Col == 0 && s.Kind == 'e');  // fill="main" follows the object's colour
            Assert.Contains(lamp.Shapes, s => s.Col == 2);                   // light
            Assert.Contains(lamp.Shapes, s => s.Kind == 'c');                // unfilled polyline
            Assert.NotNull(ItemCatalog.Find("ut box"));
            Assert.NotNull(Look.Find(Look.Hats, "mod-ut-hat"));
            Assert.Contains("Doodle", Mods.FigureNames);
            Assert.Contains("Sir Pounce", Mods.PetNames[PetKind.Cat]);
            Assert.Contains("unit test joke", Mods.Jokes);
            Assert.Contains(Mods.Errors, e => e.StartsWith("bad.json"));
            Assert.Contains(Mods.Errors, e => e.StartsWith("sneaky.json") && e.Contains("inside the mods folder"));
            Assert.Contains(Mods.Errors, e => e.Contains("\"couch\" skipped"));
            Assert.NotEqual("taken", ItemCatalog.Find("couch")!.Name);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}

[Collection("data")]
public class SettingsTests
{
    [Fact]
    public void SettingsSurviveASaveAndLoad()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"sf-settings-test-{Guid.NewGuid():N}");
        AppPaths.DataDir = dir;
        Directory.CreateDirectory(dir);
        try
        {
            var s = Settings.Load();
            Assert.True(s.FirstRun);
            s.CastName = "Round trip";
            s.PauseDays = new() { 0, 6 };
            s.Reminders.Add(new Reminder { Id = 3, Text = "water the plants", When = new DateTime(2030, 1, 2, 3, 4, 0), Repeat = "weekly" });
            s.Figures.Add(new SavedFigure { Name = "Tester", Job = "Chef", Coins = 42, AgeBank = 1.5f });
            s.Save();
            var t = Settings.Load();
            Assert.False(t.FirstRun);
            Assert.False(File.Exists(Settings.FilePath + ".saving"));
            Assert.Equal("Round trip", t.CastName);
            Assert.Equal(new[] { 0, 6 }, t.PauseDays);
            Assert.Equal("water the plants", t.Reminders.Single().Text);
            Assert.Equal(new DateTime(2030, 1, 2, 3, 4, 0), t.Reminders.Single().When);
            var f = t.Figures.Single();
            Assert.Equal(("Chef", 42, 1.5f), (f.Job, f.Coins, f.AgeBank));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void BrokenSettingsFileFallsBackToDefaults()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"sf-settings-test-{Guid.NewGuid():N}");
        AppPaths.DataDir = dir;
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Settings.FilePath, "{ this is not json");
            var s = Settings.Load();
            Assert.Equal("My cast", s.CastName);
            // The damaged file is kept aside, not lost.
            Assert.Single(Directory.GetFiles(dir, "settings.broken-*.json"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Theory]
    [InlineData("Home", "home")]
    [InlineData("a/b", "a_b")]
    public void CastNamesThatWindowsTreatsAlikeShareAFile(string a, string b) =>
        Assert.Equal(App.CastFile(a), App.CastFile(b), ignoreCase: true);
}
