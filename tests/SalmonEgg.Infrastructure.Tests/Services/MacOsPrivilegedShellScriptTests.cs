using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SalmonEgg.Infrastructure.Desktop.Services;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Services;

/// <summary>
/// The escaping between the app and a shell running as root.
/// </summary>
/// <remarks>
/// This is a security boundary, not formatting. The text these methods build is parsed twice — once as an
/// AppleScript string literal, then by /bin/sh — and both layers run with administrator privileges. The
/// paths come from the running bundle's own location rather than from user input, but a bundle can live
/// under a directory containing a quote (an app copied to /Users/o'brien/Applications is enough), so a
/// quoting bug is a root-level command injection reachable without an attacker.
/// </remarks>
public sealed class MacOsPrivilegedShellScriptTests
{
    public static TheoryData<string> HostilePaths() =>
    [
        "/Applications/SalmonEgg.app",
        "/Users/o'brien/Applications/SalmonEgg.app",
        "/tmp/a\"b/SalmonEgg.app",
        "/tmp/a\\b/SalmonEgg.app",
        "/tmp/a b/SalmonEgg.app",
        "/tmp/$(whoami)/SalmonEgg.app",
        "/tmp/`whoami`/SalmonEgg.app",
        "/tmp/x'; echo pwned; echo '/SalmonEgg.app",
        "/tmp/x\\'; echo pwned; #/SalmonEgg.app",
        "/tmp/a$b;c|d&e/SalmonEgg.app",
    ];

    [Theory]
    [MemberData(nameof(HostilePaths))]
    public async Task ShellQuotingSurvivesARealShell(string path)
    {
        // A real /bin/sh rather than a string comparison: the claim is "the shell sees exactly this value",
        // and only the shell can settle it. Skipped where there is no POSIX shell to ask.
        Assert.SkipUnless(File.Exists("/bin/sh"), "needs a POSIX shell");

        var quoted = MacOsPrivilegedShellScript.QuoteForShell(path);
        var echoed = await RunShellAsync($"printf %s {quoted}");

        Assert.Equal(path, echoed);
    }

    [Fact]
    public async Task ShellQuotingDoesNotLetAnInjectedCommandRun()
    {
        Assert.SkipUnless(File.Exists("/bin/sh"), "needs a POSIX shell");

        // Reverse verification with a side effect rather than a string assertion: if the quoting leaks, the
        // injected command creates this file, and no comparison of the generated text would have to be
        // trusted.
        var marker = Path.Combine(Path.GetTempPath(), "salmon-egg-injection-" + Guid.NewGuid().ToString("n"));
        var hostile = $"/tmp/x'; touch {marker}; echo '/SalmonEgg.app";

        var quoted = MacOsPrivilegedShellScript.QuoteForShell(hostile);
        var echoed = await RunShellAsync($"printf %s {quoted}");

        try
        {
            Assert.Equal(hostile, echoed);
            Assert.False(File.Exists(marker), "the injected command ran, so the shell quoting leaks");
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Theory]
    [MemberData(nameof(HostilePaths))]
    public void AppleScriptEscapingRoundTrips(string path)
    {
        // AppleScript recognizes only \\ and \" inside a literal, so parsing the escaped form must yield the
        // shell command byte for byte -- anything else and the shell receives something other than what the
        // quoting produced.
        var shellCommand = MacOsPrivilegedShellScript.BuildLinkCommand(
            path + "/Contents/MacOS/cli/salmon-egg",
            "/usr/local/bin/salmon-egg",
            "/usr/local/bin");

        var escaped = MacOsPrivilegedShellScript.EscapeForAppleScript(shellCommand);

        Assert.Equal(shellCommand, ParseAppleScriptLiteral(escaped));
    }

    [Theory]
    [MemberData(nameof(HostilePaths))]
    public async Task BothLayersTogetherDeliverThePathUnchanged(string path)
    {
        Assert.SkipUnless(File.Exists("/bin/sh"), "needs a POSIX shell");

        // The full trip the real thing takes: quote for the shell, escape for AppleScript, have AppleScript
        // parse it back, then let the shell parse that. The value has to survive both.
        var quoted = MacOsPrivilegedShellScript.QuoteForShell(path);
        var statement = MacOsPrivilegedShellScript.BuildOsaScriptStatement($"printf %s {quoted}");

        var literal = ExtractAppleScriptLiteral(statement);
        var echoed = await RunShellAsync(ParseAppleScriptLiteral(literal));

        Assert.Equal(path, echoed);
    }

    [Fact]
    public void TheLinkCommandRemovesBeforeLinking()
    {
        // ln -sf on an existing symlink-to-a-directory creates the new link inside it, so the order matters.
        // The pkg's postinstall does the same thing, deliberately: an install and an in-app link must leave
        // the same result.
        var command = MacOsPrivilegedShellScript.BuildLinkCommand(
            "/Applications/SalmonEgg.app/Contents/MacOS/cli/salmon-egg",
            "/usr/local/bin/salmon-egg",
            "/usr/local/bin");

        var mkdir = command.IndexOf("/bin/mkdir -p", StringComparison.Ordinal);
        var remove = command.IndexOf("/bin/rm -f", StringComparison.Ordinal);
        var link = command.IndexOf("/bin/ln -s", StringComparison.Ordinal);

        Assert.True(mkdir >= 0 && remove > mkdir && link > remove, command);
        // Absolute tool paths: a privileged shell must not resolve its commands through an inherited PATH.
        Assert.DoesNotContain(" ln -s", command, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStatementRequestsAdministratorPrivileges()
    {
        var statement = MacOsPrivilegedShellScript.BuildOsaScriptStatement("/bin/rm -f '/usr/local/bin/salmon-egg'");

        Assert.StartsWith("do shell script \"", statement, StringComparison.Ordinal);
        Assert.EndsWith("\" with administrator privileges", statement, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyInputsAreRejected()
    {
        // A blank path would produce `ln -s '' ''`, which as root is a command whose effect depends on the
        // working directory. Refusing to build it is the only safe answer.
        Assert.Throws<ArgumentException>(() =>
            MacOsPrivilegedShellScript.BuildLinkCommand(" ", "/usr/local/bin/salmon-egg", "/usr/local/bin"));
        Assert.Throws<ArgumentException>(() => MacOsPrivilegedShellScript.BuildUnlinkCommand(string.Empty));
        Assert.Throws<ArgumentException>(() => MacOsPrivilegedShellScript.BuildOsaScriptStatement("   "));
    }

    /// <summary>Undoes AppleScript literal escaping the way AppleScript itself would.</summary>
    private static string ParseAppleScriptLiteral(string escaped)
    {
        var builder = new StringBuilder(escaped.Length);
        for (var index = 0; index < escaped.Length; index++)
        {
            if (escaped[index] == '\\' && index + 1 < escaped.Length && escaped[index + 1] is '\\' or '"')
            {
                builder.Append(escaped[++index]);
                continue;
            }

            builder.Append(escaped[index]);
        }

        return builder.ToString();
    }

    private static string ExtractAppleScriptLiteral(string statement)
    {
        const string prefix = "do shell script \"";
        const string suffix = "\" with administrator privileges";
        Assert.StartsWith(prefix, statement, StringComparison.Ordinal);
        Assert.EndsWith(suffix, statement, StringComparison.Ordinal);
        return statement[prefix.Length..^suffix.Length];
    }

    private static async Task<string> RunShellAsync(string command)
    {
        var startInfo = new ProcessStartInfo("/bin/sh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(command);

        using var process = Process.Start(startInfo);
        Assert.NotNull(process);

        var stdout = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);

        return stdout;
    }
}
