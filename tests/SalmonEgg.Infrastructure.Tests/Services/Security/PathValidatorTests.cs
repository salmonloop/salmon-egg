using System;
using SalmonEgg.Infrastructure.Services.Security;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Services.Security;

public sealed class PathValidatorTests
{
    [Theory]
    [InlineData("/home/user/project")]
    [InlineData(@"C:\Users\user\project")]
    [InlineData(@"\\server\share\project")]
    public void IsAbsolutePath_AcceptsProtocolAbsolutePaths(string path)
    {
        var validator = new PathValidator();

        Assert.True(validator.IsAbsolutePath(path));
    }

    [Theory]
    [InlineData("relative-path")]
    [InlineData(@"folder\child")]
    [InlineData("C:folder")]
    [InlineData(@"\drive-relative")]
    public void IsAbsolutePath_RejectsRelativePaths(string path)
    {
        var validator = new PathValidator();

        Assert.False(validator.IsAbsolutePath(path));
    }

    [Fact]
    public void GetValidationErrors_WhenPathEmpty_ReturnsEnglishMessage()
    {
        var validator = new PathValidator();

        var errors = validator.GetValidationErrors(" ");

        Assert.Contains("Path cannot be empty.", errors);
    }

    [Fact]
    public void GetValidationErrors_WhenTraversalPatternPresent_ReturnsEnglishMessage()
    {
        var validator = new PathValidator();

        var errors = validator.GetValidationErrors("../secret");

        Assert.Contains(errors, e => e.Contains("disallowed traversal patterns", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAndNormalize_WhenPathEmpty_ThrowsEnglishArgumentException()
    {
        var validator = new PathValidator();

        var ex = Assert.Throws<ArgumentException>(() => validator.ValidateAndNormalize(""));

        Assert.Contains("Path validation failed", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Path cannot be empty", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NormalizePath_WhenPathContainsNullByte_ThrowsEnglishInvalidOperationException()
    {
        var validator = new PathValidator();

        var ex = Assert.Throws<InvalidOperationException>(() => validator.NormalizePath("bad\0path"));

        Assert.Contains("Path normalization failed", ex.Message, StringComparison.Ordinal);
    }
}
