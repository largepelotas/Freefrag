using OpenPrefirePrac;
using Xunit;

public class MenuBackendTests
{
    [Fact]
    public void ConfigZeroAlwaysUsesChatEvenWhenWasdIsInstalled()
    {
        // Prevents MenuType 0 ("keep the old chat menus") from being overridden just because WASDMenuAPI is loaded.
        Assert.Equal(MenuBackend.Chat, MenuBackend.Choose(0, true));
        Assert.Equal(MenuBackend.Chat, MenuBackend.Choose(0, false));
    }

    [Fact]
    public void ConfigOneUsesWasdWhenAvailable()
    {
        // Prevents the default config from silently staying on chat menus when WASDMenuAPI is present.
        Assert.Equal(MenuBackend.Wasd, MenuBackend.Choose(1, true));
    }

    [Fact]
    public void ConfigOneFallsBackToChatWhenWasdIsMissing()
    {
        // Prevents a server without WASDMenuAPI from getting no menu at all (the default config asks for WASD).
        Assert.Equal(MenuBackend.Chat, MenuBackend.Choose(1, false));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(99)]
    public void OutOfRangeConfigIsTreatedAsWasdIfAvailable(int configured)
    {
        // Prevents a stray value (only reachable by bypassing DefaultConfig's range check) from disabling
        // the fallback: it asks for WASD, and still degrades to chat when WASD is absent.
        Assert.Equal(MenuBackend.Wasd, MenuBackend.Choose(configured, true));
        Assert.Equal(MenuBackend.Chat, MenuBackend.Choose(configured, false));
    }
}
