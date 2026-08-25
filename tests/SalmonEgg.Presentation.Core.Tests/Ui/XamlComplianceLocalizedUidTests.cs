using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Ui;

using static SalmonEgg.Presentation.Core.Tests.Ui.XamlComplianceTestHelpers;

/// <summary>
/// Guards the contract between an <c>x:Uid</c> in XAML and the <c>Uid.Property</c> keys that supply its
/// text from <c>Strings/&lt;language&gt;/Resources.resw</c>.
/// </summary>
/// <remarks>
/// The resource keys are data, so nothing on the build path validates them: the XAML compiler never sees
/// them and the resw is not parsed until a page is loaded. WinUI resolves them inside
/// <c>Application.LoadComponent</c>, and a property that the control does not own throws
/// <c>XamlParseException</c> there — which happens during <c>InitializeComponent</c>, so the page cannot be
/// constructed at all and <c>Frame.Navigate</c> rethrows. The ACP setup wizard shipped with
/// <c>AcpSetup_WhyNotFound.Content</c> and <c>AcpSetup_InstallOutputToggle.Content</c> pointing at
/// <c>TextBlock</c> elements (which own <c>Text</c>, not <c>Content</c>), so clicking the wizard button did
/// nothing at all: the shell's UnhandledException handler marks the crash handled once the first frame is
/// up, leaving no user-visible trace.
///
/// The gate is fail-closed. A (control type, localized property) pair is accepted only when this test knows
/// the property belongs to that control, so an unrecognized pair fails and asks the author to check the
/// control's real property surface instead of being waved through. That is deliberate: silently allowing
/// unknown pairs is exactly the hole that let this ship.
/// </remarks>
public sealed class XamlComplianceLocalizedUidTests
{
    /// <summary>Localizable properties per framework control, from each control's WinUI property surface.</summary>
    /// <remarks>
    /// Only string-valued properties a resw key may legitimately set are listed. Content-bearing controls are
    /// grouped by the base that actually declares the property (<c>ContentControl.Content</c>,
    /// <c>ToggleSwitch.OnContent/OffContent</c>, …) rather than by visual similarity, so a control that merely
    /// looks like a button does not inherit a button's entry.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string[]> FrameworkLocalizableProperties =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            // TextBlock/Run carry Text. They are not ContentControls, so Content does not exist on them.
            ["TextBlock"] = ["Text"],
            ["RichTextBlock"] = [],
            ["Run"] = ["Text"],
            ["FontIcon"] = [],
            ["PathIcon"] = [],
            ["SymbolIcon"] = [],
            ["ProgressRing"] = [],
            // Text inputs: Header/PlaceholderText/Description come from the shared input surface.
            ["TextBox"] = ["Text", "Header", "PlaceholderText", "Description"],
            ["RichEditBox"] = ["Header", "PlaceholderText", "Description"],
            ["PasswordBox"] = ["Header", "PlaceholderText", "Description"],
            ["AutoSuggestBox"] = ["Text", "Header", "PlaceholderText", "Description"],
            ["NumberBox"] = ["Header", "PlaceholderText", "Description"],
            ["ComboBox"] = ["Header", "PlaceholderText", "Description"],
            // ContentControl and friends.
            ["ContentControl"] = ["Content"],
            ["Button"] = ["Content"],
            ["HyperlinkButton"] = ["Content"],
            ["ToggleButton"] = ["Content"],
            ["RepeatButton"] = ["Content"],
            ["CheckBox"] = ["Content"],
            ["RadioButton"] = ["Content"],
            ["ComboBoxItem"] = ["Content"],
            ["ListViewItem"] = ["Content"],
            ["NavigationViewItem"] = ["Content"],
            ["AppBarButton"] = ["Content", "Label"],
            ["AppBarToggleButton"] = ["Content", "Label"],
            ["Expander"] = ["Content", "Header"],
            ["SettingsCard"] = ["Content", "Header", "Description"],
            ["PivotItem"] = ["Content", "Header"],
            ["TabViewItem"] = ["Content", "Header"],
            // Controls whose text surface is its own thing.
            ["ToggleSwitch"] = ["Header", "OnContent", "OffContent"],
            ["InfoBar"] = ["Title", "Message"],
            ["TeachingTip"] = ["Title", "Subtitle"],
            ["MenuFlyoutItem"] = ["Text"],
            ["ToggleMenuFlyoutItem"] = ["Text"],
            ["ContentDialog"] = ["Content", "Title", "PrimaryButtonText", "SecondaryButtonText", "CloseButtonText"],
        };

    /// <summary>Attached properties a resw key may set, independent of the control it is attached to.</summary>
    private static readonly string[] LocalizableAttachedProperties =
    [
        "AutomationProperties.Name",
        "AutomationProperties.HelpText",
        "AutomationProperties.FullDescription",
        "ToolTipService.ToolTip",
    ];

    [Fact]
    public void LocalizedUidProperties_AreOwnedByTheControlTheyAreAttachedTo()
    {
        var appRoot = Path.Combine(FindRepoRoot(), "SalmonEgg", "SalmonEgg");
        var targets = CollectUidTargets(appRoot);
        Assert.NotEmpty(targets);

        var resourceFiles = Directory
            .EnumerateFiles(Path.Combine(appRoot, "Strings"), "Resources.resw", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.NotEmpty(resourceFiles);

        var failures = new List<string>();
        foreach (var resourceFile in resourceFiles)
        {
            foreach (var key in ReadResourceKeys(resourceFile))
            {
                var separator = key.IndexOf('.', StringComparison.Ordinal);
                if (separator <= 0)
                {
                    // A bare name is a code-resolved string (CoreStringResolver and friends), not an x:Uid target.
                    continue;
                }

                var uid = key[..separator];
                var property = key[(separator + 1)..];
                if (!targets.TryGetValue(uid, out var elements))
                {
                    // Resource without a matching x:Uid: unused text, not a crash. Left to translators.
                    continue;
                }

                foreach (var element in elements)
                {
                    var rejection = Reject(element, property);
                    if (rejection is not null)
                    {
                        failures.Add(
                            $"{Path.GetRelativePath(FindRepoRoot(), resourceFile)} defines '{key}', but"
                            + $" {Path.GetRelativePath(FindRepoRoot(), element.File)}:{element.Line} attaches that"
                            + $" x:Uid to <{element.TypeName}>. {rejection}"
                            + " WinUI resolves x:Uid resources inside Application.LoadComponent, so an unowned"
                            + " property throws XamlParseException and the page cannot be constructed.");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine + Environment.NewLine, failures));
    }

    /// <summary>Explains why <paramref name="property"/> cannot be set on the element, or null when it can.</summary>
    private static string? Reject(UidTarget element, string property)
    {
        if (property.Contains('.', StringComparison.Ordinal))
        {
            // Attached properties are declared by their owner, not by the control, so they apply anywhere.
            // Both spellings WinUI accepts are allowed: the bare path and the [using:namespace]-qualified one.
            var path = StripUsingQualifier(property);
            return LocalizableAttachedProperties.Contains(path, StringComparer.Ordinal)
                ? null
                : $"'{path}' is not a recognized localizable attached property."
                  + $" Recognized: {string.Join(", ", LocalizableAttachedProperties)}."
                  + " Add it here only after confirming the attached property exists and takes a string.";
        }

        var allowed = ResolveLocalizableProperties(element);
        if (allowed is null)
        {
            return $"<{element.TypeName}> is not described by this gate."
                   + " Add its localizable string properties to FrameworkLocalizableProperties"
                   + " (or give the app control a DependencyProperty) after checking its real property surface.";
        }

        if (allowed.Contains(property, StringComparer.Ordinal))
        {
            return null;
        }

        return allowed.Count == 0
            ? $"<{element.TypeName}> has no localizable string property; move the text to a control that does."
            : $"<{element.TypeName}> localizes {string.Join(", ", allowed)} — not '{property}'.";
    }

    /// <summary>
    /// Properties the element may localize, or null when the control is unknown to this gate.
    /// </summary>
    /// <remarks>
    /// App-defined controls are resolved from their own <c>DependencyProperty</c> declarations rather than from
    /// the table: the control's source is the authoritative statement of what it owns, and a table entry would
    /// go stale the moment someone renames a property. Their framework base still contributes, so a control
    /// deriving from <c>ContentControl</c> keeps <c>Content</c>.
    /// </remarks>
    private static IReadOnlyCollection<string>? ResolveLocalizableProperties(UidTarget element)
    {
        if (!element.NamespaceName.StartsWith("using:SalmonEgg", StringComparison.Ordinal))
        {
            return FrameworkLocalizableProperties.TryGetValue(element.TypeName, out var framework)
                ? framework
                : null;
        }

        var source = FindAppControlSource(element.TypeName);
        if (source is null)
        {
            return null;
        }

        // A DependencyProperty alone is not enough: x:Uid resources are strings, so only the string-typed
        // ones can be set from resw. Requiring both the backing property and a string CLR property keeps a
        // key like Uid.Gesture (a KeyGesture DP) from being waved through.
        var backed = Regex
            .Matches(source, @"public static readonly DependencyProperty (?<name>\w+)Property\b")
            .Cast<Match>()
            .Select(match => match.Groups["name"].Value);
        var stringTyped = Regex
            .Matches(source, @"public string\??\s+(?<name>\w+)\b")
            .Cast<Match>()
            .Select(match => match.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
        var properties = backed.Where(stringTyped.Contains).ToHashSet(StringComparer.Ordinal);

        var baseType = Regex.Match(source, $@"class {Regex.Escape(element.TypeName)}\s*:\s*(?<base>[\w.]+)");
        if (baseType.Success
            && FrameworkLocalizableProperties.TryGetValue(
                baseType.Groups["base"].Value.Split('.')[^1],
                out var inherited))
        {
            properties.UnionWith(inherited);
        }

        return properties;
    }

    /// <summary>Every element carrying an <c>x:Uid</c>, keyed by that uid. One uid may be reused by several.</summary>
    private static IReadOnlyDictionary<string, List<UidTarget>> CollectUidTargets(string appRoot)
    {
        var targets = new Dictionary<string, List<UidTarget>>(StringComparer.Ordinal);
        foreach (var xamlFile in Directory.EnumerateFiles(appRoot, "*.xaml", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(xamlFile))
            {
                continue;
            }

            var document = XDocument.Load(xamlFile, LoadOptions.SetLineInfo);
            foreach (var element in document.Descendants())
            {
                var uid = element
                    .Attributes()
                    .FirstOrDefault(attribute =>
                        string.Equals(attribute.Name.LocalName, "Uid", StringComparison.Ordinal))
                    ?.Value;
                if (string.IsNullOrEmpty(uid))
                {
                    continue;
                }

                if (!targets.TryGetValue(uid, out var elements))
                {
                    elements = [];
                    targets[uid] = elements;
                }

                elements.Add(new UidTarget(
                    element.Name.LocalName,
                    element.Name.NamespaceName,
                    xamlFile,
                    (element as IXmlLineInfo).LineNumber));
            }
        }

        return targets;
    }

    private static IEnumerable<string> ReadResourceKeys(string resourceFile)
        => XDocument
            .Load(resourceFile)
            .Root!
            .Elements("data")
            .Select(data => data.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!);

    /// <summary>Reads the source of an app-defined control, or null when no single file declares it.</summary>
    private static string? FindAppControlSource(string typeName)
    {
        var declaration = new Regex($@"\bclass {Regex.Escape(typeName)}\b");
        var matches = Directory
            .EnumerateFiles(Path.Combine(FindRepoRoot(), "SalmonEgg", "SalmonEgg"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .Select(File.ReadAllText)
            .Where(text => declaration.IsMatch(text))
            .ToArray();

        return matches.Length == 0 ? null : string.Concat(matches);
    }

    /// <summary>Removes the optional <c>[using:namespace]</c> qualifier WinUI allows on attached-property keys.</summary>
    private static string StripUsingQualifier(string property)
        => Regex.Replace(property, @"^\[using:[^\]]+\]", string.Empty);

    private static bool IsBuildOutput(string path)
        => path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
           || path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    private sealed record UidTarget(string TypeName, string NamespaceName, string File, int Line);
}
