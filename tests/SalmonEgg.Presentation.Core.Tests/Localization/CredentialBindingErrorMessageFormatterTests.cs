using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Localization;
using SalmonEgg.Presentation.Core.Resources;

namespace SalmonEgg.Presentation.Core.Tests.Localization;

public sealed class CredentialBindingErrorMessageFormatterTests
{
    [Theory]
    [InlineData(CredentialBindingValidationError.UnsupportedBinding, "凭据来源或发送方式不受支持。请选择 Token 或 API Key，并指定环境变量或请求头。")]
    [InlineData(CredentialBindingValidationError.DestinationChanged, "配置目标已更改。请重新绑定凭据，确认允许将凭据发送到新目标。")]
    [InlineData(CredentialBindingValidationError.EnvironmentRequiresStdio, "环境变量凭据需要配置本地 Agent 的启动命令。")]
    [InlineData(CredentialBindingValidationError.InvalidEnvironmentName, "请填写环境变量名称，且不能包含等号或控制字符。")]
    [InlineData(CredentialBindingValidationError.ReservedEnvironmentName, "PATH 和 PATHEXT 由启动器管理。请选择 Agent 用于接收凭据的环境变量。")]
    [InlineData(CredentialBindingValidationError.EnvironmentHasScheme, "环境变量凭据不使用请求头方案，请清空该项。")]
    [InlineData(CredentialBindingValidationError.InvalidHeaderEndpoint, "请求头凭据需要匹配的 HTTP 或 WebSocket 地址，且地址不能包含用户信息或片段标识。")]
    [InlineData(CredentialBindingValidationError.InvalidHeaderName, "请选择用于身份验证的请求头名称，不要使用传输层保留的请求头。")]
    [InlineData(CredentialBindingValidationError.InvalidHeaderScheme, "请求头方案只能填写一个 HTTP 标记；如需直接发送凭据原值，请留空。")]
    public void Format_ChineseFailureCategory_UsesSpecificExplanation(CredentialBindingValidationError error, string expected)
    {
        // Arrange
        var localizer = new ResourceLocalizer("zh-Hans");

        // Act
        var result = CredentialBindingErrorMessageFormatter.Format(error, localizer);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("en-US")]
    [InlineData("zh-Hans")]
    [InlineData("")]
    public void Format_EveryCategory_HasRealResourceInEachSupportedLanguage(string language)
    {
        // Arrange
        var localizer = new ResourceLocalizer(language);

        foreach (var error in Enum.GetValues<CredentialBindingValidationError>())
        {
            // Act
            var result = CredentialBindingErrorMessageFormatter.Format(error, localizer);

            // Assert
            Assert.False(string.IsNullOrWhiteSpace(result));
            Assert.NotEqual(localizer.LastKey, result);
            if (language.StartsWith("en", StringComparison.Ordinal))
            {
                Assert.Equal(CredentialBindingPolicy.GetDiagnosticMessage(error), result);
            }
        }
    }

    private sealed class ResourceLocalizer(string language) : IStringLocalizer<CoreStrings>
    {
        private static readonly ResourceManager Resources = new(typeof(CoreStrings).FullName!, typeof(CoreStrings).Assembly);

        public string? LastKey { get; private set; }

        public LocalizedString this[string name] => GetString(name);

        public LocalizedString this[string name, params object[] arguments] => GetString(name);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];

        private LocalizedString GetString(string name)
        {
            LastKey = name;
            var culture = CultureInfo.GetCultureInfo(language);
            var value = Resources.GetString(name, culture);
            Assert.NotNull(value);
            return new LocalizedString(name, value);
        }
    }
}
