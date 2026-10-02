namespace StickFight;

/// <summary>Problems: when something goes wrong behind the scenes (the app carries on), it's noted here so the
/// Studio can show it, with a button to copy the details for a bug report. The same goes to stickfight.log.</summary>
sealed partial class App
{
    sealed record Problem(DateTime At, string Kind, string Message, string Where, string Details);

    readonly List<Problem> _problems = new();

    void RecordProblem(Exception ex)
    {
        string where = ex.StackTrace?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains("StickFight.")) ?? "";
        // The same problem over and over counts once (with the latest time).
        _problems.RemoveAll(p => p.Kind == ex.GetType().Name && p.Where == where);
        _problems.Add(new Problem(DateTime.Now, ex.GetType().Name, ex.Message, where, ex.ToString()));
        if (_problems.Count > 20) _problems.RemoveAt(0);
    }

    object ProblemsJson() => _problems.AsEnumerable().Reverse().Select(p => new { at = p.At.ToString("d MMM HH:mm"), kind = p.Kind, message = p.Message, where = p.Where });

    string ProblemReport() =>
        $"StickFight {VersionText} on Windows {Environment.OSVersion.Version}\n\n" +
        string.Join("\n\n", _problems.Select(p => $"[{p.At:yyyy-MM-dd HH:mm:ss}] {p.Details}"));
}
