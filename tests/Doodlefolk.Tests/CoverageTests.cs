using Xunit;

namespace Doodlefolk.Tests;

public class CoverageTests
{
    [Theory]
    [InlineData(new[] { 2, 2, 2 }, 2)]
    [InlineData(new[] { 3, 3, 3, 3 }, 2)]
    [InlineData(new[] { 4, 3, 2, 5, 3, 2, 2, 3, 4, 2, 3, 2, 2, 2, 3, 2, 2, 2, 3, 2 }, 2)]
    [InlineData(new[] { 3, 2, 3, 2, 2, 3 }, 3)]
    public void EveryCombinationIsCovered(int[] sizes, int strength)
    {
        var rows = Coverage.Build(sizes, strength, new Random(1));
        Assert.True(Coverage.Covers(rows, sizes, strength));
        Assert.All(rows, r => Assert.All(r.Select((v, i) => (v, i)), x => Assert.InRange(x.v, 0, sizes[x.i] - 1)));
    }

    [Fact]
    public void PairsStayFewWhileEverythingExplodes()
    {
        // Twenty features with 2 to 5 settings: millions of full combinations, a few dozen pairwise scenarios.
        int[] sizes = { 4, 3, 2, 5, 3, 2, 2, 3, 4, 2, 3, 2, 2, 2, 3, 2, 2, 2, 3, 2 };
        double all = sizes.Aggregate(1.0, (a, b) => a * b);
        var rows = Coverage.Build(sizes, 2, new Random(2));
        Assert.True(all > 1e6);
        Assert.InRange(rows.Count, 20, 60);
    }

    [Fact]
    public void TriplesForTheWholeTownStayManageable()
    {
        // The deep run: every triple of 26 features, in a few hundred scenarios rather than billions, built quickly.
        int[] sizes = { 2, 3, 3, 2, 4, 2, 3, 4, 2, 4, 4, 3, 2, 2, 3, 2, 2, 2, 2, 2, 2, 2, 2, 3, 2, 2 };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rows = Coverage.Build(sizes, 3, new Random(3));
        Assert.True(sw.Elapsed.TotalSeconds < 30, $"took {sw.Elapsed.TotalSeconds:0.0}s");
        Assert.InRange(rows.Count, 64, 400);
        Assert.True(Coverage.Covers(rows, sizes, 3));
    }

    [Fact]
    public void TheSameSeedGivesTheSameScenarios()
    {
        int[] sizes = { 3, 2, 4, 2, 3 };
        var a = Coverage.Build(sizes, 2, new Random(5));
        var b = Coverage.Build(sizes, 2, new Random(5));
        Assert.Equal(a.Select(r => string.Join(",", r)), b.Select(r => string.Join(",", r)));
    }
}
