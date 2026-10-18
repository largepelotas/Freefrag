using Freefrag;
using Xunit;

public class ConvarRestoreTests
{
    [Theory]
    [InlineData("bot_quota")]
    [InlineData("bot_quota_mode")]
    [InlineData("BOT_QUOTA")]
    [InlineData("Bot_Quota_Mode")]
    public void BotQuotaConvarsAreNotRestored(string name)
    {
        Assert.False(ConvarRestore.ShouldRestore(name));
    }

    [Theory]
    [InlineData("mp_free_armor")]
    [InlineData("bot_difficulty")]
    [InlineData("mp_warmup_pausetimer")]
    public void OtherConvarsAreRestored(string name)
    {
        Assert.True(ConvarRestore.ShouldRestore(name));
    }
}
