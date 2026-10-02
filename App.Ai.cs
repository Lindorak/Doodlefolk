using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Doodlefolk;

/// <summary>Optional AI conversations (off by default). With an OpenAI API key (pasted into the Studio, where it's
/// stored encrypted for your Windows account only, or from the OPENAI_API_KEY environment variable), figures answer
/// what you say to them with a language model, in character: their personality, mood, likes, friends and how they
/// feel about you are sent along with what you said, to api.openai.com only, and nothing else. Responses aren't
/// stored by OpenAI (store: false). If anything goes wrong they answer the usual way.</summary>
sealed partial class App
{
    double _aiNextAt;
    int _aiToday;
    DateTime _aiDay = DateTime.Today;
    public string AiStatus = "";

    bool AiReady => _settings.AiChat && AiKey() != null;

    string? AiKey()
    {
        if (_settings.AiKeyProtected is { Length: > 0 } enc)
        {
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(enc), null, DataProtectionScope.CurrentUser)); }
            catch { }
        }
        return Environment.GetEnvironmentVariable("OPENAI_API_KEY") is { Length: > 20 } env ? env : null;
    }

    void SetAiKey(string key)
    {
        key = key.Trim();
        _settings.AiKeyProtected = key.Length == 0 ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
        _settings.Save();
        AiStatus = key.Length == 0 ? "Key removed." : "Key saved (encrypted for your Windows account).";
    }

    /// <summary>Say something to a figure: the AI answers if it's on, otherwise (or if it fails) the figure does.</summary>
    string TalkTo(Figure f, string text)
    {
        if (!AiReady) return f.Brain.Talk(text, _w);
        double now = _clock.Elapsed.TotalSeconds;
        if (DateTime.Today != _aiDay) { _aiDay = DateTime.Today; _aiToday = 0; }
        if (now < _aiNextAt || _aiToday >= _settings.AiDailyLimit) return f.Brain.Talk(text, _w);
        _aiNextAt = now + 2.5;
        _aiToday++;
        f.Emote("…", 3);
        _ = AiReply(f, text);
        return "(thinking…)";
    }

    async Task AiReply(Figure f, string text)
    {
        string reply;
        try
        {
            var body = new
            {
                model = _settings.AiModel.Length > 0 ? _settings.AiModel : "gpt-5-mini",
                instructions = Persona(f),
                input = text.Length > 400 ? text[..400] : text,
                max_output_tokens = 400,
                store = false,
            };
            using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses")
            {
                Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", AiKey());
            using var res = await Http.SendAsync(req);
            string json = await res.Content.ReadAsStringAsync();
            if (!res.IsSuccessStatusCode)
            {
                AiStatus = res.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "OpenAI didn't accept the key (401). Check it, or paste a new one.",
                    System.Net.HttpStatusCode.TooManyRequests => "OpenAI says slow down, or the account is out of credit (429).",
                    _ => $"OpenAI error {(int)res.StatusCode}.",
                };
                World.Log("ai: " + AiStatus);
                throw new HttpRequestException(AiStatus);
            }
            using var doc = JsonDocument.Parse(json);
            var sb = new StringBuilder();
            foreach (var item in doc.RootElement.GetProperty("output").EnumerateArray())
                if (item.TryGetProperty("content", out var content))
                    foreach (var c in content.EnumerateArray())
                        if (c.TryGetProperty("type", out var t) && t.GetString() == "output_text") sb.Append(c.GetProperty("text").GetString());
            reply = sb.ToString().Trim().Trim('"');
            if (reply.Length == 0) throw new InvalidDataException("empty reply");
            if (reply.Length > 160) reply = reply[..157].TrimEnd() + "…";
            AiStatus = $"Working ({_aiToday} today).";
        }
        catch (Exception e)
        {
            if (AiStatus.Length == 0 || !AiStatus.StartsWith("OpenAI")) AiStatus = "Couldn't reach OpenAI: " + e.Message;
            _overlay.BeginInvoke(() => { string local = f.Brain.Talk(text, _w); PostAll(new { t = "said", id = f.Id, text = local }); });
            return;
        }
        _overlay.BeginInvoke(() =>
        {
            if (!_w.Figures.Contains(f)) return;
            f.Emote(reply, Math.Clamp(1.5f + reply.Length * 0.06f, 2, 8));
            PostAll(new { t = "said", id = f.Id, text = reply });
        });
    }

    static string MoodWord(Brain b) => b.Fear > 0.4f ? "scared" : b.Annoyance > 0.5f ? "annoyed" : b.Sadness > 0.4f ? "sad" : b.Joy > 0.5f ? "happy" : b.Stamina < 0.3f ? "tired" : b.Boredom > 0.6f ? "bored" : "calm";

    /// <summary>Who they are, for the model.</summary>
    string Persona(Figure f)
    {
        var b = f.Brain;
        var p = f.Traits;
        string Level(float v) => v > 0.7f ? "very" : v > 0.45f ? "fairly" : v > 0.25f ? "a little" : "not at all";
        var likes = Enum.GetValues<Thing>().OrderByDescending(t => f.Tastes.Of(t)).Take(3).Select(t => t.ToString());
        var hates = Enum.GetValues<Thing>().OrderBy(t => f.Tastes.Of(t)).Take(2).Select(t => t.ToString());
        var friends = _w.Figures.Where(o => o != f).OrderByDescending(o => b.AffinityWith(o)).Take(2).Select(o => o.Name);
        var recent = b.Diary.TakeLast(4).Select(d => d.Text);
        return $"""
            You are {f.Name}, a small stick figure who lives on the user's computer desktop with other stick figures and pets.
            Personality: {Level(p.Energy)} energetic, {Level(p.Curiosity)} curious, {Level(p.Bravery)} brave, {Level(p.Playfulness)} playful, {Level(p.Aggression)} hot-headed, {Level(p.Sociability)} sociable.
            Right now you are: {b.Activity.ToLowerInvariant()}. Mood: {MoodWord(b)}. You feel about the user: {b.FeelingsAboutYou()}.
            You love {string.Join(", ", likes)}; you dislike {string.Join(", ", hates)}. Your closest friends: {string.Join(", ", friends)}.
            {(b.Job is > Job.None ? $"You work as a {Brain.JobName(b.Job)}. " : "")}Things that happened lately: {string.Join(" / ", recent)}
            The user is talking to you. Answer in character, in one or two very short sentences (under 25 words), casually, the way a little cartoon person would.
            Stay kind and family-friendly. Never say you are an AI or a language model; you're {f.Name}.
            """;
    }
}
