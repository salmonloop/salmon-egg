using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Interfaces.Transport;
using SalmonEgg.Presentation.Core.Localization;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.Core.Tests.Localization;

public sealed class TransportErrorMessageFormatterTests
{
    [Theory]
    [InlineData("zh-Hans", true, "在 PATH 中找不到 Agent 命令“my-agent”。请安装该 Agent，或在配置中填写可执行文件的完整路径。")]
    [InlineData("zh-Hans", false, "配置的 Agent 命令“my-agent”不存在。请检查 Agent 配置中的路径。")]
    [InlineData("en-US", true, "Agent command 'my-agent' was not found on PATH. Install the agent or configure the full path to its executable.")]
    [InlineData("en-US", false, "The configured agent command 'my-agent' does not exist. Check the path in the agent configuration.")]
    public void Format_WhenCommandResolutionFailed_UsesLocalizedResource(string languageTag, bool searchedOnPath, string expected)
    {
        // Arrange
        var formatter = new TransportErrorMessageFormatter(new ResourceLocalizer(languageTag));
        var error = new TransportErrorEventArgs("Original transport detail.", kind: TransportErrorKind.ProcessStartFailed)
        {
            CommandResolutionFailure = new StdioCommandResolutionFailure("my-agent", searchedOnPath)
        };

        // Act
        var message = formatter.Format(error);

        // Assert
        Assert.Equal(expected, message);
        Assert.Equal("Original transport detail.", error.ErrorMessage);
    }

    [Fact]
    public void Format_AfterLanguageChanges_UsesCurrentLanguage()
    {
        // Arrange
        var localizer = new ResourceLocalizer("zh-Hans");
        var formatter = new TransportErrorMessageFormatter(localizer);
        var error = new TransportErrorEventArgs("Original transport detail.", kind: TransportErrorKind.ProcessStartFailed)
        {
            CommandResolutionFailure = new StdioCommandResolutionFailure("my-agent", SearchedOnPath: true)
        };

        // Act
        var chineseMessage = formatter.Format(error);
        localizer.LanguageTag = "en-US";
        var englishMessage = formatter.Format(error);

        // Assert
        Assert.StartsWith("在 PATH 中找不到", chineseMessage, StringComparison.Ordinal);
        Assert.StartsWith("Agent command", englishMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_WithoutResolutionFailure_PreservesDiagnosticsAndInnerException()
    {
        // Arrange
        var exception = new IOException("Read interrupted.");
        var error = new TransportErrorEventArgs("stdout closed", exception, TransportErrorKind.StdoutReadFailed);
        var formatter = new TransportErrorMessageFormatter(new ResourceLocalizer("zh-Hans"));

        // Act
        var message = formatter.Format(error);

        // Assert
        Assert.Equal(error.ErrorMessage, message);
        Assert.Same(exception, error.Exception);
    }

    [Fact]
    public void Format_WhenResourceIsMissing_PreservesOriginalFallback()
    {
        // Arrange
        var formatter = new TransportErrorMessageFormatter(new MutableTestCoreStringLocalizer());
        var error = new TransportErrorEventArgs("The configured agent command '/tmp/{agent.exe' does not exist.")
        {
            CommandResolutionFailure = new StdioCommandResolutionFailure("/tmp/{agent.exe", SearchedOnPath: false)
        };

        // Act
        var message = formatter.Format(error);

        // Assert
        Assert.Equal(error.ErrorMessage, message);
    }

    [Fact]
    public void Format_WhenCommandContainsBraces_PreservesLiteralPath()
    {
        // Arrange
        var formatter = new TransportErrorMessageFormatter(new ResourceLocalizer("zh-Hans"));
        var error = new TransportErrorEventArgs("Original transport detail.", kind: TransportErrorKind.ProcessStartFailed)
        {
            CommandResolutionFailure = new StdioCommandResolutionFailure("/tmp/{agent.exe", SearchedOnPath: false)
        };

        // Act
        var message = formatter.Format(error);

        // Assert
        Assert.Equal("配置的 Agent 命令“/tmp/{agent.exe”不存在。请检查 Agent 配置中的路径。", message);
    }

    private sealed class ResourceLocalizer(string languageTag) : IStringLocalizer<CoreStrings>
    {
        private static readonly ResourceManager Resources = new(typeof(CoreStrings).FullName!, typeof(CoreStrings).Assembly);

        public string LanguageTag { get; set; } = languageTag;

        public LocalizedString this[string name] => GetString(name, []);

        public LocalizedString this[string name, params object[] arguments] => GetString(name, arguments);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];

        private LocalizedString GetString(string name, object[] arguments)
        {
            var culture = CultureInfo.GetCultureInfo(LanguageTag);
            var value = Resources.GetString(name, culture);
            return new LocalizedString(name, value is null ? name : string.Format(culture, value, arguments), value is null);
        }
    }
}
