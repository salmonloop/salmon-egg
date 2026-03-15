using System;
using System.IO;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Threading;

public sealed class ThreadingComplianceTests
{
    [Fact]
    public void ChatViewModel_DoesNotBlockOnAsyncOperations()
    {
        var code = LoadFile(@"src\SalmonEgg.Presentation.Core\ViewModels\Chat\ChatViewModel.cs");

        Assert.DoesNotContain("GetAwaiter().GetResult()", code);
    }

    private static string LoadFile(string relativePath)
    {
        var root = FindRepoRoot();
        var fullPath = Path.Combine(root, relativePath);
        return File.ReadAllText(fullPath);
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
