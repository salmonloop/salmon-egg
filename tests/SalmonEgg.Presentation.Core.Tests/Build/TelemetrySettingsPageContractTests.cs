using System;
using System.Linq;
using System.Xml.Linq;

namespace SalmonEgg.Presentation.Core.Tests.Build;

public sealed class TelemetrySettingsPageContractTests
{
    [Fact]
    public void DataStoragePage_TelemetryAuthHeader_UsesPasswordBoxProjection()
    {
        var document = XDocument.Parse(TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml"));

        var authHeader = document
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "PasswordBox"
                && string.Equals(
                    (string?)element.Attribute("AutomationProperties.AutomationId"),
                    "DataStorage.TelemetryAuthHeader",
                    StringComparison.Ordinal));

        Assert.Equal(
            "TelemetryAuthHeaderBox",
            authHeader.Attributes().Single(attribute => attribute.Name.LocalName == "Name").Value);
        Assert.Equal("OnTelemetryAuthHeaderChanged", (string?)authHeader.Attribute("PasswordChanged"));
        Assert.Null(authHeader.Attribute("Text"));
        Assert.Null(authHeader.Attribute("Password"));

        var codeBehind = TestSourceFiles.ReadAllText(
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml.cs");
        Assert.Contains("TelemetryAuthHeaderBox.Password = ViewModel.Preferences.TelemetryAuthHeader ?? string.Empty;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("if (_isInitializingTelemetryAuthHeader)", codeBehind, StringComparison.Ordinal);
    }
}
