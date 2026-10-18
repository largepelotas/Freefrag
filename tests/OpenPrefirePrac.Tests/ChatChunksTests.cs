using System.Text;
using OpenPrefirePrac;
using Xunit;

public class ChatChunksTests
{
    private const char Nl = ChatChunks.LineBreak;

    [Fact]
    // Bug prevented: the whole help text sent as one chat message, which CS2 cuts off after a few
    // hundred bytes so the last shortcuts never render.
    public void NoChunkExceedsTheByteBudgetWhenLinesAreShort()
    {
        var text = string.Join(Nl, Enumerable.Range(0, 12).Select(i => $"line {i}: " + new string('x', 60)));

        var chunks = ChatChunks.Split(text, 200);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(Encoding.UTF8.GetByteCount(c) <= 200));
    }

    [Fact]
    // Bug prevented: a line split in the middle, or lines reordered or dropped across chunks.
    public void KeepsLinesWholeAndInOrder()
    {
        var lines = Enumerable.Range(0, 9).Select(i => $"item {i} " + new string('y', 40)).ToArray();

        var chunks = ChatChunks.Split(string.Join(Nl, lines), 150);

        var rejoined = string.Join(Nl, chunks).Split(Nl);
        Assert.Equal(lines, rejoined);
    }

    [Fact]
    // Bug prevented: an over-long single line being dropped instead of sent on its own.
    public void SingleLongLineIsItsOwnChunk()
    {
        var longLine = new string('z', 500);
        var chunks = ChatChunks.Split("short" + Nl + longLine + Nl + "tail", 100);

        Assert.Equal(new[] { "short", longLine, "tail" }, chunks);
    }

    [Fact]
    // Bug prevented: multibyte text (zh) measured in chars instead of bytes, overflowing the limit.
    public void BudgetIsMeasuredInUtf8Bytes()
    {
        var cjk = new string('中', 50);                      // 150 bytes, 50 chars
        var chunks = ChatChunks.Split(cjk + Nl + cjk, 200);

        Assert.Equal(2, chunks.Count);
    }

    [Fact]
    public void EmptyTextGivesNoChunks()
    {
        Assert.Empty(ChatChunks.Split(""));
        Assert.Single(ChatChunks.Split("one line"));
    }
}
