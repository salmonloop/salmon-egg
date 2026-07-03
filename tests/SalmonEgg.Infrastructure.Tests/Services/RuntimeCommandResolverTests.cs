using System;
using System.IO;
using SalmonEgg.Infrastructure.Services;

namespace SalmonEgg.Infrastructure.Tests.Services;

public sealed class RuntimeCommandResolverTests : IDisposable
{
    private readonly string _directory;
    private readonly string? _previousPath;

    public RuntimeCommandResolverTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "SalmonEggCommandResolverTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _previousPath = Environment.GetEnvironmentVariable("PATH");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _previousPath, EnvironmentVariableTarget.Process);
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void TryResolve_CommandInPath_ReturnsFullPath()
    {
        var commandPath = Path.Combine(_directory, "sample-tool");
        File.WriteAllText(commandPath, string.Empty);
        Environment.SetEnvironmentVariable("PATH", _directory, EnvironmentVariableTarget.Process);

        var resolved = RuntimeCommandResolver.TryResolve("sample-tool", out var path);

        Assert.True(resolved);
        Assert.Equal(commandPath, path);
    }

    [Fact]
    public void TryResolve_CommandMissing_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("PATH", _directory, EnvironmentVariableTarget.Process);

        var resolved = RuntimeCommandResolver.TryResolve("missing-tool", out var path);

        Assert.False(resolved);
        Assert.Equal(string.Empty, path);
    }
}
