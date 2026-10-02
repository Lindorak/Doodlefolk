using Xunit;

namespace Doodlefolk.Tests;

public class TwitchTests
{
    [Fact]
    public void ChatLinesAreReadWithNameColourAndBadges()
    {
        var l = TwitchChat.Parse("@badge-info=;badges=moderator/1;color=#1E90FF;display-name=Cool\\sCat;mod=1;user-id=1 :coolcat!coolcat@coolcat.tmi.twitch.tv PRIVMSG #somechannel :!join please");
        Assert.NotNull(l);
        Assert.Equal("coolcat", l!.User);
        Assert.Equal("Cool Cat", l.Name);
        Assert.Equal("#1E90FF", l.Colour);
        Assert.Equal("!join please", l.Text);
        Assert.True(l.Mod);
        Assert.False(l.Broadcaster);
    }

    [Fact]
    public void ActionsAndColonsInMessagesSurvive()
    {
        var l = TwitchChat.Parse(":bob!bob@bob.tmi.twitch.tv PRIVMSG #chan :\u0001ACTION waves: hi :) \u0001");
        Assert.Equal("waves: hi :) ", l!.Text);
        Assert.Equal("", l.Colour);
    }

    [Theory]
    [InlineData("PING :tmi.twitch.tv")]
    [InlineData(":tmi.twitch.tv 001 justinfan12345 :Welcome, GLHF!")]
    [InlineData(":justinfan1!justinfan1@justinfan1.tmi.twitch.tv JOIN #chan")]
    [InlineData("@room-id=1 :tmi.twitch.tv ROOMSTATE #chan")]
    public void EverythingElseIsIgnored(string raw) => Assert.Null(TwitchChat.Parse(raw));
}
