using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Ui;

using static SalmonEgg.Presentation.Core.Tests.Ui.XamlComplianceTestHelpers;

/// <summary>
/// Fail-closed gate for native list-row chrome: rounded corners, the left selection indicator and
/// the hover / pressed / selected fills.
/// </summary>
/// <remarks>
/// A WinUI ItemContainerStyle REPLACES the framework's implicit ListViewItem style rather than
/// merging with it, and that implicit style declares no setters of its own - its whole content is
/// BasedOn="{StaticResource DefaultListViewItemStyle}". A container style that does not re-chain
/// therefore loses the Template and the ListViewItemPresenter that draws all of the above, while
/// compiling clean and leaving every other test green.
///
/// Uno inverts both halves: its ApplyStyles supplies a Template from the default style at the lower
/// ImplicitStyle precedence even when the explicit style omits one, and its keyed
/// DefaultListViewItemStyle wraps a ListViewItemPresenter it never implemented
/// (unoplatform/uno#1444) in a template with zero VisualStateGroups - so chaining there costs the
/// state fills instead of restoring them.
///
/// Hence one seam, AppListRowBaseStyle, whose base is stated once with Uno's win: conditional-XAML
/// prefix. This gate never asks a list to acquire a container style - a list with none inherits the
/// implicit style untouched and is already correct - it only requires that any style which exists
/// routes through the seam.
/// </remarks>
public sealed class XamlComplianceListRowChromeTests
{
    private const string SeamKey = "AppListRowBaseStyle";
    private const string SeamReference = "{StaticResource AppListRowBaseStyle}";
    private const string FrameworkBaseReference = "{StaticResource DefaultListViewItemStyle}";

    /// <summary>The namespace win: must map to, which is what makes win:BasedOn mean BasedOn on Windows.</summary>
    private const string WindowsPresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private const string AppXaml = @"SalmonEgg\SalmonEgg\App.xaml";
    private const string WizardXaml = @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpSetupWizardPage.xaml";

    /// <summary>
    /// The file that owns the seam. It is the one place a ListViewItem style may carry the framework
    /// base rather than the seam reference.
    /// </summary>
    private static readonly HashSet<string> SeamDefinitionOwners = new(StringComparer.Ordinal)
    {
        "App.xaml",
    };

    /// <summary>
    /// Files allowed to hold a seam-less ListViewItem style, and the shape that earns the exemption.
    /// </summary>
    /// <remarks>
    /// Chat transcript rows are message surfaces rather than selectable list items: they zero Padding
    /// and MinHeight so each bubble owns its bounds, and inheriting the row template would paint hover
    /// fills behind chrome the bubble already draws.
    ///
    /// Listing the file is not enough on its own. A file name would let any future style in the same
    /// file inherit the exemption silently - add a real row picker to ChatView.xaml and it would ship
    /// without chrome and without a failing test. So each exempt style must also prove the transcript
    /// shape via <see cref="EarnsTranscriptExemption"/>.
    /// </remarks>
    private static readonly HashSet<string> TranscriptExemptOwners = new(StringComparer.Ordinal)
    {
        "ChatView.xaml",
        "MiniChatView.xaml",
    };

    /// <summary>
    /// Confirms a seam-less style really is a transcript row rather than a list row that forgot the seam.
    /// </summary>
    /// <remarks>
    /// Two independent signals, both required. The zeroed Padding and MinHeight say the row delegates
    /// its bounds to the bubble inside it; SelectionMode="None" on the list that owns the style says
    /// there is no selection for row chrome to represent. A style that zeroes its insets inside a
    /// selectable list is a picker that lost its chrome, not a transcript.
    /// </remarks>
    private static bool EarnsTranscriptExemption(XElement style)
    {
        if (!string.Equals(GetSetterValue(style, "Padding"), "0", StringComparison.Ordinal)
            || !string.Equals(GetSetterValue(style, "MinHeight"), "0", StringComparison.Ordinal))
        {
            return false;
        }

        var owningList = style
            .Ancestors()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "ListView", StringComparison.Ordinal));

        return string.Equals(GetAttributeValue(owningList, "SelectionMode"), "None", StringComparison.Ordinal);
    }

    [Fact]
    public void ListViewItemContainerStyles_ChainToTheAppSeamSoTheRowTemplateSurvives()
    {
        var offenders = new List<string>();
        var inspected = 0;

        foreach (var (path, style) in EnumerateListViewItemStyles())
        {
            var fileName = Path.GetFileName(path);
            if (SeamDefinitionOwners.Contains(fileName))
            {
                continue;
            }

            if (TranscriptExemptOwners.Contains(fileName) && EarnsTranscriptExemption(style))
            {
                continue;
            }

            inspected++;
            var basedOn = GetAttributeValue(style, "BasedOn");
            if (!string.Equals(basedOn, SeamReference, StringComparison.Ordinal))
            {
                offenders.Add(
                    $"{fileName}: style '{DescribeStyle(style)}' has BasedOn='{basedOn ?? "(none)"}', "
                    + $"expected '{SeamReference}'. Chaining to '{FrameworkBaseReference}' directly is "
                    + "wrong too: it is correct on WinUI but shadows Uno's working row template.");
            }
        }

        // Guard against a vacuous pass if the walk stops finding styles at all.
        Assert.True(inspected > 0, "Expected to inspect at least one seam-bound ListViewItem style.");
        Assert.Empty(offenders);
    }

    [Fact]
    public void EveryListWhoseRowsMustDrawChrome_HasASeamBoundContainerStyle()
    {
        // Named rather than counted: a count drifts when a list is legitimately added or removed,
        // while these specific lists are the ones whose rows a user picks from and must therefore
        // show hover, focus and selection. Naming them also states which file to look in.
        var required = new (string Xaml, string Style)[]
        {
            (@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml", "DiscoverSessionItemStyleCompact"),
            (@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml", "DiscoverSessionItemStyleComfortable"),
            (@"SalmonEgg\SalmonEgg\Presentation\Views\Discover\DiscoverSessionsPage.xaml", "DiscoverProfileItemStyle"),
            (@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml", "AgentListItemStyleCompact"),
            (@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml", "AgentListItemStyleComfortable"),
        };

        foreach (var (xaml, styleKey) in required)
        {
            var style = FindKeyedListViewItemStyle(xaml, styleKey);
            Assert.Equal(SeamReference, GetAttributeValue(style, "BasedOn"));
        }

        // The two dialogs declare their container style inline, so they are matched by owning list.
        foreach (var xaml in new[]
        {
            @"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\SessionsListDialog.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Navigation\RemoteProjectSelectionDialog.xaml",
        })
        {
            var inline = XDocument.Parse(LoadXaml(xaml))
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal)
                    && string.Equals(element.Attribute("TargetType")?.Value, "ListViewItem", StringComparison.Ordinal))
                .ToArray();

            Assert.NotEmpty(inline);
            Assert.All(inline, style => Assert.Equal(SeamReference, GetAttributeValue(style, "BasedOn")));
        }
    }

    [Fact]
    public void TheSeamStatesItsWindowsOnlyBaseThroughTheConditionalXamlPrefix()
    {
        var document = XDocument.Parse(LoadXaml(AppXaml));
        var seam = FindKeyedListViewItemStyle(AppXaml, SeamKey);

        // The base must be Windows-scoped. An unprefixed BasedOn is the regression this catches: it
        // is correct on WinUI and shadows Uno's working row template everywhere else.
        var basedOn = seam
            .Attributes()
            .SingleOrDefault(attribute => string.Equals(attribute.Name.LocalName, "BasedOn", StringComparison.Ordinal));

        Assert.NotNull(basedOn);
        Assert.Equal(FrameworkBaseReference, basedOn!.Value);
        Assert.Equal(
            WindowsPresentationNamespace,
            basedOn.Name.NamespaceName);

        // And the prefix has to be the literal "win", since Uno's generator matches on the prefix
        // name (ExcludeXamlNamespaces Include="win"), not on the namespace URI.
        var winPrefix = document.Root!
            .Attributes()
            .SingleOrDefault(attribute =>
                string.Equals(attribute.Name.NamespaceName, XNamespace.Xmlns.NamespaceName, StringComparison.Ordinal)
                && string.Equals(attribute.Value, WindowsPresentationNamespace, StringComparison.Ordinal)
                && string.Equals(attribute.Name.LocalName, "win", StringComparison.Ordinal));

        Assert.NotNull(winPrefix);
    }

    [Fact]
    public void TheSeamOwnsTheCornerRadiusBecauseUnoReadsItFromTheContainer()
    {
        // Uno's row template template-binds CornerRadius from the container and Uno ships no
        // ListViewItem radius resource, so without this setter rows render square there. WinUI
        // ignores it - its presenter reads ThemeResource ListViewItemCornerRadius - so stating it
        // once on the seam costs nothing on Windows and spares every call site a duplicate.
        var seam = FindKeyedListViewItemStyle(AppXaml, SeamKey);
        Assert.Equal("{ThemeResource ControlCornerRadius}", GetSetterValue(seam, "CornerRadius"));

        // ControlCornerRadius rather than the more specific-looking ListViewItemCornerRadius, which
        // WinUI defines but Uno does not - it would silently resolve to nothing on Skia and WASM.
        var offenders = EnumerateListViewItemStyles()
            .Where(entry => GetSetterValue(entry.Style, "CornerRadius") is { } value
                && value.Contains("ListViewItemCornerRadius", StringComparison.Ordinal))
            .Select(entry => Path.GetFileName(entry.Path))
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public void TheAcpWizardAgentListStaysOnTheFrameworkDefaultAsTheReferenceRendering()
    {
        // This list is the control: it declares no container style at all, so it inherits the
        // framework implicit style wholesale, which is the most native outcome available. Giving it
        // one - even a seam-bound one - would replace the framework Padding and change a rendering
        // nothing else depends on.
        var document = XDocument.Parse(LoadXaml(WizardXaml));
        var list = FindListViewByName(document, "AcpSetupAgentsList");

        Assert.Null(GetAttributeValue(list, "ItemContainerStyle"));
        Assert.DoesNotContain(
            list.Elements(),
            element => element.Name.LocalName.EndsWith("ItemContainerStyle", StringComparison.Ordinal));
    }

    private static XElement FindKeyedListViewItemStyle(string relativePath, string key)
    {
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var style = XDocument.Parse(LoadXaml(relativePath))
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal)
                && string.Equals(element.Attribute("TargetType")?.Value, "ListViewItem", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Key")?.Value, key, StringComparison.Ordinal));

        Assert.NotNull(style);
        return style!;
    }

    private static XElement FindListViewByName(XDocument document, string name)
    {
        var list = document
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "ListView", StringComparison.Ordinal)
                && string.Equals(GetAttributeValue(element, "Name"), name, StringComparison.Ordinal));

        Assert.NotNull(list);
        return list!;
    }

    private static IEnumerable<(string Path, XElement Style)> EnumerateListViewItemStyles()
    {
        foreach (var path in EnumerateProductXamlFiles())
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(File.ReadAllText(path));
            }
            catch (System.Xml.XmlException)
            {
                continue;
            }

            foreach (var style in document.Descendants().Where(element =>
                string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal)
                && string.Equals(element.Attribute("TargetType")?.Value, "ListViewItem", StringComparison.Ordinal)))
            {
                yield return (path, style);
            }
        }
    }

    private static string[] EnumerateProductXamlFiles()
    {
        var root = Path.Combine(FindRepoRoot(), "SalmonEgg", "SalmonEgg");
        return Directory
            .EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }

    private static string? GetSetterValue(XElement style, string property)
        => style.Elements()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal)
                && string.Equals(element.Attribute("Property")?.Value, property, StringComparison.Ordinal))
            ?.Attribute("Value")
            ?.Value;

    private static string DescribeStyle(XElement style)
    {
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        return style.Attribute(xNamespace + "Key")?.Value ?? "(inline)";
    }

    private static string? GetAttributeValue(XElement? element, string localName)
        => element?.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, localName, StringComparison.Ordinal))
            ?.Value;
}
