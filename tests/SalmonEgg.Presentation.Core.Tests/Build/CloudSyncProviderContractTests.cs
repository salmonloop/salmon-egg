using System;

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
}
