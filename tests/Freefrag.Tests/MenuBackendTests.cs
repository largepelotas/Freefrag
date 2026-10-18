using Freefrag;
using Xunit;

public class MenuBackendTests
{
    [Fact]
    public void ConfigZeroUsesChat()
    {
        // Prevents MenuType 0 ("keep the old chat menus") from being switched to the screen menu.
        Assert.Equal(MenuBackend.Chat, MenuBackend.Choose(0));
    }

    [Fact]
    public void ConfigOneUsesScreen()
    {
        // Prevents the default config from silently staying on chat menus now that no other plugin is needed.
        Assert.Equal(MenuBackend.Screen, MenuBackend.Choose(1));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    [InlineData(99)]
    public void OutOfRangeConfigUsesScreen(int configured)
    {
        // Prevents a stray value (only reachable by bypassing DefaultConfig's range check) from being
        // mistaken for chat: only 0 selects chat menus.
        Assert.Equal(MenuBackend.Screen, MenuBackend.Choose(configured));
    }
}
