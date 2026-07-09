using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;

namespace SalmonEgg.Domain.Tests.Architecture;

[TestFixture]
public sealed class AcpSdkBoundaryTests
{
    [Test]
    public void AcpSdkProject_RemainsIndependentFromSalmonEggBusinessProjects()
    {
        var project = XDocument.Parse(LoadFile(@"src\SalmonEgg.Acp\SalmonEgg.Acp.csproj"));

        Assert.That(project.Descendants("ProjectReference"), Is.Empty);
        Assert.That(Directory.EnumerateFiles(RepoPath(@"src\SalmonEgg.Acp"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText), Has.None.Contains("SalmonEgg.Domain"));
        Assert.That(Directory.EnumerateFiles(RepoPath(@"src\SalmonEgg.Acp"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText), Has.None.Contains("SalmonEgg.Application"));
        Assert.That(Directory.EnumerateFiles(RepoPath(@"src\SalmonEgg.Acp"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText), Has.None.Contains("SalmonEgg.Presentation"));
    }

    [Test]
    public void DomainReferencesAcpSdkInsteadOfOwningProtocolPathRules()
    {
        var domainProject = XDocument.Parse(LoadFile(@"src\SalmonEgg.Domain\SalmonEgg.Domain.csproj"));

        Assert.That(File.Exists(RepoPath(@"src\SalmonEgg.Domain\Models\Protocol\ProtocolPathRules.cs")), Is.False);
        Assert.That(Directory.Exists(RepoPath(@"src\SalmonEgg.Domain\Models\JsonRpc")), Is.False);
        Assert.That(Directory.Exists(RepoPath(@"src\SalmonEgg.Domain\Models\Content")), Is.False);
        Assert.That(Directory.Exists(RepoPath(@"src\SalmonEgg.Domain\Models\Plan")), Is.False);
        Assert.That(Directory.Exists(RepoPath(@"src\SalmonEgg.Domain\Models\Tool")), Is.False);
        Assert.That(domainProject.Descendants("ProjectReference").Select(reference => (string?)reference.Attribute("Include")),
            Has.Some.EqualTo(@"..\SalmonEgg.Acp\SalmonEgg.Acp.csproj"));
    }

    private static string LoadFile(string relativePath)
        => File.ReadAllText(RepoPath(relativePath));

    private static string RepoPath(string relativePath)
        => Path.Combine(FindRepoRoot(), relativePath.Replace('\\', Path.DirectorySeparatorChar));

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SalmonEgg.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
