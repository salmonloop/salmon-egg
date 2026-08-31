using System;
using System.Linq;
using System.Xml.Linq;

using static SalmonEgg.Presentation.Core.Tests.Ui.XamlComplianceTestHelpers;

namespace SalmonEgg.Presentation.Core.Tests.Settings.AcpSetup;

public sealed class AcpSetupWizardXamlTests
{
    private const string WizardPath = "SalmonEgg/SalmonEgg/Presentation/Views/Settings/AcpSetupWizardPage.xaml";

    [Fact]
    public void Navigation_ExposesSecondarySkipAndAnnouncesDynamicStepPosition()
    {
        var document = XDocument.Parse(LoadXaml(WizardPath));

        var skip = FindByName(document, "AcpSetupSkipTestButton");
        Assert.Equal("AcpSetup_SkipTest", AttributeByLocalName(skip, "Uid"));
        Assert.Equal("{x:Bind ViewModel.SkipTestCommand}", skip.Attribute("Command")?.Value);
        Assert.Equal(
            "{x:Bind ViewModel.IsSkipTestVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}",
            skip.Attribute("Visibility")?.Value);
        Assert.Equal("AcpSetup.SkipTest", skip.Attribute("AutomationProperties.AutomationId")?.Value);
        Assert.Null(skip.Attribute("Style"));

        var next = FindByName(document, "AcpSetupNextStepButton");
        Assert.Equal("{StaticResource AccentButtonStyle}", next.Attribute("Style")?.Value);
        Assert.Equal("AcpSetup.Next", next.Attribute("AutomationProperties.AutomationId")?.Value);
        Assert.Equal(
            "AcpSetup.Back",
            FindByName(document, "AcpSetupBackStepButton").Attribute("AutomationProperties.AutomationId")?.Value);

        var position = FindByName(document, "AcpSetupStepPosition");
        Assert.Equal("{x:Bind ViewModel.StepPositionText, Mode=OneWay}", position.Attribute("Text")?.Value);
        Assert.Equal("Polite", position.Attribute("AutomationProperties.LiveSetting")?.Value);
        Assert.Equal("AcpSetup.StepPosition", position.Attribute("AutomationProperties.AutomationId")?.Value);
    }

    [Fact]
    public void LocalizedStepTitles_DoNotClaimFixedOrdinalsWhenStepsCanBeSkipped()
    {
        string[] resourceFiles =
        [
            "SalmonEgg/SalmonEgg/Strings/zh-Hans/Resources.resw",
            "SalmonEgg/SalmonEgg/Strings/en/Resources.resw",
            "SalmonEgg/SalmonEgg/Strings/en-US/Resources.resw"
        ];
        string[] titleKeys =
        [
            "AcpSetup_AgentsTitle.Text",
            "AcpSetup_ComponentsTitle.Text",
            "AcpSetup_ParametersTitle.Text",
            "AcpSetup_TestTitle.Text",
            "AcpSetup_SaveTitle.Text"
        ];
        string[] skipKeys =
        [
            "AcpSetup_SkipTest.Content",
            "AcpSetup_SkipTest.AutomationProperties.HelpText"
        ];

        foreach (var resourceFile in resourceFiles)
        {
            var resources = XDocument.Parse(LoadText(resourceFile));
            var values = resources.Descendants("data").ToDictionary(
                data => (string)data.Attribute("name")!,
                data => data.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

            foreach (var key in titleKeys)
            {
                Assert.True(values.TryGetValue(key, out var value), $"{resourceFile} must define {key}.");
                Assert.DoesNotMatch(@"^\d+\.", value);
            }

            foreach (var key in skipKeys)
            {
                Assert.True(values.TryGetValue(key, out var value), $"{resourceFile} must define {key}.");
                Assert.False(string.IsNullOrWhiteSpace(value), $"{resourceFile} must localize {key}.");
            }
        }
    }

    private static XElement FindByName(XDocument document, string name)
        => Assert.Single(document.Descendants(), element =>
            string.Equals(AttributeByLocalName(element, "Name"), name, StringComparison.Ordinal));

    private static string? AttributeByLocalName(XElement element, string localName)
        => element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))
            ?.Value;
}
