using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services.AcpSetup;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

/// <summary>
/// A real executable in a directory that only an <see cref="IAcpSearchPathSource"/> reports, so a test can
/// tell "the probe consulted the sources" apart from "the command happened to be on PATH".
/// </summary>
/// <remarks>
/// A real file rather than a stubbed probe: the behaviour under test is how this probe resolves and starts a
/// command, which a stub would replace rather than exercise. The command name carries a GUID so nothing the
/// host machine has installed can answer for it — which is what makes the paired negative tests meaningful.
/// </remarks>
internal sealed class ExecutableFixture : IDisposable
{
    private readonly string _root;

    private ExecutableFixture(string root, string command, string path)
    {
        _root = root;
        Command = command;
        Path = path;
        Source = new FixedSearchPathSource(System.IO.Path.GetDirectoryName(path)!);
    }

    /// <summary>The bare name to ask the probe for. Unique per fixture.</summary>
    public string Command { get; }

    /// <summary>Absolute path of the executable, for asserting which one answered.</summary>
    public string Path { get; }

    /// <summary>A source reporting the executable's directory, and nothing else.</summary>
    public IAcpSearchPathSource Source { get; }

    /// <summary>
    /// Writes an executable script whose body is <paramref name="body"/>.
    /// </summary>
    public static ExecutableFixture Create(string body)
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "acp-fixture-" + Guid.NewGuid().ToString("N"));
        var bin = System.IO.Path.Combine(root, "bin");
        Directory.CreateDirectory(bin);

        var command = "acp-fixture-cmd-" + Guid.NewGuid().ToString("N");
        var path = System.IO.Path.Combine(bin, command);
        File.WriteAllText(path, body);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return new ExecutableFixture(root, command, path);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private sealed class FixedSearchPathSource : IAcpSearchPathSource
    {
        private readonly string[] _directories;

        public FixedSearchPathSource(string directory) => _directories = new[] { directory };

        public Task<IReadOnlyList<string>> GetSearchDirectoriesAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(_directories);

        /// <summary>Nothing is cached, so there is nothing to discard.</summary>
        public void Invalidate()
        {
        }
    }
}
