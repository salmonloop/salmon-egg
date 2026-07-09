using System;
using System.Linq;
using System.Xml.Linq;

namespace SalmonEgg.Presentation.Core.Tests.Build;

public sealed class CloudSyncProviderContractTests
{
    [Fact]
    public void S3Provider_RequiresStoredCredentialsBeforeAuthorizationSucceeds()
    {
        var provider = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Cloud\S3CloudConfigStorageProvider.cs");

        Assert.Contains("S3 access key ID is required.", provider, StringComparison.Ordinal);
        Assert.Contains("S3 secret access key is required.", provider, StringComparison.Ordinal);
        Assert.Contains("LoadConfigurationAsync", provider, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrWhiteSpace(accessKeyId)", provider, StringComparison.Ordinal);
        Assert.Contains("string.IsNullOrEmpty(secretAccessKey)", provider, StringComparison.Ordinal);
    }

    [Fact]
    public void S3Provider_DoesNotSavePartialCredentialsBeforeValidationCompletes()
    {
        var provider = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Cloud\S3CloudConfigStorageProvider.cs");

        var firstSave = provider.IndexOf("await _secureStorage.SaveAsync", StringComparison.Ordinal);
        var missingSecretCheck = provider.IndexOf("S3 secret access key is required.", StringComparison.Ordinal);

        Assert.True(firstSave > missingSecretCheck, "S3 provider must validate the complete credential pair before writing either secret.");
    }

    [Fact]
    public void S3Provider_UsesLocalSigV4WithoutAwsSdkDependency()
    {
        var provider = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Services\Cloud\S3CloudConfigStorageProvider.cs");
        var project = TestSourceFiles.ReadAllText(@"SalmonEgg\SalmonEgg\SalmonEgg.csproj");

        Assert.Contains("AWS4-HMAC-SHA256", provider, StringComparison.Ordinal);
        Assert.Contains("x-amz-content-sha256", provider, StringComparison.Ordinal);
        Assert.DoesNotContain("AmazonS3", provider, StringComparison.Ordinal);
        Assert.DoesNotContain("AWSSDK", project, StringComparison.Ordinal);
    }

    [Fact]
    public void DataStoragePage_UsesSingleProviderPickerForCloudSync()
    {
        var xaml = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml");

        Assert.Contains("DataStorage.CloudSync.ProviderPicker", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedValue=\"{x:Bind ViewModel.SelectedCloudConfigProviderId, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsWebDavCloudConfigProviderSelected", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.IsS3CloudConfigProviderSelected", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DataStoragePage_CloudSyncSmokeExposesProviderSetupControls()
    {
        var xaml = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml");
        var document = XDocument.Parse(xaml);

        AssertElement(document, "DataStorage.CloudSync.ProviderPicker", "ComboBox",
            ("ItemsSource", "{x:Bind ViewModel.CloudConfigProviders, Mode=OneWay}"),
            ("SelectedValue", "{x:Bind ViewModel.SelectedCloudConfigProviderId, Mode=TwoWay}"));
        AssertElement(document, "DataStorage.CloudSync.WebDavFileUrl", "TextBox",
            ("Text", "{x:Bind ViewModel.WebDavFileUrl, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"));
        AssertElement(document, "DataStorage.CloudSync.WebDavUsername", "TextBox",
            ("Text", "{x:Bind ViewModel.WebDavUsername, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"));
        AssertElement(document, "DataStorage.CloudSync.WebDavPassword", "PasswordBox",
            ("PasswordChanged", "OnWebDavPasswordChanged"));
        AssertElement(document, "DataStorage.CloudSync.S3Endpoint", "TextBox",
            ("Text", "{x:Bind ViewModel.S3Endpoint, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"));
        AssertElement(document, "DataStorage.CloudSync.S3Bucket", "TextBox",
            ("Text", "{x:Bind ViewModel.S3Bucket, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"));
        AssertElement(document, "DataStorage.CloudSync.S3ObjectKey", "TextBox",
            ("Text", "{x:Bind ViewModel.S3ObjectKey, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"));
        Assert.Contains("DataStorage_CloudSyncS3ObjectKeyDescription", xaml, StringComparison.Ordinal);
        AssertElement(document, "DataStorage.CloudSync.S3AccessKeyId", "TextBox",
            ("Text", "{x:Bind ViewModel.S3AccessKeyId, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"));
        AssertElement(document, "DataStorage.CloudSync.S3SecretAccessKey", "PasswordBox",
            ("PasswordChanged", "OnS3SecretAccessKeyChanged"));

        AssertElement(document, "DataStorage.CloudSync.ConnectSelected", "Button",
            ("Content", "{x:Bind ViewModel.ConnectCloudConfigProviderButtonText, Mode=OneWay}"),
            ("Command", "{x:Bind ViewModel.ConnectSelectedCloudConfigProviderCommand}"),
            ("IsEnabled", "{x:Bind ViewModel.IsCloudConfigSyncConfigured, Mode=OneWay}"));
        AssertElement(document, "DataStorage.CloudSync.SyncNow", "Button",
            ("Command", "{x:Bind ViewModel.SyncCloudConfigCommand}"),
            ("IsEnabled", "{x:Bind ViewModel.CanSyncCloudConfig, Mode=OneWay}"));
        AssertElement(document, "DataStorage.CloudSync.Disconnect", "Button",
            ("Command", "{x:Bind ViewModel.DisconnectCloudConfigCommand}"),
            ("IsEnabled", "{x:Bind ViewModel.CanDisconnectCloudConfig, Mode=OneWay}"));
    }

    [Fact]
    public void DataStoragePage_CloudSyncSmokeKeepsProviderPanelsMutuallyExclusive()
    {
        var document = XDocument.Parse(TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml"));
        var webDavFileUrl = FindElementByAutomationId(document, "DataStorage.CloudSync.WebDavFileUrl");
        var s3Endpoint = FindElementByAutomationId(document, "DataStorage.CloudSync.S3Endpoint");
        var webDavPanel = Assert.Single(webDavFileUrl.Ancestors(), IsProviderVisibilityPanel);
        var s3Panel = Assert.Single(s3Endpoint.Ancestors(), IsProviderVisibilityPanel);

        Assert.NotSame(webDavPanel, s3Panel);
        Assert.Equal(
            "{x:Bind ViewModel.IsWebDavCloudConfigProviderSelected, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}",
            GetAttributeByLocalName(webDavPanel, "Visibility"));
        Assert.Equal(
            "{x:Bind ViewModel.IsS3CloudConfigProviderSelected, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}",
            GetAttributeByLocalName(s3Panel, "Visibility"));
        Assert.DoesNotContain(s3Endpoint, webDavPanel.Descendants());
        Assert.DoesNotContain(webDavFileUrl, s3Panel.Descendants());
    }

    [Fact]
    public void DataStoragePage_CloudSyncSmokeHasLocalizedUxCopy()
    {
        var requiredCoreKeys = new[]
        {
            "DataStorage_CloudSyncCredentialsSaved",
            "DataStorage_CloudSyncCredentialsMissing",
            "DataStorage_CloudSyncWebDavFileUrlRequired",
            "DataStorage_CloudSyncWebDavFileUrlInvalid",
            "DataStorage_CloudSyncWebDavCredentialsRequired",
            "DataStorage_CloudSyncS3EndpointRequired",
            "DataStorage_CloudSyncS3EndpointInvalid",
            "DataStorage_CloudSyncS3BucketRequired",
            "DataStorage_CloudSyncS3CredentialsRequired",
            "DataStorage_CloudSyncConnectOneDrive",
            "DataStorage_CloudSyncConnectWebDav",
            "DataStorage_CloudSyncConnectS3",
            "DataStorage_CloudSyncConnectSelected",
            "DataStorage_CloudSyncSwitchConfirmTitle",
            "DataStorage_CloudSyncSwitchConfirmMessage",
            "DataStorage_CloudSyncSwitchConfirmPrimary",
            "DataStorage_CloudSyncSwitchConfirmCancel"
        };

        foreach (var resourcePath in new[]
        {
            @"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.resx",
            @"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.zh-Hans.resx",
            @"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.en.resx",
            @"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.en-US.resx"
        })
        {
            var resources = XDocument.Parse(TestSourceFiles.ReadAllText(resourcePath));
            foreach (var key in requiredCoreKeys)
            {
                AssertResourceValue(resources, key, resourcePath);
            }
        }

        var requiredXamlKeys = new[]
        {
            "DataStorage_CloudSyncTitle.Text",
            "DataStorage_CloudSyncProviderTitle.Text",
            "DataStorage_CloudSyncDescription.Text",
            "DataStorage_CloudSyncProviderSelectionDescription.Text",
            "DataStorage_CloudSyncProviderPicker.Header",
            "DataStorage_CloudSyncWebDavFileUrl.Header",
            "DataStorage_CloudSyncWebDavFileUrl.PlaceholderText",
            "DataStorage_CloudSyncWebDavFolderUrlDescription.Text",
            "DataStorage_CloudSyncWebDavUsername.Header",
            "DataStorage_CloudSyncWebDavPassword.Header",
            "DataStorage_CloudSyncS3Endpoint.Header",
            "DataStorage_CloudSyncS3Bucket.Header",
            "DataStorage_CloudSyncS3ObjectKeyDescription.Text",
            "DataStorage_CloudSyncS3AccessKeyId.Header",
            "DataStorage_CloudSyncS3SecretAccessKey.Header",
            "DataStorage_CloudSyncNow.Content",
            "DataStorage_CloudSyncDisconnect.Content"
        };

        foreach (var resourcePath in new[]
        {
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw"
        })
        {
            var resources = XDocument.Parse(TestSourceFiles.ReadAllText(resourcePath));
            foreach (var key in requiredXamlKeys)
            {
                AssertResourceValue(resources, key, resourcePath);
            }
        }
    }

    private static void AssertElement(
        XDocument document,
        string automationId,
        string elementName,
        params (string Attribute, string Value)[] expectedAttributes)
    {
        var element = FindElementByAutomationId(document, automationId);
        Assert.Equal(elementName, element.Name.LocalName);
        foreach (var (attribute, value) in expectedAttributes)
        {
            Assert.Equal(value, GetAttributeByLocalName(element, attribute));
        }
    }

    private static XElement FindElementByAutomationId(XDocument document, string automationId)
    {
        var element = document.Descendants().FirstOrDefault(candidate =>
            candidate.Attributes().Any(attribute =>
                IsAutomationIdAttribute(attribute)
                && string.Equals(attribute.Value, automationId, StringComparison.Ordinal)));

        Assert.NotNull(element);
        return element!;
    }

    private static bool IsAutomationIdAttribute(XAttribute attribute)
        => string.Equals(attribute.Name.LocalName, "AutomationId", StringComparison.Ordinal)
           || string.Equals(attribute.Name.LocalName, "AutomationProperties.AutomationId", StringComparison.Ordinal);

    private static bool IsProviderVisibilityPanel(XElement element)
        => string.Equals(element.Name.LocalName, "StackPanel", StringComparison.Ordinal)
           && (GetAttributeByLocalName(element, "Visibility")?.Contains("CloudConfigProviderSelected", StringComparison.Ordinal) == true);

    private static string? GetAttributeByLocalName(XElement element, string localName)
        => element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))
            ?.Value;

    private static void AssertResourceValue(XDocument document, string key, string resourcePath)
    {
        var value = document.Descendants("data")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("name"), key, StringComparison.Ordinal))
            ?.Element("value")
            ?.Value;

        Assert.False(string.IsNullOrWhiteSpace(value), $"Resource '{key}' must be defined in '{resourcePath}'.");
    }
}
