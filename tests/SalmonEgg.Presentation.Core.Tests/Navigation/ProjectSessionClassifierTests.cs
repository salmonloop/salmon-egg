using System;
using System.Collections.Generic;
using System.IO;
using SalmonEgg.Presentation.ViewModels.Navigation;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Navigation;

public sealed class ProjectSessionClassifierTests
{
    [Fact]
    public void ClassifyProjectId_ReturnsUnclassified_WhenCwdIsMissing()
    {
        var roots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["p1"] = NavTimeFormatter.NormalizePathForPrefixMatch(Path.Combine("C:", "Repo"))
        };

        var result = ProjectSessionClassifier.ClassifyProjectId(null, roots, "__unclassified__");

        Assert.Equal("__unclassified__", result);
    }

    [Fact]
    public void ClassifyProjectId_PicksLongestMatchingRoot()
    {
        var baseRoot = Path.Combine("C:", "Repo");
        var nestedRoot = Path.Combine(baseRoot, "Sub");

        var roots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["base"] = NavTimeFormatter.NormalizePathForPrefixMatch(baseRoot),
            ["nested"] = NavTimeFormatter.NormalizePathForPrefixMatch(nestedRoot)
        };

        var cwd = Path.Combine(nestedRoot, "Feature");
        var result = ProjectSessionClassifier.ClassifyProjectId(cwd, roots, "__unclassified__");

        Assert.Equal("nested", result);
    }

    [Fact]
    public void ClassifyProjectId_IgnoresCase_WhenMatching()
    {
        var root = Path.Combine("C:", "Repo", "Demo");
        var roots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["demo"] = NavTimeFormatter.NormalizePathForPrefixMatch(root)
        };

        var cwd = Path.Combine("c:", "repo", "demo", "Sub");
        var result = ProjectSessionClassifier.ClassifyProjectId(cwd, roots, "__unclassified__");

        Assert.Equal("demo", result);
    }

    [Fact]
    public void ClassifyProjectId_ReturnsUnclassified_WhenNoRootMatches()
    {
        var root = Path.Combine("C:", "Repo", "Demo");
        var roots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["demo"] = NavTimeFormatter.NormalizePathForPrefixMatch(root)
        };

        var cwd = Path.Combine("C:", "Other", "Path");
        var result = ProjectSessionClassifier.ClassifyProjectId(cwd, roots, "__unclassified__");

        Assert.Equal("__unclassified__", result);
    }
}
