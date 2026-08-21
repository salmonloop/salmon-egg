using SalmonEgg.Cli.Hosting;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Cli.Tests.Hosting;

/// <summary>
/// The downgrade policy is decided before the parser exists, so the bootstrap read has to agree with the
/// parser for every argument shape the CLI accepts. A disagreement means the container was built under a
/// policy the user did not ask for, which is exactly the case the host refuses.
/// </summary>
public sealed class CliSecureStorageOptionTests
{
    [Theory]
    [InlineData()]
    [InlineData("config", "server", "list")]
    [InlineData("--help")]
    public void ResolveBootstrapPolicy_WithoutTheFlag_FailsClosed(params string[] args)
        => Assert.Equal(
            SecureStorageDowngradePolicy.FailClosed,
            CliSecureStorageOption.ResolveBootstrapPolicy(args));

    [Theory]
    [InlineData("--allow-insecure-storage")]
    [InlineData("--allow-insecure-storage", "config", "server", "list")]
    [InlineData("config", "server", "list", "--allow-insecure-storage")]
    [InlineData("--allow-insecure-storage=true")]
    [InlineData("--allow-insecure-storage:true")]
    public void ResolveBootstrapPolicy_WithTheFlag_AllowsDowngrade(params string[] args)
        => Assert.Equal(
            SecureStorageDowngradePolicy.AllowPlaintextDowngrade,
            CliSecureStorageOption.ResolveBootstrapPolicy(args));

    [Fact]
    public void ResolveBootstrapPolicy_WhenTokenFollowsTheEndOfOptionsSeparator_FailsClosed()
    {
        // Everything after "--" is positional input. Treating it as the flag would let a server name or
        // stdio argument silently enable plaintext credential storage.
        var policy = CliSecureStorageOption.ResolveBootstrapPolicy(
            ["config", "server", "list", "--", "--allow-insecure-storage"]);

        Assert.Equal(SecureStorageDowngradePolicy.FailClosed, policy);
    }

    [Fact]
    public void ResolveBootstrapPolicy_WithPrefixSharingArgument_FailsClosed()
        => Assert.Equal(
            SecureStorageDowngradePolicy.FailClosed,
            CliSecureStorageOption.ResolveBootstrapPolicy(["--allow-insecure-storage-extra"]));

    [Theory]
    [InlineData(SecureStorageDowngradePolicy.FailClosed, false, true)]
    [InlineData(SecureStorageDowngradePolicy.FailClosed, true, false)]
    [InlineData(SecureStorageDowngradePolicy.AllowPlaintextDowngrade, true, true)]
    [InlineData(SecureStorageDowngradePolicy.AllowPlaintextDowngrade, false, false)]
    public void MatchesParsedValue_ComparesBootstrapAgainstParser(
        SecureStorageDowngradePolicy bootstrapPolicy,
        bool parsedValue,
        bool expectedMatch)
        => Assert.Equal(
            expectedMatch,
            CliSecureStorageOption.MatchesParsedValue(bootstrapPolicy, parsedValue));
}
