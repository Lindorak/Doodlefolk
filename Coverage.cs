namespace Doodlefolk;

/// <summary>Covering arrays for the simulation tests: a small set of scenarios in which every combination of any
/// <c>strength</c> factors' values (every pair, or every triple) appears at least once. Trying every combination of
/// twenty-odd features would take millions of runs; covering every pair takes a few dozen, and it's pairs (and
/// triples) of features meeting that most bugs come from. Greedy construction (in the spirit of AETG): each new row
/// starts from a combination nobody has covered yet and fills in the rest to cover as many more as it can.</summary>
static class Coverage
{
    public static List<int[]> Build(int[] sizes, int strength, Random rng, int tries = 30)
    {
        int n = sizes.Length;
        strength = Math.Clamp(strength, 1, n);
        int radix = sizes.Max();
        long span = (long)Math.Pow(radix, strength);
        var groups = Combinations(n, strength).ToArray();
        // For each factor, the groups it belongs to (only those can change when it does).
        var groupsOf = Enumerable.Range(0, n).Select(f => Enumerable.Range(0, groups.Length).Where(g => groups[g].Contains(f)).ToArray()).ToArray();
        long KeyOf(int g, int[] row)
        {
            long c = 0;
            foreach (int f in groups[g]) { if (row[f] < 0) return -1; c = c * radix + row[f]; }
            return g * span + c;
        }
        var uncovered = new HashSet<long>();
        for (int g = 0; g < groups.Length; g++)
            foreach (var vals in Values(groups[g], sizes))
            {
                long c = 0;
                foreach (int v in vals) c = c * radix + v;
                uncovered.Add(g * span + c);
            }
        var rows = new List<int[]>();
        var order = Enumerable.Range(0, n).ToArray();
        while (uncovered.Count > 0)
        {
            // Start from an uncovered combination so every row makes progress.
            long seed = uncovered.ElementAt(rng.Next(uncovered.Count));
            int sg = (int)(seed / span);
            long sc = seed % span;
            var start = Enumerable.Repeat(-1, n).ToArray();
            for (int i = groups[sg].Length - 1; i >= 0; i--) { start[groups[sg][i]] = (int)(sc % radix); sc /= radix; }

            int[]? best = null; int bestGain = -1;
            for (int t = 0; t < tries; t++)
            {
                var row = (int[])start.Clone();
                Shuffle(order, rng);
                foreach (int f in order)
                {
                    if (row[f] >= 0) continue;
                    int bestV = rng.Next(sizes[f]), bestCount = -1;
                    int off = rng.Next(sizes[f]);
                    for (int k = 0; k < sizes[f]; k++)
                    {
                        int v = (k + off) % sizes[f];
                        row[f] = v;
                        int c = 0;
                        foreach (int g in groupsOf[f]) { long key = KeyOf(g, row); if (key >= 0 && uncovered.Contains(key)) c++; }
                        if (c > bestCount) { bestCount = c; bestV = v; }
                    }
                    row[f] = bestV;
                }
                int gain = 0;
                for (int g = 0; g < groups.Length; g++) if (uncovered.Contains(KeyOf(g, row))) gain++;
                if (gain > bestGain) { bestGain = gain; best = row; }
            }
            for (int g = 0; g < groups.Length; g++) uncovered.Remove(KeyOf(g, best!));
            rows.Add(best!);
        }
        return rows;
    }

    static void Shuffle(int[] a, Random rng)
    {
        for (int i = a.Length - 1; i > 0; i--) { int j = rng.Next(i + 1); (a[i], a[j]) = (a[j], a[i]); }
    }

    public static IEnumerable<int[]> Combinations(int n, int k)
    {
        var c = Enumerable.Range(0, k).ToArray();
        while (true)
        {
            yield return (int[])c.Clone();
            int i = k - 1;
            while (i >= 0 && c[i] == n - k + i) i--;
            if (i < 0) yield break;
            c[i]++;
            for (int j = i + 1; j < k; j++) c[j] = c[j - 1] + 1;
        }
    }

    static IEnumerable<int[]> Values(int[] group, int[] sizes)
    {
        var v = new int[group.Length];
        while (true)
        {
            yield return (int[])v.Clone();
            int i = group.Length - 1;
            while (i >= 0 && v[i] == sizes[group[i]] - 1) { v[i] = 0; i--; }
            if (i < 0) yield break;
            v[i]++;
        }
    }

    /// <summary>True if every combination of <c>strength</c> factors' values appears in some row.</summary>
    public static bool Covers(List<int[]> rows, int[] sizes, int strength)
    {
        foreach (var g in Combinations(sizes.Length, strength))
            foreach (var vals in Values(g, sizes))
                if (!rows.Any(r => g.Select((f, i) => r[f] == vals[i]).All(x => x))) return false;
        return true;
    }
}
