using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SalmonEgg.Presentation.Core.Tests.Build;

public sealed class CloudSyncProviderContractTests
{
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
        Assert.Contains("SelectedItem=\"{x:Bind ViewModel.CloudConfig.SelectedProviderOption, Mode=TwoWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedValuePath=\"ProviderId\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.CloudConfig.IsWebDavSelected", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewModel.CloudConfig.IsS3Selected", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DataStoragePage_CloudSyncSmokeExposesProviderSetupControls()
    {
        var xaml = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml");
        var document = XDocument.Parse(xaml);

        AssertElement(document, "DataStorage.CloudSync.ProviderPicker", "ComboBox",
            ("ItemsSource", "{x:Bind ViewModel.CloudConfig.Providers, Mode=OneWay}"),
            ("SelectedItem", "{x:Bind ViewModel.CloudConfig.SelectedProviderOption, Mode=TwoWay}"));
        AssertElement(document, "DataStorage.CloudSync.WebDavFileUrl", "TextBox",
            ("Text", "{x:Bind ViewModel.CloudConfig.WebDavFileUrl, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"));
        AssertElement(document, "DataStorage.CloudSync.WebDavUsername", "TextBox",
            ("Text", "{x:Bind ViewModel.CloudConfig.WebDavUsername, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"));
        AssertElement(document, "DataStorage.CloudSync.WebDavPassword", "PasswordBox",
            ("PasswordChanged", "OnWebDavPasswordChanged"));
        AssertElement(document, "DataStorage.CloudSync.S3Endpoint", "TextBox",
            ("Text", "{x:Bind ViewModel.CloudConfig.S3Endpoint, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"));
        AssertElement(document, "DataStorage.CloudSync.S3Bucket", "TextBox",
            ("Text", "{x:Bind ViewModel.CloudConfig.S3Bucket, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"));
        AssertElement(document, "DataStorage.CloudSync.S3ObjectKey", "TextBox",
            ("Text", "{x:Bind ViewModel.CloudConfig.S3ObjectKey, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"));
        AssertElement(document, "DataStorage.CloudSync.S3AccessKeyId", "TextBox",
            ("Text", "{x:Bind ViewModel.CloudConfig.S3AccessKeyId, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"));
        AssertElement(document, "DataStorage.CloudSync.S3SecretAccessKey", "PasswordBox",
            ("PasswordChanged", "OnS3SecretAccessKeyChanged"));

        AssertElement(document, "DataStorage.CloudSync.Apply", "Button",
            ("Command", "{x:Bind ViewModel.CloudConfig.ApplyCommand}"),
            ("IsEnabled", "{x:Bind ViewModel.CloudConfig.CanApply, Mode=OneWay}"));
        AssertElement(document, "DataStorage.CloudSync.SyncNow", "Button",
            ("Command", "{x:Bind ViewModel.CloudConfig.SyncNowCommand}"),
            ("IsEnabled", "{x:Bind ViewModel.CloudConfig.CanSync, Mode=OneWay}"));
        AssertElement(document, "DataStorage.CloudSync.Disable", "Button",
            ("Command", "{x:Bind ViewModel.CloudConfig.DisableCommand}"),
            ("IsEnabled", "{x:Bind ViewModel.CloudConfig.CanDisable, Mode=OneWay}"));
        AssertElement(document, "DataStorage.CloudSync.Forget", "Button",
            ("Command", "{x:Bind ViewModel.CloudConfig.ForgetCommand}"),
            ("IsEnabled", "{x:Bind ViewModel.CloudConfig.CanForget, Mode=OneWay}"));
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
            "{x:Bind ViewModel.CloudConfig.IsWebDavSelected, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}",
            GetAttributeByLocalName(webDavPanel, "Visibility"));
        Assert.Equal(
            "{x:Bind ViewModel.CloudConfig.IsS3Selected, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}",
            GetAttributeByLocalName(s3Panel, "Visibility"));
        Assert.DoesNotContain(s3Endpoint, webDavPanel.Descendants());
        Assert.DoesNotContain(webDavFileUrl, s3Panel.Descendants());
    }

    [Fact]
    public void DataStoragePage_WebDavCopyExplainsFolderUrlAndDefaultPackageName()
    {
        foreach (var resourcePath in new[]
        {
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw"
        })
        {
            var resources = XDocument.Parse(TestSourceFiles.ReadAllText(resourcePath));
            var header = GetResourceValue(resources, "DataStorage_CloudSyncWebDavFileUrl.Header", resourcePath);
            var description = GetResourceValue(resources, "DataStorage_CloudSyncWebDavFolderUrlDescription.Text", resourcePath);

            Assert.Contains("WebDAV", header, StringComparison.Ordinal);
            Assert.True(
                header.Contains("folder", StringComparison.OrdinalIgnoreCase) ||
                header.Contains("文件夹", StringComparison.Ordinal),
                $"WebDAV URL header must use folder semantics in '{resourcePath}'.");
            Assert.DoesNotContain("file URL", header, StringComparison.OrdinalIgnoreCase);

            Assert.Contains("salmonegg-config.zip", description, StringComparison.Ordinal);
            Assert.True(
                description.Contains("folder", StringComparison.OrdinalIgnoreCase) ||
                description.Contains("文件夹", StringComparison.Ordinal),
                $"WebDAV help text must tell users to enter a folder path in '{resourcePath}'.");
            Assert.True(
                description.Contains("only", StringComparison.OrdinalIgnoreCase) ||
                description.Contains("只填写", StringComparison.Ordinal),
                $"WebDAV help text must state that only the folder path is needed in '{resourcePath}'.");
        }
    }

    [Fact]
    public void DataStoragePage_CloudSyncActionsDescribeTheirEffects()
    {
        var expectations = new[]
        {
            new { Path = @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw", Apply = "保存并同步", Disable = "关闭云同步（保留设置）", Remove = "移除本机云同步设置" },
            new { Path = @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw", Apply = "Save and sync", Disable = "Turn off sync (keep settings)", Remove = "Remove cloud sync setup" },
            new { Path = @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw", Apply = "Save and sync", Disable = "Turn off sync (keep settings)", Remove = "Remove cloud sync setup" }
        };

        foreach (var expectation in expectations)
        {
            var resources = XDocument.Parse(TestSourceFiles.ReadAllText(expectation.Path));

            Assert.Equal(expectation.Apply, GetResourceValue(resources, "DataStorage_CloudSyncApplyAndVerify.Content", expectation.Path));
            Assert.Equal(expectation.Disable, GetResourceValue(resources, "DataStorage_CloudSyncDisable.Content", expectation.Path));
            Assert.Equal(expectation.Remove, GetResourceValue(resources, "DataStorage_CloudSyncForget.Content", expectation.Path));
        }
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
            "DataStorage_CloudSyncApplyAndVerify.Content",
            "DataStorage_CloudSyncDisable.Content",
            "DataStorage_CloudSyncForget.Content"
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

    [Fact]
    public void CloudConfigSettingsViewModel_LocalizedMessagesExistInEveryCoreResource()
    {
        var source = TestSourceFiles.ReadAllText(
            @"src\SalmonEgg.Presentation.Core\ViewModels\Settings\CloudConfigSettingsViewModel.cs");
        var referencedKeys = Regex.Matches(source, "_localizer\\[\"([^\"]+)\"\\]")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (var resourcePath in new[]
        {
            @"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.resx",
            @"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.zh-Hans.resx",
            @"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.en.resx",
            @"src\SalmonEgg.Presentation.Core\Resources\CoreStrings.en-US.resx"
        })
        {
            var resources = XDocument.Parse(TestSourceFiles.ReadAllText(resourcePath));
            foreach (var key in referencedKeys)
            {
                AssertResourceValue(resources, key, resourcePath);
            }
        }
    }

    [Fact]
    public void DataStoragePage_CloudSyncUidsDefineVisibleTextInEveryAppResource()
    {
        var page = XDocument.Parse(TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml"));
        var localizedElements = page.Descendants()
            .Select(element => new
            {
                Element = element,
                Uid = element.Attributes().FirstOrDefault(attribute =>
                    string.Equals(attribute.Name.LocalName, "Uid", StringComparison.Ordinal))?.Value
            })
            .Where(item => item.Uid?.StartsWith("DataStorage_CloudSync", StringComparison.Ordinal) == true)
            .Select(item => new
            {
                Uid = item.Uid!,
                Property = item.Element.Name.LocalName is "TextBlock" ? "Text" :
                    item.Element.Name.LocalName is "Button" ? "Content" : "Header"
            })
            .Append(new { Uid = "Common_Cancel", Property = "Content" })
            .ToArray();

        foreach (var resourcePath in new[]
        {
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw"
        })
        {
            var resources = XDocument.Parse(TestSourceFiles.ReadAllText(resourcePath));
            foreach (var item in localizedElements)
            {
                AssertResourceValue(resources, $"{item.Uid}.{item.Property}", resourcePath);
            }

            foreach (var auxiliaryKey in new[]
            {
                "DataStorage_CloudSyncWebDavPasswordReplacement.PlaceholderText",
                "DataStorage_CloudSyncS3SecretAccessKeyReplacement.PlaceholderText",
                "DataStorage_CloudSyncS3ForcePathStyle.OnContent",
                "DataStorage_CloudSyncS3ForcePathStyle.OffContent"
            })
            {
                AssertResourceValue(resources, auxiliaryKey, resourcePath);
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
    {
        var visibility = GetAttributeByLocalName(element, "Visibility");
        return string.Equals(element.Name.LocalName, "StackPanel", StringComparison.Ordinal)
               && (visibility?.Contains("ViewModel.CloudConfig.IsWebDavSelected", StringComparison.Ordinal) == true
                   || visibility?.Contains("ViewModel.CloudConfig.IsS3Selected", StringComparison.Ordinal) == true);
    }

    private static string? GetAttributeByLocalName(XElement element, string localName)
        => element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))
            ?.Value;

    private static void AssertResourceValue(XDocument document, string key, string resourcePath)
    {
        var value = GetResourceValue(document, key, resourcePath);

        Assert.False(string.IsNullOrWhiteSpace(value), $"Resource '{key}' must be defined in '{resourcePath}'.");
    }

    private static string GetResourceValue(XDocument document, string key, string resourcePath)
    {
        var value = document.Descendants("data")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("name"), key, StringComparison.Ordinal))
            ?.Element("value")
            ?.Value;

        Assert.False(string.IsNullOrWhiteSpace(value), $"Resource '{key}' must be defined in '{resourcePath}'.");
        return value!;
    }
}
