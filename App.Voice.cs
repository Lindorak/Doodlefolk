using System.Globalization;
using System.Numerics;
using System.Speech.Recognition;

namespace StickFight;

/// <summary>Talk to them out loud (opt-in): press the microphone button, say something, and Windows' own speech
/// recognition, running on this PC, turns it into words. Nothing is sent anywhere. Say a figure's name to talk to
/// it ("Sparky, come here", "Bubbles, what's your favourite food?"), ask for something ("make a pizza", "give me a
/// bike"), or call for an event, a photo, a clip or some weather; anything else goes to whoever's nearest your cursor.</summary>
sealed partial class App
{
    SpeechRecognitionEngine? _sre;
    volatile bool _listening;
    bool _heardSomething;

    public string Listen()
    {
        if (!_settings.VoiceInput) return "Turn on voice in Settings first.";
        if (_listening) return "Already listening…";
        try
        {
            if (_sre == null)
            {
                var all = SpeechRecognitionEngine.InstalledRecognizers();
                var info = all.FirstOrDefault(r => r.Culture.Name == CultureInfo.CurrentUICulture.Name)
                           ?? all.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)
                           ?? all.FirstOrDefault(r => r.Culture.TwoLetterISOLanguageName == "en") ?? all.FirstOrDefault();
                if (info == null) return "Windows speech recognition isn't installed on this PC (Settings → Time & language → Speech).";
                _sre = new SpeechRecognitionEngine(info);
                _sre.LoadGrammar(new DictationGrammar());
                _sre.SetInputToDefaultAudioDevice();
                _sre.InitialSilenceTimeout = TimeSpan.FromSeconds(5);
                _sre.EndSilenceTimeout = TimeSpan.FromSeconds(0.8);
                _sre.BabbleTimeout = TimeSpan.FromSeconds(6);
                _sre.SpeechRecognized += (_, e) =>
                {
                    _heardSomething = true;
                    string text = e.Result.Text;
                    float conf = e.Result.Confidence;
                    _overlay.BeginInvoke(() => Heard(text, conf));
                };
                _sre.RecognizeCompleted += (_, e) =>
                {
                    _listening = false;
                    if (!_heardSomething && e.Error == null) _overlay.BeginInvoke(() => PostAll(new { t = "toast", text = "Didn't catch that. Try again?" }));
                    if (e.Error != null) World.Log("voice: " + e.Error.Message);
                };
            }
            _heardSomething = false;
            _listening = true;
            _sre.RecognizeAsync(RecognizeMode.Single);
            // Someone nearby perks up to listen.
            var ear = _w.Figures.Where(f => f.Mode == Mode.Control && !f.Brain.Asleep).OrderBy(f => Vector2.Distance(f.Jt[J.Head], _w.Cursor)).FirstOrDefault();
            ear?.Emote("👂", 2);
            return "Listening… say something.";
        }
        catch (Exception e)
        {
            _listening = false;
            _sre?.Dispose(); _sre = null;
            World.Log("voice: " + e);
            return "Couldn't start listening: " + e.Message;
        }
    }

    void Heard(string text, float confidence)
    {
        World.Log($"heard ({confidence:0.00}): {text}");
        string res = VoiceCommand(text);
        PostAll(new { t = "toast", text = $"“{text}” → {res}" });
    }

    /// <summary>Act on something said (or typed into the same box).</summary>
    string VoiceCommand(string text)
    {
        string t = text.Trim().TrimEnd('.', '!', '?').Trim();
        if (t.Length == 0) return "…";
        string low = t.ToLowerInvariant();
        // Addressed to someone by name.
        foreach (var f in _w.Figures.OrderByDescending(f => f.Name.Length))
        {
            string n = f.Name.ToLowerInvariant();
            if (!(low == n || low.StartsWith(n + " ") || low.StartsWith(n + ","))) continue;
            string rest = t[f.Name.Length..].TrimStart(',', ' ');
            if (rest.StartsWith("come", StringComparison.OrdinalIgnoreCase)) return f.Brain.CalledByUser(_w);
            return $"{f.Name}: {TalkTo(f, rest.Length > 0 ? rest : "hello")}";
        }
        foreach (var p in _w.Pets)
            if (low.StartsWith(p.Name.ToLowerInvariant()))
            {
                string rest = t[p.Name.Length..].TrimStart(',', ' ');
                if (rest.StartsWith("come", StringComparison.OrdinalIgnoreCase)) { p.CallTo(_w.Cursor); return $"{p.Name} comes over"; }
                p.Hear(rest, true, _w);
                return $"{p.Name} listens";
            }
        // Things to do.
        if (low.Contains("photo") || low.Contains("cheese")) { TakePhoto(); return "Say cheese!"; }
        if (low.Contains("record")) return StartRecording();
        if (low.Contains("festival")) return StartHappening("festival");
        if (low.Contains("talent")) return StartHappening("talent");
        if (low.Contains("race")) return StartHappening("race");
        foreach (var (word, kind) in new[] { ("thunder", WeatherKind.Storm), ("storm", WeatherKind.Storm), ("snow", WeatherKind.Snow), ("rain", WeatherKind.Rain), ("sunny", WeatherKind.Clear), ("clear", WeatherKind.Clear) })
            if (low.Contains(word) && (low.Contains("make it") || low.Contains("let it") || low.StartsWith(word)))
            { _w.Weather.Start(kind, _clock.Elapsed.TotalSeconds, _w.Rng, _w); return kind == WeatherKind.Clear ? "Clear skies" : $"Here comes the {word}"; }
        if (low.StartsWith("everyone") || low.StartsWith("everybody"))
        {
            if (low.Contains("dance")) { foreach (var f in _w.Figures) if (f.Mode == Mode.Control && f.Grounded) f.StartFidget(Fidget.Groove); return "Everyone dances!"; }
            if (low.Contains("come")) { foreach (var f in _w.Figures) f.Brain.CalledByUser(_w); return "Everyone's coming over"; }
            if (low.Contains("sleep") || low.Contains("bed")) { foreach (var f in _w.Figures) f.Emote("yawn…", 1.5f); return "They yawn"; }
        }
        foreach (var lead in new[] { "draw ", "make ", "give me ", "summon ", "i want ", "can i have ", "spawn " })
            if (low.StartsWith(lead)) return Summon(t[lead.Length..]);
        // Otherwise, whoever's closest to your cursor answers.
        var near = _w.Figures.Where(f => f.Mode == Mode.Control).OrderBy(f => Vector2.Distance(f.Jt[J.Head], _w.Cursor)).FirstOrDefault();
        return near != null ? $"{near.Name}: {TalkTo(near, t)}" : "Nobody's here to hear it.";
    }
}
