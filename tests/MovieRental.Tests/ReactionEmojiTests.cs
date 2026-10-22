using MovieRental.Modules.Identity.Features;

namespace MovieRental.Tests;

/// <summary>
/// A reaction is stored and shown to the other person as it was sent, so it has to be exactly
/// one emoji: never text, never markup, never a row of them.
/// </summary>
public class ReactionEmojiTests
{
    [Theory]
    [InlineData("👍")]
    [InlineData("❤️")]           // heart + variation selector
    [InlineData("👍🏽")]           // skin tone
    [InlineData("🇦🇿")]           // a flag is two regional indicators
    [InlineData("👨‍👩‍👧")]       // a family joined with ZWJ
    public void One_emoji_is_a_reaction(string emoji) => Assert.True(DirectMessageActions.IsEmoji(emoji));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("a")]
    [InlineData("<b>")]
    [InlineData("👍👍")]
    [InlineData("❤️a")]
    [InlineData("1")]
    public void Anything_else_is_not(string value) => Assert.False(DirectMessageActions.IsEmoji(value));
}
