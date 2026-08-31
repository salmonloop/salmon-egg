using SalmonEgg.Presentation.Core.Localization;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Localization;

/// <summary>
/// The resolver decides what reaches the screen when a resource is unavailable, and every view model
/// that localizes goes through it. Its three unresolved cases — no localizer, missing key, blank value
/// — are asserted directly here rather than only through whichever view model happens to exercise
/// them, because the parameterized overload exists to keep exactly one implementation of them.
/// </summary>
public sealed class CoreStringResolverTests
{
    private const string Key = "AcpSetup_Step_Position";
    private const string FallbackFormat = "Step {0} of {1}";

    [Fact]
    public void Resolve_WithoutLocalizer_ReturnsFallback()
        => Assert.Equal("fallback", CoreStringResolver.Resolve(localizer: null, Key, "fallback"));

    [Fact]
    public void Resolve_WhenResourceIsMissing_ReturnsFallback()
    {
        var localizer = new MutableTestCoreStringLocalizer();

        Assert.Equal("fallback", CoreStringResolver.Resolve(localizer, "AcpSetup_NoSuchKey", "fallback"));
    }

    [Fact]
    public void Resolve_WhenResourceIsBlank_ReturnsFallback()
    {
        // A resource that exists but is empty renders as a blank label, which reads as a layout bug
        // rather than as missing copy; the fallback is at least diagnosable.
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", Key, "   ");

        Assert.Equal("fallback", CoreStringResolver.Resolve(localizer, Key, "fallback"));
    }

    [Fact]
    public void Resolve_WhenKeyIsEmpty_ReturnsFallbackWithoutConsultingLocalizer()
    {
        var localizer = new MutableTestCoreStringLocalizer();

        Assert.Equal("fallback", CoreStringResolver.Resolve(localizer, string.Empty, "fallback"));
        Assert.Equal("fallback", CoreStringResolver.Resolve(localizer, null, "fallback"));
    }

    [Fact]
    public void ResolveFormat_WithoutLocalizer_FormatsTheFallback()
    {
        // The fallback is a format string, not a literal: an unresolved parameterized resource must
        // still name its numbers, or the wizard says "Step {0} of {1}" on screen.
        Assert.Equal(
            "Step 2 of 5",
            CoreStringResolver.ResolveFormat(localizer: null, Key, FallbackFormat, 2, 5));
    }

    [Fact]
    public void ResolveFormat_WhenResourceIsMissing_FormatsTheFallback()
    {
        var localizer = new MutableTestCoreStringLocalizer();

        Assert.Equal(
            "Step 2 of 5",
            CoreStringResolver.ResolveFormat(localizer, "AcpSetup_NoSuchKey", FallbackFormat, 2, 5));
    }

    [Fact]
    public void ResolveFormat_WhenResourceIsBlank_FormatsTheFallback()
    {
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", Key, " ");

        Assert.Equal(
            "Step 2 of 5",
            CoreStringResolver.ResolveFormat(localizer, Key, FallbackFormat, 2, 5));
    }

    [Fact]
    public void ResolveFormat_WhenResourceResolves_FormatsTheResource()
    {
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", Key, "第 {0} 步，共 {1} 步");

        Assert.Equal(
            "第 2 步，共 5 步",
            CoreStringResolver.ResolveFormat(localizer, Key, FallbackFormat, 2, 5));
    }
}
