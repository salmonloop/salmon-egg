using System;
using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ChatLayoutContractTests
{
    [Fact]
    public void ChatSkeleton_UsesSharedChatContentWidthContract()
    {
        var repositoryRoot = FindRepositoryRoot();
        var skeletonXamlPath = Path.Combine(
            repositoryRoot,
            "SalmonEgg",
            "SalmonEgg",
            "Controls",
            "ChatSkeleton.xaml");

        var xaml = File.ReadAllText(skeletonXamlPath);

        Assert.Contains("ResponsiveContentHost", xaml, StringComparison.Ordinal);
        Assert.Contains("UiLayout.ContentMaxWidth", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"600\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"280\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"400\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"340\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Width=\"220\"", xaml, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SalmonEgg.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
