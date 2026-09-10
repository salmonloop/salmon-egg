using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SalmonEgg.Application.Validators;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.ViewModels;

namespace SalmonEgg.Presentation.Core.Tests.Settings;

public sealed partial class ConfigurationEditorViewModelTests
{
    [Theory]
    [InlineData("zh-Hans", "配置目标已更改。请重新绑定凭据，确认允许将凭据发送到新目标。")]
    [InlineData("en", "The profile destination changed. Bind the credential again to approve its new destination.")]
    [InlineData("en-US", "The profile destination changed. Bind the credential again to approve its new destination.")]
    public async Task SaveConfigurationAsync_ChangedCredentialDestination_UsesLocalizedValidation(string language, string expected)
    {
        // Arrange
        var service = new Mock<IConfigurationService>();
        var editor = CreateLocalizedCredentialEditor(service, language);
        editor.LoadConfiguration(CreateCredentialProfile());
        editor.StdioCommand = "new-agent";

        // Act
        await editor.SaveConfigurationAsync();

        // Assert
        Assert.True(editor.HasError);
        Assert.Contains(expected, editor.ErrorMessage, StringComparison.Ordinal);
        Assert.Equal("agent", editor.Configuration.StdioCommand);
        service.Verify(value => value.SaveConfigurationAsync(It.IsAny<ServerConfiguration>()), Times.Never);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CredentialValidation_InvalidHeader_UsesSameChineseMessageOnBindAndSave(bool saving)
    {
        // Arrange
        var profile = new ServerConfiguration
        {
            Id = "credential-profile",
            Name = "Agent",
            Transport = TransportType.StreamableHttp,
            ServerUrl = "https://agent.example/acp",
        };
        if (saving)
        {
            profile.CredentialBinding = CredentialBindingPolicy.Create(
                profile, CredentialSource.Token, CredentialTarget.Header, "Host");
        }
        var service = new Mock<IConfigurationService>();
        var editor = CreateLocalizedCredentialEditor(service, "zh-Hans");
        editor.LoadConfiguration(profile);
        editor.CredentialName = "Host";

        // Act
        if (saving) await editor.SaveConfigurationAsync();
        else editor.BindCredentialCommand.Execute(null);

        // Assert
        Assert.True(editor.HasError);
        Assert.Equal("验证失败：请选择用于身份验证的请求头名称，不要使用传输层保留的请求头。", editor.ErrorMessage);
        service.Verify(value => value.SaveConfigurationAsync(It.IsAny<ServerConfiguration>()), Times.Never);
    }

    private static ConfigurationEditorViewModel CreateLocalizedCredentialEditor(Mock<IConfigurationService> service, string language)
        => new(new ServerConfigurationValidator(), service.Object, CreateTransportSupportPolicy(supportsStdioTransport: true),
            new CredentialResourceLocalizer(language), NullLogger<ConfigurationEditorViewModel>.Instance);

    private sealed class CredentialResourceLocalizer(string language) : IStringLocalizer<CoreStrings>
    {
        private static readonly ResourceManager Resources = new(typeof(CoreStrings).FullName!, typeof(CoreStrings).Assembly);

        public LocalizedString this[string name] => GetString(name, []);

        public LocalizedString this[string name, params object[] arguments] => GetString(name, arguments);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];

        private LocalizedString GetString(string name, object[] arguments)
        {
            var culture = CultureInfo.GetCultureInfo(language);
            var value = Resources.GetString(name, culture);
            return new LocalizedString(name, value is null ? name : string.Format(culture, value, arguments), value is null);
        }
    }
}
