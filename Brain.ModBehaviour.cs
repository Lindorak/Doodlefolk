using System.Numerics;
using System.Text.Json;

namespace Doodlefolk;

/// <summary>One step of a mod behaviour.</summary>
sealed record BehaviourStep(string Kind, string Arg, string[] Lines, float Seconds, Dictionary<string, float> Feel);

/// <summary>A behaviour a mod adds: when it can happen, how likely, and what they do (see docs/MODDING.md).</summary>
sealed record ModBehaviour(string Key, string Name, float Weight, float Cooldown, BehaviourWhen When, BehaviourStep[] Steps);

sealed record BehaviourWhen(bool? Night, string Weather, string[] Seasons, int HourFrom, int HourTo, Dictionary<string, float> TraitsAtLeast,
                            Thing? Likes, string NearItem, bool? Alone, float MinStamina);

/// <summary>Behaviour mods, run safely: they're descriptions, not code. A behaviour is a few steps from a fixed set
/// (go somewhere, say something, strike a pose for a while, wait, write in the diary, feel a little different), so
/// a mod can't touch files, the network or anything outside the town, and can't run away: at most twenty steps, two
/// minutes a step, and a cooldown before it can happen again.</summary>
sealed partial class Brain
{
    ModBehaviour? _mb;
    int _mbStep;
    float _mbUntil;
    bool _mbWalking;
    readonly Dictionary<string, float> _mbCooldowns = new();

    public string? ModBehaviourNow => _mb?.Name;

    bool BehaviourFits(ModBehaviour b, World w)
    {
        var c = b.When;
        if (_mbCooldowns.TryGetValue(b.Key, out var until) && _t0 < until) return false;
        if (c.Night is bool night && (w.Night > 0.5f) != night) return false;
        if (c.Weather.Length > 0 && c.Weather switch { "rain" => !w.Weather.Raining, "snow" => !w.Weather.Snowing, "clear" => w.Weather.Raining || w.Weather.Snowing, _ => false }) return false;
        if (c.Seasons.Length > 0 && !c.Seasons.Contains(w.Seasons.Now.ToString(), StringComparer.OrdinalIgnoreCase)) return false;
        int hour = DateTime.Now.Hour;
        if (c.HourFrom != c.HourTo && !(c.HourFrom < c.HourTo ? hour >= c.HourFrom && hour < c.HourTo : hour >= c.HourFrom || hour < c.HourTo)) return false;
        foreach (var (trait, min) in c.TraitsAtLeast)
        {
            float v = trait.ToLowerInvariant() switch { "energy" => P.Energy, "curiosity" => P.Curiosity, "bravery" => P.Bravery, "playfulness" => P.Playfulness, "aggression" => P.Aggression, "sociability" => P.Sociability, _ => 1 };
            if (v < min) return false;
        }
        if (c.Likes is Thing t && f.Tastes.Of(t) < 0.2f) return false;
        if (c.NearItem.Length > 0 && !w.Items.Any(i => i.Def.Key == c.NearItem && Vector2.Distance(i.Pos, f.Base) < 1500 * S)) return false;
        if (c.Alone is bool alone && w.Figures.Any(o => o != f && Vector2.Distance(o.Base, f.Base) < 400 * S) == alone) return false;
        if (Stamina < c.MinStamina) return false;
        return true;
    }

    void ModBehaviourOptions(World w, OptionList opts)
    {
        if (Mods.Behaviours.Count == 0 || Baby || f.Visitor != VisitorKind.None || World.Focus) return;
        foreach (var b in Mods.Behaviours)
            if (BehaviourFits(b, w))
                opts.Add(b.Weight * (b.When.Likes is Thing t ? 1 + f.Tastes.Of(t) : 1), () => StartBehaviour(b), b.Name);
    }

    public void StartBehaviour(ModBehaviour b)
    {
        _mb = b; _mbStep = -1; _mbUntil = 0; _mbWalking = false;
        _mbCooldowns[b.Key] = _t0 + b.Cooldown;
        NextBehaviourStep(World.Current!);
    }

    void EndBehaviour() { _mb = null; _mbWalking = false; Go(G.Idle, rng.Range(0.6f, 1.5f)); }

    /// <summary>Each brain step while a mod behaviour runs. True while it's in charge (not walking somewhere).</summary>
    bool BehaviourStep(World w)
    {
        if (_mb is not { } b) return false;
        if (f.Mode != Mode.Control) { _mb = null; return false; }
        if (_mbWalking) return false;   // the walk (navigation) is in charge; it calls back when there
        var step = b.Steps[_mbStep];
        f.DesiredVX = 0;
        switch (step.Kind)
        {
            case "act":
                f.SetAction(step.Arg switch
                {
                    "sitfloor" => Act.SitFloor, "sitedge" => Act.SitEdge, "wave" => Act.Wave, "cheer" => Act.Cheer, "talk" => Act.Talk,
                    "tap" => Act.Tap, "sleep" => Act.Lie, _ => Act.Stand,
                });
                if (step.Arg == "dance" && (f.Action != Act.Fidget || f.ActionT >= f.FidgetDur)) f.StartFidget(Fidget.Groove);
                break;
        }
        if (_t0 >= _mbUntil) NextBehaviourStep(w);
        return _mb != null;
    }

    void NextBehaviourStep(World w)
    {
        if (_mb is not { } b) return;
        _mbStep++;
        if (_mbStep >= b.Steps.Length) { EndBehaviour(); return; }
        var s = b.Steps[_mbStep];
        string Line() => s.Lines.Length == 0 ? "" : s.Lines[rng.Next(s.Lines.Length)];
        _mbUntil = _t0 + s.Seconds;
        switch (s.Kind)
        {
            case "say": f.Emote(Line(), Math.Clamp(s.Seconds, 1, 6)); break;
            case "diary": Write("mod:" + b.Key, Line(), s.Arg.Length > 0 ? s.Arg : "★", 600); _mbUntil = _t0; break;
            case "feel":
                foreach (var (k, v) in s.Feel)
                {
                    float d = Math.Clamp(v, -0.5f, 0.5f);
                    switch (k.ToLowerInvariant())
                    {
                        case "joy": if (d >= 0) Cheered(d); else Joy = M.Clamp01(Joy + d); break;
                        case "sadness": if (d >= 0) Saddened(d); else Sadness = M.Clamp01(Sadness + d); break;
                        case "boredom": Boredom = M.Clamp01(Boredom + d); break;
                        case "loneliness": Loneliness = M.Clamp01(Loneliness + d); break;
                        case "stamina": Stamina = M.Clamp01(Stamina + d); break;
                    }
                }
                _mbUntil = _t0;
                break;
            case "go":
                if (BehaviourTarget(s.Arg, w) is not { } where) { NextBehaviourStep(w); return; }
                _mbWalking = true;
                Navigate(() => _mb == b ? where : null, 16 * S, false, () => { _mbWalking = false; if (_mb == b) { _mbUntil = 0; NextBehaviourStep(w); } }, WalkPurpose.Other);
                break;
        }
    }

    /// <summary>Where "go" goes: high, low, random, near a kind of thing, near a friend.</summary>
    Vector2? BehaviourTarget(string where, World w)
    {
        var floors = w.Env.Platforms.Where(p => p.Item == null && p.X2 - p.X1 > 60 * S).ToList();
        if (floors.Count == 0) return null;
        Platform? pf = where switch
        {
            "high" => floors.OrderBy(p => p.Y).FirstOrDefault(),
            "low" => floors.OrderByDescending(p => p.Y).FirstOrDefault(),
            "random" => floors[rng.Next(floors.Count)],
            _ => null,
        };
        if (pf != null) return new Vector2(rng.Range(pf.X1 + 20 * S, pf.X2 - 20 * S), pf.Y);
        if (where.StartsWith("item:") && w.Items.Where(i => i.Def.Key == where[5..] && !Unreachable(i)).OrderBy(i => Vector2.Distance(i.Pos, f.Base)).FirstOrDefault() is { } it)
            return it.Pos + new Vector2((f.Base.X < it.Pos.X ? -1 : 1) * (it.Def.W * it.Sc * 0.5f + 12 * S), 0);
        if (where == "friend" && w.Figures.Where(o => o != f && AffinityWith(o) > 0.2f).OrderByDescending(AffinityWith).FirstOrDefault() is { } fr)
            return fr.Base + new Vector2((f.Base.X < fr.Base.X ? -1 : 1) * 40 * S, 0);
        return null;
    }
}

static partial class Mods
{
    public static readonly List<ModBehaviour> Behaviours = new();
    static readonly string[] StepKinds = { "go", "say", "act", "wait", "diary", "feel" };
    static readonly string[] Poses = { "stand", "sitfloor", "sitedge", "wave", "cheer", "dance", "talk", "tap", "sleep" };

    static ModBehaviour BehaviourFrom(JsonElement e, List<string> warnings)
    {
        string key = Key(e, "behaviour");
        string name = Text(e, "name", key, 40);
        var w = e.TryGetProperty("when", out var we) ? we : default;
        bool has(string p) => w.ValueKind == JsonValueKind.Object && w.TryGetProperty(p, out _);
        var traits = new Dictionary<string, float>();
        if (has("traits")) foreach (var t in w.GetProperty("traits").EnumerateObject())
            {
                if (t.Name.ToLowerInvariant() is not ("energy" or "curiosity" or "bravery" or "playfulness" or "aggression" or "sociability")) throw new FormatException($"behaviour \"{key}\": unknown trait \"{t.Name}\"");
                traits[t.Name] = Math.Clamp(t.Value.GetSingle(), 0, 1);
            }
        Thing? likes = has("likes") ? Enum.TryParse<Thing>(w.GetProperty("likes").GetString(), true, out var th) ? th : throw new FormatException($"behaviour \"{key}\": unknown thing \"{w.GetProperty("likes").GetString()}\"") : null;
        int[] hours = has("hours") ? w.GetProperty("hours").EnumerateArray().Select(x => Math.Clamp(x.GetInt32(), 0, 24)).ToArray() : new[] { 0, 0 };
        string weather = has("weather") ? (w.GetProperty("weather").GetString() ?? "").ToLowerInvariant() : "";
        if (weather.Length > 0 && weather is not ("rain" or "snow" or "clear")) throw new FormatException($"behaviour \"{key}\": weather is rain, snow or clear");
        string near = has("near") ? w.GetProperty("near").GetString() ?? "" : "";
        if (near.Length > 0 && ItemCatalog.Find(near) == null) warnings.Add($"behaviour \"{key}\": no object called \"{near}\" (it'll never happen)");
        var when = new BehaviourWhen(has("night") ? w.GetProperty("night").GetBoolean() : null, weather, has("seasons") ? Strings(w, "seasons").ToArray() : Array.Empty<string>(),
                                     hours.Length == 2 ? hours[0] : 0, hours.Length == 2 ? hours[1] : 0, traits, likes, near,
                                     has("alone") ? w.GetProperty("alone").GetBoolean() : null, has("stamina") ? Math.Clamp(w.GetProperty("stamina").GetSingle(), 0, 1) : 0);
        if (!e.TryGetProperty("steps", out var ss) || ss.GetArrayLength() == 0) throw new FormatException($"behaviour \"{key}\" needs some steps");
        if (ss.GetArrayLength() > 20) throw new FormatException($"behaviour \"{key}\": twenty steps at most");
        var steps = ss.EnumerateArray().Select(s =>
        {
            var prop = s.EnumerateObject().FirstOrDefault(p => StepKinds.Contains(p.Name));
            if (prop.Value.ValueKind == JsonValueKind.Undefined) throw new FormatException($"behaviour \"{key}\": each step is one of {string.Join(", ", StepKinds)}");
            float secs = Math.Clamp(s.TryGetProperty("seconds", out var sv) ? sv.GetSingle() : prop.Name == "say" ? 2.5f : prop.Name == "act" ? 8 : 0, 0, 120);
            string[] lines = prop.Name is "say" or "diary" ? (prop.Value.ValueKind == JsonValueKind.Array ? prop.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : new[] { prop.Value.GetString() ?? "" })
                                                               .Where(x => x.Length > 0).Select(x => x.Length > 80 ? x[..80] : x).ToArray() : Array.Empty<string>();
            string arg = prop.Name switch { "go" or "act" => prop.Value.GetString() ?? "", "diary" => s.TryGetProperty("mood", out var mo) ? mo.GetString() ?? "" : "", _ => "" };
            if (prop.Name == "wait") secs = Math.Clamp(prop.Value.GetSingle(), 0, 120);
            if (prop.Name == "act" && !Poses.Contains(arg)) throw new FormatException($"behaviour \"{key}\": unknown pose \"{arg}\" ({string.Join(", ", Poses)})");
            if (prop.Name == "go" && arg is not ("high" or "low" or "random" or "friend") && !arg.StartsWith("item:")) throw new FormatException($"behaviour \"{key}\": go where? (high, low, random, friend or item:<key>)");
            if (prop.Name is "say" or "diary" && lines.Length == 0) throw new FormatException($"behaviour \"{key}\": \"{prop.Name}\" needs words");
            var feel = prop.Name == "feel" ? prop.Value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetSingle()) : new Dictionary<string, float>();
            return new BehaviourStep(prop.Name, arg, lines, secs, feel);
        }).ToArray();
        return new ModBehaviour(key, name, Math.Clamp(Num(e, "weight", 0.3f), 0, 3), Math.Clamp(Num(e, "cooldown", 600), 60, 86400), when, steps);
    }
}
