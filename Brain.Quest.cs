namespace Doodlefolk;

sealed partial class Brain
{
    /// <summary>You did what it asked (see App.Quests): thrilled.</summary>
    public void QuestGranted(string asked, World w)
    {
        FeelUser(0.18f, "did what I asked");
        Coins += 5;
        Cheered(0.6f);
        f.Emote(Gestures ? "🥳" : V("thank you!!", "THANK YOU THANK YOU THANK YOU!!!", "…thanks. really.", "oh! th-thank you…", "it's like a dream"), 2.4f);
        Go(G.Cheer, 1.5f);
        Write("quest", V($"I asked for something (\"{asked}\") and it happened! Best day.", $"I ASKED AND IT HAPPENED!!! (\"{asked}\")", $"Asked for something. Got it. Huh. Nice.", $"I asked… and they listened. (\"{asked}\")", $"A wish, granted: \"{asked}\""), "♥", 0);
    }
}
