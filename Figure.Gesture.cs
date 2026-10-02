using System.Numerics;
using System.Text.RegularExpressions;

namespace Doodlefolk;

enum GestureKind { None, Wave, Shrug, ShakeHead, Nod, Point, ArmsUp }

/// <summary>Gestures-only mode (an idea from StickBuddies, MIT, and from the silent films stick figures
/// grew out of): instead of words in a bubble, a figure says it with its body (a wave, a shrug, a shake of the head, a
/// nod, a point, both arms up) and a little symbol. The diary still uses words: that's for you.</summary>
sealed partial class Figure
{
    public GestureKind Gesture;
    float _gestureT;

    /// <summary>What a line of speech becomes in gestures: the symbol to show and the movement to make.</summary>
    internal static (string symbol, GestureKind g) ToGesture(string text)
    {
        if (!Regex.IsMatch(text, "[A-Za-z]")) return (text, GestureKind.None);   // already a symbol (♥, !, ♪, ⏰…)
        string t = text.ToLowerInvariant();
        bool Has(string pattern) => Regex.IsMatch(t, pattern);
        if (Has(@"\b(hi|hello|hey there|bye|goodbye|morning|night|welcome|see you)\b")) return ("👋", GestureKind.Wave);
        if (Has(@"\b(no|nah|nope|ugh|stop|don'?t|boo|hate|never|rude)\b") || t.Contains("#@!")) return ("👎", GestureKind.ShakeHead);
        if (t.Contains('?') || Has(@"\b(huh|hmm|maybe|dunno|what)\b")) return ("🤷", GestureKind.Shrug);
        if (Has(@"\b(ha|haha|hehe|lol)\b")) return ("😄", GestureKind.Nod);
        if (Has(@"\b(love|thank|thanks|sweet|cute|aww)\b")) return ("♥", GestureKind.Nod);
        if (Has(@"\b(look|there|see|come|this way|over here|go)\b")) return ("👉", GestureKind.Point);
        if (Has(@"\b(zzz|yawn|tired|sleepy|nap)\b")) return ("💤", GestureKind.None);
        if (Has(@"\b(hungry|eat|food|pizza|cake|snack)\b")) return ("😋", GestureKind.Nod);
        if (t.Contains('!') || Has(@"\b(yay|wow|woo|yes|nice|won|win|did it|great|cool)\b")) return ("🙌", GestureKind.ArmsUp);
        if (Has(@"\b(sorry|sad|oh no|aw)\b")) return ("😢", GestureKind.ShakeHead);
        return ("🙂", GestureKind.Nod);
    }

    public void StartGestureFor(GestureKind g, float dur) { Gesture = g; _gestureT = dur; }

    void StartGesture(GestureKind g, float dur)
    {
        if (g == GestureKind.None) return;
        Gesture = g;
        _gestureT = MathF.Min(dur, 1.6f);
    }

    /// <summary>Layered on top of whatever pose it's in, for a moment.</summary>
    void GesturePose(float dt, ref Vector2 hN, ref Vector2 hF, ref Vector2 eN, ref float tiltT, ref float handW)
    {
        if (Gesture == GestureKind.None) return;
        _gestureT -= dt;
        if (_gestureT <= 0 || !Grounded || Mode != Mode.Control) { Gesture = GestureKind.None; return; }
        float k = _time * 9;
        switch (Gesture)
        {
            case GestureKind.Wave: hN = new(Arm * 0.3f + MathF.Sin(k * 1.4f) * Arm * 0.25f, -Arm * 0.75f); eN = new(1, 0.6f); handW = 22; break;
            case GestureKind.Shrug: hN = new(Arm * 0.55f, Arm * 0.2f); hF = new(-Arm * 0.45f, Arm * 0.2f); tiltT += 0.18f; handW = 18; break;
            case GestureKind.ShakeHead: tiltT += MathF.Sin(k * 2.2f) * 0.25f; break;
            case GestureKind.Nod: tiltT += MathF.Max(0, MathF.Sin(k * 1.3f)) * 0.3f * Facing; break;
            case GestureKind.Point: hN = new(Arm * 0.95f, -Arm * 0.15f); eN = new(0, 1); handW = 24; break;
            case GestureKind.ArmsUp: hN = new(Arm * 0.25f, -Arm * 0.9f); hF = new(-Arm * 0.2f, -Arm * 0.9f); handW = 24; break;
        }
    }
}
