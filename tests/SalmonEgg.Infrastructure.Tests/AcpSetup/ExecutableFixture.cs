using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services.AcpSetup;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

/// <summary>
/// A real, runnable executable in a directory that only an <see cref="IAcpSearchPathSource"/> reports, so a
/// test can tell "the probe consulted the sources" apart from "the command happened to be on PATH".
/// </summary>
/// <remarks>
/// A real file rather than a stubbed probe: the behaviour under test is how this probe resolves and then
/// starts a command, which a stub would replace rather than exercise. The command name carries a GUID so
/// nothing the host machine has installed can answer for it — which is what makes the paired negative tests
/// meaningful.
///
/// Written per platform rather than skipped off POSIX, because both platforms have a scriptable launcher and
/// the resolution being tested is platform-specific in exactly this way: Windows finds a bare name through
/// PATHEXT and cannot start an extensionless file, so a <c>#!/bin/sh</c> script there resolves to nothing.
/// A batch shim is also what npm actually installs on Windows, so this is the shape the wizard meets.
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

    /// <summary>The bare name to ask the probe for. Unique per fixture, and carries no extension.</summary>
    public string Command { get; }

    /// <summary>Absolute path of the executable, for asserting which one answered.</summary>
    public string Path { get; }

    /// <summary>A source reporting the executable's directory, and nothing else.</summary>
    public IAcpSearchPathSource Source { get; }

    /// <summary>
    /// Writes an executable that prints <paramref name="output"/> and exits successfully.
    /// </summary>
    /// <remarks>
    /// The caller supplies what the command should print rather than a whole script, because the script
    /// around it has to differ per platform and every caller wants the same thing from it.
    /// </remarks>
    public static ExecutableFixture Printing(string output)
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "acp-fixture-" + Guid.NewGuid().ToString("N"));
        var bin = System.IO.Path.Combine(root, "bin");
        Directory.CreateDirectory(bin);

        var command = "acp-fixture-cmd-" + Guid.NewGuid().ToString("N");
        var isWindows = OperatingSystem.IsWindows();
        // Resolved by PATHEXT on Windows, where an extensionless file cannot be started at all.
        var path = System.IO.Path.Combine(bin, isWindows ? command + ".cmd" : command);

        File.WriteAllText(
            path,
            isWindows
                // @echo off so the interpreter does not echo the command itself onto the same stdout the
                // caller reads the payload from.
                ? "@echo off\r\necho " + output + "\r\n"
                : "#!/bin/sh\necho '" + output + "'\n");

        if (!isWindows)
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
