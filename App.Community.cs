using System.Globalization;

namespace Doodlefolk;

/// <summary>A week's goal for everyone playing.</summary>
sealed record CommunityGoal(string Stat, string Title, long Target, int DailyCap, string Unit);

/// <summary>Steam extras (instead of a competitive global leaderboard, which wouldn't suit a cosy app and would just
/// reward whoever speeds the game up): a community goal each week that everyone works on together, counted with Steam's
/// global stats, with a small daily cap on what any one player adds (so nobody can farm it); reach it and everyone
/// playing that week gets the laurel wreath. And, if you opt in, a friends-only weekly board: biggest fish, minutes of
/// focus, Doodledex found. All of it only when Doodlefolk runs through Steam.</summary>
sealed partial class App
{
    public static readonly CommunityGoal[] Goals =
    {
        new("FISH_CAUGHT", "Catch 250,000 fish together", 250_000, 40, "fish"),
        new("FESTIVALS_HELD", "Hold 25,000 town events together", 25_000, 6, "events"),
        new("FOCUS_MINUTES", "Focus for 2,000,000 minutes together", 2_000_000, 300, "minutes"),
        new("REQUESTS_DONE", "Grant 60,000 requests together", 60_000, 10, "requests"),
        new("SONGS_SUNG", "Sing 40,000 songs together", 40_000, 10, "songs"),
    };

    static int Week(DateTime d) => ISOWeek.GetYear(d) * 100 + ISOWeek.GetWeekOfYear(d);
    public static CommunityGoal GoalThisWeek => Goals[ISOWeek.GetWeekOfYear(DateTime.Now) % Goals.Length];
    double _communityAt;
    public long CommunityProgress = -1;

    /// <summary>Count something towards the goals (Steam stats), within today's cap for that stat.</summary>
    public void Contribute(string stat, int amount)
    {
        var goal = Goals.FirstOrDefault(g => g.Stat == stat);
        if (goal == null || amount <= 0) return;
        string today = DateTime.Today.ToString("yyyy-MM-dd");
        if (_settings.ContribDay != today) { _settings.ContribDay = today; _settings.ContribToday.Clear(); }
        int used = _settings.ContribToday.GetValueOrDefault(stat);
        int add = Math.Min(amount, goal.DailyCap - used);
        if (add <= 0) return;
        _settings.ContribToday[stat] = used + add;
        int week = Week(DateTime.Now);
        if (_settings.ContribWeek != week) { _settings.ContribWeek = week; _settings.ContribThisWeek = 0; }
        if (stat == GoalThisWeek.Stat) _settings.ContribThisWeek += add;
        SteamHub.AddStat(stat, add);
    }

    /// <summary>Now and then: how's the week's goal going (and is it done)?</summary>
    void CommunityFrame(double now)
    {
        if (!SteamHub.Ready || now < _communityAt) return;
        _communityAt = now + 600;
        var goal = GoalThisWeek;
        int days = (int)DateTime.Now.DayOfWeek == 0 ? 7 : (int)DateTime.Now.DayOfWeek;   // since Monday
        SteamHub.WeekTotal(goal.Stat, days, total =>
        {
            CommunityProgress = total;
            if (total >= goal.Target && _settings.ContribThisWeek > 0 && !_settings.UnlockedHats.Contains("laurel"))
            {
                _settings.UnlockedHats.Add("laurel");
                _w.Sticker("community");
                _w.News("community", $"Everyone did it: \"{goal.Title}\"! Your town gets the laurel wreath.", 4);
                _settings.Save();
            }
        });
        // The friends' board (opt-in): this week's numbers.
        if (_settings.ShareWeekly)
        {
            int week = Week(DateTime.Now);
            float bestFish = _settings.FishLog.Values.Select(r => r.BestCm).DefaultIfEmpty(0).Max();
            SteamHub.PostWeekly($"W{week}_BIGGEST_FISH", (int)(bestFish * 10));
            SteamHub.PostWeekly($"W{week}_FOCUS", _settings.FocusMinutesWeek(week));
            SteamHub.PostWeekly($"W{week}_DEX", _settings.Dex.Count + _settings.FishLog.Count);
        }
    }

    object CommunityState()
    {
        var g = GoalThisWeek;
        int week = Week(DateTime.Now);
        return new
        {
            steam = SteamHub.Ready, title = g.Title, target = g.Target, progress = CommunityProgress, unit = g.Unit,
            mine = _settings.ContribWeek == week ? _settings.ContribThisWeek : 0, cap = g.DailyCap, laurel = _settings.UnlockedHats.Contains("laurel"),
            share = _settings.ShareWeekly, boards = SteamHub.Boards.Select(b => new { title = b.Key, entries = b.Value.Select(e => new { name = e.name, score = e.score }) }),
        };
    }
}
