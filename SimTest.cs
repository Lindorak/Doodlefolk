using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Doodlefolk;

/// <summary>The simulation tests (Doodlefolk.exe --simtest [quick|deep|soak] [--seed N] [--jobs N] [--only 3,7] [--out dir]).
/// Every feature in App.SimFactors has a few settings; trying every combination would take millions of runs, so a
/// covering array picks a few dozen towns in which every pair of settings meets (quick, ~90 simulated seconds each),
/// or every triple (deep, a few hundred towns, 4 minutes each), or a handful of random towns left running for half an
/// hour (soak). Each town runs as its own hidden --simrun process, several side by side, and the report lists what
/// broke with a command line that replays exactly that town. Add a feature to App.SimFactors and it's covered against
/// everything else from then on, so the tests grow with every combination new features bring.</summary>
static class SimTest
{
    public static int Run(string[] args)
    {
        string Arg(string name, string def)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : def;
        }
        string tier = Arg("--simtest", "quick").ToLowerInvariant();
        int seed = int.TryParse(Arg("--seed", ""), out var s) ? s : tier == "quick" ? 1 : Environment.TickCount & 0xFFFF;
        int jobs = int.TryParse(Arg("--jobs", ""), out var j) ? Math.Max(1, j) : Math.Clamp(Environment.ProcessorCount / 2, 1, 6);
        string outDir = Path.GetFullPath(Arg("--out", Path.Combine(Path.GetTempPath(), "Doodlefolk-simtest")));
        var only = Arg("--only", "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();

        var factors = App.SimFactors;
        int[] sizes = factors.Select(f => f.Values.Length).ToArray();
        var rng = new Random(seed);
        (List<int[]> rows, double dur) plan = tier switch
        {
            "deep" => (Coverage.Build(sizes, 3, rng), 240),
            "soak" => (Enumerable.Range(0, 6).Select(_ => sizes.Select(n => rng.Next(n)).ToArray()).ToList(), 1800),
            _ => (Coverage.Build(sizes, 2, rng), 90),
        };
        var specs = plan.rows.Select((r, i) => $"seed={seed * 1000 + i}|dur={plan.dur}|" + string.Join("|", factors.Select((f, k) => $"{f.Name}={f.Values[r[k]]}"))).ToList();

        // Our own folder: only ever cleared if we made it.
        string marker = Path.Combine(outDir, ".doodlefolk-simtest");
        if (Directory.Exists(outDir) && Directory.EnumerateFileSystemEntries(outDir).Any())
        {
            if (!File.Exists(marker)) return 2;
            try { Directory.Delete(outDir, true); } catch { }
        }
        Directory.CreateDirectory(outDir);
        File.WriteAllText(marker, "Made by Doodlefolk --simtest; safe to delete.");

        string exe = Environment.ProcessPath!;
        var results = new (string spec, int code, JsonElement? json, string note)[specs.Count];
        var sw = Stopwatch.StartNew();
        Parallel.ForEach(Enumerable.Range(0, specs.Count).Where(i => only.Count == 0 || only.Contains(i)), new ParallelOptions { MaxDegreeOfParallelism = jobs }, i =>
        {
            string dir = Path.Combine(outDir, $"town-{i:000}");
            Directory.CreateDirectory(dir);
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("--simrun"); psi.ArgumentList.Add(specs[i]);
            psi.ArgumentList.Add("--data"); psi.ArgumentList.Add(dir);
            using var p = Process.Start(psi)!;
            int limit = (int)(120_000 + plan.dur * 3000);
            string note = "";
            if (!p.WaitForExit(limit)) { try { p.Kill(); } catch { } note = $"didn't finish in {limit / 1000}s"; }
            JsonElement? json = null;
            string res = Path.Combine(dir, "simresult.json");
            try { if (File.Exists(res)) json = JsonDocument.Parse(File.ReadAllText(res)).RootElement.Clone(); } catch (Exception e) { note += " bad result: " + e.Message; }
            if (json == null && note.Length == 0) note = $"crashed (exit code {(p.HasExited ? p.ExitCode : -1)})";
            results[i] = (specs[i], p.HasExited ? p.ExitCode : -1, json, note);
        });

        // The report: what failed (with a replay line), then the measures across all towns.
        var sb = new StringBuilder();
        var ran = Enumerable.Range(0, specs.Count).Where(i => results[i].spec != null).ToList();
        int failed = 0;
        sb.AppendLine($"Doodlefolk simulation tests: {tier}, seed {seed}, {ran.Count} towns ({factors.Length} features, every {(tier == "deep" ? "triple" : tier == "quick" ? "pair" : "-")} of settings), {plan.dur:0} simulated seconds each, {sw.Elapsed.TotalSeconds:0}s");
        var failCounts = new Dictionary<string, int>();
        foreach (int i in ran)
        {
            var (spec, code, json, note) = results[i];
            bool pass = json is { } js && js.GetProperty("passed").GetBoolean() && code == 0;
            if (pass) { try { Directory.Delete(Path.Combine(outDir, $"town-{i:000}"), true); } catch { } continue; }
            failed++;
            sb.AppendLine();
            sb.AppendLine($"FAIL town {i}: {spec}");
            if (note.Length > 0) sb.AppendLine("      " + note);
            if (json is { } jf)
                foreach (var f in jf.GetProperty("fails").EnumerateArray())
                {
                    string text = f.GetString() ?? "";
                    sb.AppendLine("      " + text);
                    string rule = text.Contains("  ") ? text.Split("  ")[1] : text;
                    failCounts[rule] = failCounts.GetValueOrDefault(rule) + 1;
                }
            sb.AppendLine($"      replay: Doodlefolk.exe --simrun \"{spec}\"   (log: {Path.Combine(outDir, $"town-{i:000}", "events.log")})");
        }
        if (failCounts.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Failures by rule:");
            foreach (var (rule, n) in failCounts.OrderByDescending(x => x.Value)) sb.AppendLine($"  {n,3}  {rule}");
            // Which settings the failing towns have in common, compared with all towns: a pointer to the cause.
            var failing = ran.Where(i => !(results[i].json is { } js && js.GetProperty("passed").GetBoolean() && results[i].code == 0)).ToList();
            sb.AppendLine("Settings most over-represented in failing towns:");
            var lift = new List<(string, double)>();
            for (int k = 0; k < factors.Length; k++)
                for (int v = 0; v < factors[k].Values.Length; v++)
                {
                    string tag = $"{factors[k].Name}={factors[k].Values[v]}";
                    double inFail = failing.Count(i => results[i].spec.Contains("|" + tag + "|") || results[i].spec.EndsWith("|" + tag)) / (double)Math.Max(1, failing.Count);
                    double inAll = ran.Count(i => results[i].spec.Contains("|" + tag + "|") || results[i].spec.EndsWith("|" + tag)) / (double)Math.Max(1, ran.Count);
                    if (inAll > 0) lift.Add((tag, inFail / inAll));
                }
            foreach (var (tag, l) in lift.OrderByDescending(x => x.Item2).Take(6)) sb.AppendLine($"  ×{l:0.0}  {tag}");
        }
        // Measures across every town that reported.
        var ms = ran.Where(i => results[i].json != null).Select(i => results[i].json!.Value.GetProperty("metrics")).ToList();
        double Avg(string k) => ms.Where(m => m.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number).Select(m => m.GetProperty(k).GetDouble()).DefaultIfEmpty(0).Average();
        double Max(string k) => ms.Where(m => m.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number).Select(m => m.GetProperty(k).GetDouble()).DefaultIfEmpty(0).Max();
        var allActs = ms.Where(m => m.TryGetProperty("activities", out _)).SelectMany(m => m.GetProperty("activities").EnumerateArray().Select(a => a.GetString() ?? "")).Distinct().OrderBy(x => x).ToList();
        sb.AppendLine();
        sb.AppendLine($"Measures: speed {Avg("speed"):0.0}× real time; simulation {Avg("simMsAvg"):0.00} ms a frame (worst {Max("simMsMax"):0.0}); memory up to {Max("memoryMB"):0} MB");
        sb.AppendLine($"  life: {Avg("activitiesPerFigure"):0.0} different things per figure per town, {Avg("switchesPerMinute"):0.0} changes a minute, {Avg("activitiesPerPet"):0.0} per animal; {Avg("fights"):0.0} fights a town");
        sb.AppendLine($"  {allActs.Count} kinds of activity seen: {string.Join(", ", allActs)}");
        sb.AppendLine();
        sb.AppendLine(failed == 0 ? $"ALL {ran.Count} TOWNS PASSED" : $"{failed} OF {ran.Count} TOWNS FAILED");
        File.WriteAllText(Path.Combine(outDir, "simtest-report.txt"), sb.ToString(), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(outDir, "simtest-report.json"), JsonSerializer.Serialize(new
        {
            tier, seed, towns = ran.Count, failed,
            results = ran.Select(i => new { town = i, results[i].spec, results[i].code, results[i].note, result = results[i].json }),
        }, new JsonSerializerOptions { WriteIndented = true }));
        try { Console.Out.Write(sb.ToString()); } catch { }
        return failed == 0 ? 0 : 1;
    }
}
