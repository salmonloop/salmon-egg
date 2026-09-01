using SalmonEgg.Domain.Models.AcpSetup;
using SalmonEgg.Infrastructure.Desktop.AcpSetup;

namespace SalmonEgg.Infrastructure.Tests.AcpSetup;

public sealed class AcpToolchainChecksumTests
{
    [Fact]
    public void Parse_ShasumList_ShouldSelectTheNamedArchiveRatherThanFirstLine()
    {
        var document = string.Join('\n',
            new string('a', 64) + "  node-v24.20.0-darwin-arm64.tar.gz",
            new string('b', 64) + "  node-v24.20.0-linux-arm64.tar.gz",
            new string('c', 64) + "  node-v24.20.0-win-x64.zip");

        var digest = AcpToolchainChecksum.Parse(
            document,
            AcpChecksumFormat.ShasumList,
            "node-v24.20.0-linux-arm64.tar.gz");

        Assert.Equal(new string('b', 64), digest);
    }

    [Fact]
    public void Parse_ShasumList_WithoutTheRequestedArchive_ShouldReturnNull()
    {
        var document = new string('a', 64) + "  node-v24.20.0-darwin-arm64.tar.gz";

        Assert.Null(AcpToolchainChecksum.Parse(
            document,
            AcpChecksumFormat.ShasumList,
            "node-v24.20.0-linux-arm64.tar.gz"));
    }

    [Fact]
    public void Parse_SingleHash_ShouldAcceptTheSha256sumTrailingFilename()
    {
        var document = new string('d', 64) + "  uv-aarch64-unknown-linux-gnu.tar.gz";

        Assert.Equal(
            new string('d', 64),
            AcpToolchainChecksum.Parse(document, AcpChecksumFormat.SingleHash, "ignored"));
    }

    /// <summary>
    /// uv writes its Windows digests in <c>sha256sum</c> binary mode, which marks the filename with a
    /// leading asterisk. Refusing that spelling would make every Windows uv install fail verification.
    /// </summary>
    [Fact]
    public void Parse_SingleHash_ShouldAcceptBinaryModeAsterisk()
    {
        var document = new string('e', 64) + " *uv-x86_64-pc-windows-msvc.zip";

        Assert.Equal(
            new string('e', 64),
            AcpToolchainChecksum.Parse(document, AcpChecksumFormat.SingleHash, "uv-x86_64-pc-windows-msvc.zip"));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("not-a-hash node.tar.gz")]
    [InlineData("")]
    public void Parse_MalformedDocument_ShouldReturnNull(string document)
        => Assert.Null(AcpToolchainChecksum.Parse(document, AcpChecksumFormat.SingleHash, "node.tar.gz"));
}
