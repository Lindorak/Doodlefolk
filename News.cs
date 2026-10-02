namespace Doodlefolk;

/// <summary>Something newsworthy that happened (for the weekly paper in the Studio).</summary>
sealed class NewsItem
{
    public DateTime When { get; set; } = DateTime.Now;
    public string Kind { get; set; } = "";
    public string Text { get; set; } = "";
    /// <summary>How big a story it is: 1 a footnote … 5 front page.</summary>
    public int Weight { get; set; } = 1;
    public List<string> Who { get; set; } = new();
}

/// <summary>A sticker for your sticker book: unlocked by things you do (or see happen).</summary>
readonly record struct StickerDef(string Key, string Art, string Title, string Hint);

static class Stickers
{
    public static readonly StickerDef[] All =
    {
        new("hello", "✏️", "Hello, world", "Draw your first figure"),
        new("fullhouse", "🏠", "Full house", "Have eight figures at once"),
        new("yeet", "🚀", "Yeet", "Throw a figure really hard"),
        new("talk", "💬", "Small talk", "Say something to a figure"),
        new("wish", "💭", "Wish granted", "Give a figure what it asked for"),
        new("wishes10", "🎁", "Fairy godparent", "Grant ten wishes"),
        new("pencil", "🖍️", "Creator", "A figure draws something with the Creator's Pencil"),
        new("hideseek", "🙈", "Found you!", "Find everyone in hide-and-seek"),
        new("tag", "🏃", "Tag, you're it", "Tag a figure in a game of tag"),
        new("catch10", "⚾", "Butterfingers no more", "Ten catches in a row"),
        new("photo", "📷", "Say cheese", "Take a photo"),
        new("champion", "🏆", "Champion", "See a tournament through to the end"),
        new("rivals", "⚔️", "Arch-rivals", "Two figures become rivals"),
        new("party", "🎂", "Party time", "Celebrate a birthday"),
        new("lovebirds", "💞", "Lovebirds", "Two figures start dating"),
        new("baby", "🍼", "Bundle of joy", "A baby is born"),
        new("home", "⛺", "Home sweet home", "A figure makes somewhere its home"),
        new("club", "🎌", "Join the club", "Friends start a club"),
        new("story", "🔥", "Around the campfire", "Hear a campfire story"),
        new("sleepover", "🌙", "Sleepover", "Friends sleep over together"),
        new("collector", "💎", "Magpie", "A figure collects five trinkets"),
        new("bloom", "🌻", "Green thumb", "A plant grows all the way into bloom"),
        new("storm", "⛈️", "Stormy weather", "Live through a thunderstorm"),
        new("snowman", "⛄", "Do you want to build a snowman?", "A snowman gets built"),
        new("pet", "🐾", "Best friends", "Adopt a pet"),
        new("adored", "💛", "Adored", "A figure adores you"),
        new("nemesis", "😠", "Nemesis", "A figure can't stand you"),
        new("welcome", "👋", "Welcome back", "Get welcomed back after time away"),
        new("paper", "📰", "Hot off the press", "Read the weekly paper"),
        new("seasons", "🍂", "Turn of the season", "See leaves, petals or fireflies"),
        new("walkies", "🦮", "Walkies!", "Take a dog for a good long walk"),
        new("grownup", "🐾", "All grown up", "A kitten, puppy or chick grows up"),
        new("cleanup", "🧹", "On poop patrol", "Clean up after a pet"),
        new("trained", "🎓", "Good boy!", "Train a pet out of a bad habit (75%)"),
        new("pettalk", "🦜", "Pretty bird", "Teach a parrot a new word"),
        new("petmode", "🏡", "Pet parent", "Try pet-only mode"),
        new("litter", "🐣", "Growing family", "A litter is born"),
        new("trick", "🎪", "Show-off", "Teach a pet a trick"),
        new("dressup", "🎀", "Dress-up", "Put clothes on a pet"),
        new("visitor", "🧳", "Company!", "Have a visitor drop by"),
        new("rarehat", "🎁", "Something special", "Find a rare hat in a gift crate"),
        new("rarepet", "🌈", "One in forty", "Meet an animal with a rare coat"),
        new("focus", "🎯", "In the zone", "Finish a focus session"),
        new("album", "📷", "Say cheese", "A big moment goes in the photo album"),
        new("fishdex", "🐟", "Angler", "Catch ten different kinds of fish"),
        new("helper", "🙏", "Wish granted", "Do something a figure asked for"),
        new("memorial", "🕯", "Remembered", "Say goodbye to someone"),
        new("toybox", "🧸", "Mad scientist", "Play with the toybox (moon gravity, a giant ball…)"),
        new("prank", "🤡", "Gotcha!", "Someone pulls a prank (prank mode)"),
        new("meme", "😂", "Meme lord", "Someone hangs up a meme (prank mode)"),
        new("goodfriend", "💛", "Good friend", "Grant ten requests"),
        new("legendfish", "🌟", "The one that didn't get away", "Catch a legendary fish"),
        new("allfish", "🏆", "Master angler", "Catch every kind of thing in the pond"),
        new("built", "🔨", "Master builder", "A fort or treehouse gets built"),
        new("shopping", "🪙", "Retail therapy", "A figure buys something with coins it earned"),
        new("dream", "💭", "Sweet dreams", "Catch a figure dreaming"),
        new("ride", "🚲", "On a roll", "A figure goes for a ride"),
        new("swim", "🏊", "Making a splash", "A figure goes for a swim"),
        new("fish", "🎣", "Gone fishing", "A figure catches a fish"),
        new("festival", "🎪", "Festival!", "Hold a town festival"),
        new("talent", "🎤", "Star of the show", "Win a talent show"),
        new("race", "🏁", "Photo finish", "See a race day through"),
        new("realweather", "🌦️", "Same sky", "Turn on your real weather"),
    };

    public static StickerDef? Find(string key) => All.FirstOrDefault(s => s.Key == key) is { Key.Length: > 0 } d ? d : null;
}

sealed partial class World
{
    public readonly List<NewsItem> NewsLog = new();
    /// <summary>Set by the app: unlock a sticker (no-op if it's already unlocked).</summary>
    public Action<string>? OnSticker;

    public void Sticker(string key) => OnSticker?.Invoke(key);

    /// <summary>Report something for the paper (repeats of the same story within ten minutes are ignored).</summary>
    public void News(string kind, string text, int weight, params Figure[] who)
    {
        var now = DateTime.Now;
        if (NewsLog.Any(n => n.Text == text && (now - n.When).TotalMinutes < 10)) return;
        NewsLog.Add(new NewsItem { When = now, Kind = kind, Text = text, Weight = weight, Who = who.Select(f => f.Name).ToList() });
        if (NewsLog.Count > 400) NewsLog.RemoveRange(0, NewsLog.Count - 400);
        Log($"news [{kind}] {text}");
        if (weight >= 3) OnMilestone?.Invoke(kind, text, weight, who);
    }

    /// <summary>A big moment (news of weight 3 and up): the app takes an album photo.</summary>
    public Action<string, string, int, Figure[]>? OnMilestone;
}
