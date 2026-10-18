using OpenPrefirePrac;
using Xunit;

public class ToggleSettingTests
{
    [Theory]
    [InlineData("on", true)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("ON", true)]
    [InlineData("off", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("OFF", false)]
    public void ExplicitTokensSetTheValueRegardlessOfCurrent(string token, bool expected)
    {
        // Prevents "!freefrag hs on" from flipping the setting off when it is already on (or vice versa).
        Assert.Equal(expected, ToggleSetting.Parse(token, true));
        Assert.Equal(expected, ToggleSetting.Parse(token, false));
    }

    [Fact]
    public void MissingTokenTogglesTheCurrentValue()
    {
        // Prevents a bare "!freefrag hs" from printing usage instead of toggling.
        Assert.True(ToggleSetting.Parse("", false));
        Assert.False(ToggleSetting.Parse("", true));
        Assert.True(ToggleSetting.Parse(null, false));
    }

    [Fact]
    public void InvalidTokenIsRejected()
    {
        // Prevents a typo such as "!freefrag hs maybe" from silently changing the setting.
        Assert.Null(ToggleSetting.Parse("maybe", false));
        Assert.Null(ToggleSetting.Parse("2", true));
    }

    [Fact]
    public void DisplayNameMatchesState()
    {
        Assert.Equal("on", ToggleSetting.DisplayName(true));
        Assert.Equal("off", ToggleSetting.DisplayName(false));
    }
}
