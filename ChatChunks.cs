using System.Text;

namespace Freefrag;

/// <summary>
/// CS2 truncates a single chat message after a few hundred bytes, so long texts such as the help
/// screen have to go out as several PrintToChat calls. Splits on the plugin's line separator
/// (U+2029), keeping lines whole and grouping as many as fit under the byte budget.
/// </summary>
public static class ChatChunks
{
    public const char LineBreak = (char)0x2029;

    /// <summary>Conservative byte budget per chat message, leaving room for the chat prefix.</summary>
    public const int MaxChunkBytes = 400;

    public static IReadOnlyList<string> Split(string text, int maxBytes = MaxChunkBytes)
    {
        var chunks = new List<string>();
        if (string.IsNullOrEmpty(text))
            return chunks;

        var current = new StringBuilder();
        var currentBytes = 0;

        foreach (var line in text.Split(LineBreak))
        {
            var lineBytes = Encoding.UTF8.GetByteCount(line);
            var separatorBytes = current.Length > 0 ? Encoding.UTF8.GetByteCount(LineBreak.ToString()) : 0;

            if (current.Length > 0 && currentBytes + separatorBytes + lineBytes > maxBytes)
            {
                chunks.Add(current.ToString());
                current.Clear();
                currentBytes = 0;
                separatorBytes = 0;
            }

            if (current.Length > 0)
                current.Append(LineBreak);
            current.Append(line);
            currentBytes += separatorBytes + lineBytes;
        }

        if (current.Length > 0)
            chunks.Add(current.ToString());

        return chunks;
    }
}
