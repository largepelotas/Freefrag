using Freefrag;
using Xunit;

public class ModeGateTests
{
    [Fact]
    public void StartsInactiveWithUnknownMode()
    {
        var gate = new ModeGate();
        Assert.False(gate.Active);
        Assert.Null(gate.CurrentMode);
    }

    [Fact]
    public void PrefireActivatesOnceThenIsUnchanged()
    {
        var gate = new ModeGate();
        Assert.Equal(ModeTransition.Activated, gate.Apply("Prefire"));
        Assert.True(gate.Active);
        Assert.Equal(ModeTransition.None, gate.Apply("Prefire"));
        Assert.True(gate.Active);
    }

    [Theory]
    [InlineData("prefire ")]
    [InlineData("PREFIRE")]
    [InlineData(" Prefire")]
    public void PrefireMatchIgnoresCaseAndWhitespace(string name)
    {
        var gate = new ModeGate();
        Assert.Equal(ModeTransition.Activated, gate.Apply(name));
        Assert.True(gate.Active);
    }

    [Fact]
    public void LeavingPrefireDeactivates()
    {
        var gate = new ModeGate();
        gate.Apply("Prefire");
        Assert.Equal(ModeTransition.Deactivated, gate.Apply("Retakes"));
        Assert.False(gate.Active);
        Assert.Equal("Retakes", gate.CurrentMode);
    }

    [Fact]
    public void NullWhileActiveDeactivates()
    {
        var gate = new ModeGate();
        gate.Apply("Prefire");
        Assert.Equal(ModeTransition.Deactivated, gate.Apply(null));
        Assert.False(gate.Active);
        Assert.Null(gate.CurrentMode);
    }

    [Fact]
    public void NullWhileInactiveChangesNothing()
    {
        var gate = new ModeGate();
        Assert.Equal(ModeTransition.None, gate.Apply(null));
        Assert.False(gate.Active);
    }

    [Fact]
    public void OtherModeWhileInactiveRecordsNameWithoutTransition()
    {
        var gate = new ModeGate();
        Assert.Equal(ModeTransition.None, gate.Apply("Retakes"));
        Assert.False(gate.Active);
        Assert.Equal("Retakes", gate.CurrentMode);
    }

    [Theory]
    [InlineData("Prefire", true)]
    [InlineData(" prefire ", true)]
    [InlineData("Prefire 2", false)]
    [InlineData("Retakes", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPrefire(string? name, bool expected) => Assert.Equal(expected, ModeGate.IsPrefire(name));

    [Theory]
    [InlineData("none", null)]
    [InlineData("NONE", null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    [InlineData(" Prefire ", "Prefire")]
    [InlineData("Retakes", "Retakes")]
    public void ParseSignal(string? arg, string? expected) => Assert.Equal(expected, ModeGate.ParseSignal(arg));

    [Theory]
    [InlineData("modes")]
    [InlineData("gamemodes")]
    [InlineData("MODES")]
    [InlineData("lang")]
    [InlineData("top")]
    [InlineData("stats")]
    [InlineData("help")]
    [InlineData("binds")]
    public void AllowedWhileDormantAllowList(string sub) => Assert.True(ModeGate.AllowedWhileDormant(sub));

    [Theory]
    [InlineData("prac")]
    [InlineData("exit")]
    [InlineData("df")]
    public void AllowedWhileDormantDeniesPracticeCommands(string sub) => Assert.False(ModeGate.AllowedWhileDormant(sub));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AllowedWhileDormantAllowsBareCommand(string? sub) => Assert.True(ModeGate.AllowedWhileDormant(sub));
}
