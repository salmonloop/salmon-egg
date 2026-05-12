using System;
using System.IO;

namespace SalmonEgg.Presentation.Core.Tests.Utilities;

public sealed class TranscriptPointerIntentFilterSourceTests
{
    [Fact]
    public void ResolveSourceKind_TreatsMarkdownPresenterAsContentOwnedInteraction()
    {
        var source = LoadRepoFile(
            "SalmonEgg",
            "SalmonEgg",
            "Presentation",
            "Utilities",
            "TranscriptPointerIntentFilter.cs");

        Assert.Contains("MarkdownTextPresenter", source, StringComparison.Ordinal);
        Assert.Contains("TranscriptPointerSourceKind.InteractiveChild", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ChatView_PointerWheelHandler_RespectsTranscriptPointerIntentFilter()
    {
        var source = LoadRepoFile(
            "SalmonEgg",
            "SalmonEgg",
            "Presentation",
            "Views",
            "Chat",
            "ChatView.xaml.cs");
        var methodBody = ExtractMethodBody(source, "private void OnMessagesListPointerWheelChanged");

        Assert.Contains("private void OnMessagesListPointerWheelChanged", source, StringComparison.Ordinal);
        Assert.Contains(
            "!TranscriptPointerIntentFilter.ShouldTrackViewportIntent(e.OriginalSource, MessagesList)",
            methodBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MiniChatView_PointerWheelHandler_RespectsTranscriptPointerIntentFilter()
    {
        var source = LoadRepoFile(
            "SalmonEgg",
            "SalmonEgg",
            "Presentation",
            "Views",
            "MiniWindow",
            "MiniChatView.xaml.cs");
        var methodBody = ExtractMethodBody(source, "private void OnMessagesListPointerWheelChanged");

        Assert.Contains("private void OnMessagesListPointerWheelChanged", source, StringComparison.Ordinal);
        Assert.Contains(
            "!TranscriptPointerIntentFilter.ShouldTrackViewportIntent(e.OriginalSource, MessagesList)",
            methodBody,
            StringComparison.Ordinal);
    }

    private static string ExtractMethodBody(string source, string methodSignature)
    {
        var methodStart = source.IndexOf(methodSignature, StringComparison.Ordinal);
        Assert.True(methodStart >= 0, $"Method signature not found: {methodSignature}");

        var bodyStart = source.IndexOf('{', methodStart);
        Assert.True(bodyStart >= 0, $"Method body start not found: {methodSignature}");

        var nextMethodStart = source.IndexOf("private void ", bodyStart + 1, StringComparison.Ordinal);
        if (nextMethodStart < 0)
        {
            nextMethodStart = source.Length;
        }

        return source.Substring(bodyStart, nextMethodStart - bodyStart);
    }

    private static string LoadRepoFile(params string[] segments)
    {
        var root = FindRepoRoot();
        return File.ReadAllText(Path.Combine([root, .. segments]));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SalmonEgg.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root (SalmonEgg.sln) not found.");
    }
}
