using Vortice.Mathematics;
using Xunit;

namespace Doodlefolk.Tests;

public class SkillTests
{
    static Figure Fig() => new(new Color4(1, 0, 0, 1), "Test", 1, new Personality { Energy = 0.5f, Curiosity = 0.5f, Bravery = 0.5f, Playfulness = 0.5f, Aggression = 0.3f, Sociability = 0.5f }, new Random(1));

    [Fact]
    public void PracticeMakesBetterWithDiminishingReturns()
    {
        var f = Fig();
        float start = f.Brain.Sk(SkillKind.Swimming);
        f.Brain.Practice(SkillKind.Swimming, 0.2f, continuous: true);
        float once = f.Brain.Sk(SkillKind.Swimming);
        f.Brain.Practice(SkillKind.Swimming, 0.2f, continuous: true);
        float twice = f.Brain.Sk(SkillKind.Swimming);
        Assert.True(once > start && twice > once);
        Assert.True(twice - once < once - start);
        Assert.True(twice <= 1);
    }

    [Fact]
    public void UnusedSkillsGetRustyButNeverBelowTalent()
    {
        var f = Fig();
        float talent = f.Brain.Sk(SkillKind.Fishing);
        f.Brain.Skills[SkillKind.Fishing] = 0.8f;
        f.Brain.LastPractised[SkillKind.Fishing] = DateTime.Now.AddDays(-20);
        f.Brain.Skills[SkillKind.Dancing] = 0.7f;
        f.Brain.LastPractised[SkillKind.Dancing] = DateTime.Now.AddDays(-1);
        f.Brain.Rust(DateTime.Now);
        Assert.InRange(f.Brain.Sk(SkillKind.Fishing), talent, 0.79f);
        Assert.Equal(0.7f, f.Brain.Sk(SkillKind.Dancing), 3);   // practised yesterday: no rust

        f.Brain.LastPractised[SkillKind.Fishing] = DateTime.Now.AddDays(-5000);
        f.Brain.Rust(DateTime.Now);
        Assert.True(f.Brain.Sk(SkillKind.Fishing) >= talent - 1e-4f);
    }

    [Fact]
    public void SportSkillsStartFromBallControl()
    {
        var keen = Fig();
        keen.Brain.Skills[SkillKind.Ball] = 0.9f;
        var novice = Fig();
        novice.Brain.Skills[SkillKind.Ball] = 0.1f;
        Assert.True(keen.Brain.Sk(SkillKind.Shooting) > novice.Brain.Sk(SkillKind.Shooting));
    }
}
