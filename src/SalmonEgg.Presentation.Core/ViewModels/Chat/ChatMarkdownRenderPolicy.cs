namespace SalmonEgg.Presentation.ViewModels.Chat;

public static class ChatMarkdownRenderPolicy
{
    public static ChatMarkdownRenderMode Resolve(
        string? contentType,
        bool isOutgoing,
        string? text,
        bool isFallbackSticky)
    {
        if (isFallbackSticky)
        {
            return ChatMarkdownRenderMode.FallbackPlain;
        }

        if (isOutgoing || !string.Equals(contentType, "text", StringComparison.Ordinal))
        {
            return ChatMarkdownRenderMode.PlainStreaming;
        }

        return HasUnclosedFence(text)
            ? ChatMarkdownRenderMode.PlainStreaming
            : ChatMarkdownRenderMode.MarkdownReady;
    }

    private static bool HasUnclosedFence(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var isFenceOpen = false;
        var fenceChar = '\0';
        var fenceLength = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimStart();
            if (line.Length < 3)
            {
                continue;
            }

            if (!TryGetFenceHeader(line, out var markerChar, out var markerLength))
            {
                continue;
            }

            if (!isFenceOpen)
            {
                isFenceOpen = true;
                fenceChar = markerChar;
                fenceLength = markerLength;
                continue;
            }

            if (markerChar == fenceChar && markerLength >= fenceLength)
            {
                isFenceOpen = false;
                fenceChar = '\0';
                fenceLength = 0;
            }
        }

        return isFenceOpen;
    }

    private static bool TryGetFenceHeader(string line, out char markerChar, out int markerLength)
    {
        markerChar = '\0';
        markerLength = 0;
        var first = line[0];
        if (first != '`' && first != '~')
        {
            return false;
        }

        var i = 0;
        while (i < line.Length && line[i] == first)
        {
            i++;
        }

        if (i < 3)
        {
            return false;
        }

        markerChar = first;
        markerLength = i;
        return true;
    }
}
