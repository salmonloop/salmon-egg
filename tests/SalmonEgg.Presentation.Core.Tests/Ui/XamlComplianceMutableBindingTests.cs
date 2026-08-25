using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

using static SalmonEgg.Presentation.Core.Tests.Ui.XamlComplianceTestHelpers;

namespace SalmonEgg.Presentation.Core.Tests.Ui;

/// <summary>
/// x:Bind defaults to OneTime, and a OneTime binding against a property that changes after the view
/// is realized freezes its first value for the control's lifetime. Every such defect in the ACP setup
/// wizard (probe verdicts never appearing on agent rows, validation messages never rendering) was a
/// missing <c>Mode</c> that no test caught, because the view models are tested directly and read-only
/// properties bypass XAML entirely.
///
/// The gate derives "mutable" from source: properties the generator raises change notifications for —
/// those on <c>[ObservableProperty]</c> fields plus everything listed in
/// <c>[NotifyPropertyChangedFor]</c> — and requires any x:Bind path rooted at one of them to state an
/// explicit mode. Adding a mutable property or a new binding cannot silently regress this page again.
/// </summary>
public sealed class XamlComplianceMutableBindingTests
{
    private const string WizardPagePath = "SalmonEgg/SalmonEgg/Presentation/Views/Settings/AcpSetupWizardPage.xaml";

    private static readonly string[] WizardViewModelSources =
    {
        "src/SalmonEgg.Presentation.Core/ViewModels/Settings/AcpSetup/AcpSetupWizardViewModel.cs",
        "src/SalmonEgg.Presentation.Core/ViewModels/Settings/AcpSetup/AcpSetupAgentRowViewModel.cs",
        "src/SalmonEgg.Presentation.Core/ViewModels/Settings/AcpSetup/AcpSetupParameterRowViewModel.cs",
    };

    private static readonly Regex AttributeBlockRegex = new(
        @"(?<attrs>(?:\s*\[[A-Za-z][^\]\n]*(?:\(\)?[^]\n]*)?\])+)\s*(?<decl>private\s+\S+\s+_[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Compiled);

    private static readonly Regex NotifyForRegex = new(
        @"\[NotifyPropertyChangedFor\(nameof\((?<name>[A-Za-z_][A-Za-z0-9_]*)\)\)\]",
        RegexOptions.Compiled);

    [Fact]
    public void MutablePropertyInventory_IsNotEmpty()
    {
        var mutables = CollectMutableProperties();
        Assert.True(mutables.Count >= 20, $"Only {mutables.Count} mutable properties were derived; the extraction regex has likely rotted and the gate is silently checking nothing.");
        Assert.Contains("IsChecking", mutables);
        Assert.Contains("ValidationMessage", mutables);
        Assert.Contains("IsTestFailed", mutables);
    }

    /// <summary>
    /// Every x:Bind whose first path segment is a property the generator notifies about must carry an
    /// explicit Mode. OneTime is permitted deliberately where a value genuinely never changes; what is
    /// forbidden is the implicit default deciding by accident.
    /// </summary>
    [Fact]
    public void WizardPage_XamlBindsToMutableProperties_AlwaysStateAnExplicitMode()
    {
        var mutables = CollectMutableProperties();
        var xaml = LoadXaml(WizardPagePath);
        var violations = new List<string>();

        foreach (Match match in Regex.Matches(
                     xaml,
                     @"x:Bind\s+(?<path>[A-Za-z_][A-Za-z0-9_.]*)\s*(?<rest>,[^}]*)?\}",
                     RegexOptions.Compiled))
        {
            var path = match.Groups["path"].Value;
            var rest = match.Groups["rest"].Success ? match.Groups["rest"].Value : string.Empty;
            var root = path.Split('.')[0];
            if (!mutables.Contains(root))
            {
                continue;
            }

            if (!Regex.IsMatch(rest, @"Mode\s*=\s*(OneWay|TwoWay|OneTime)", RegexOptions.IgnoreCase))
            {
                violations.Add($"{root} (x:Bind {path}{rest})");
            }
        }

        Assert.True(
            violations.Count == 0,
            "x:Bind paths rooted at notification-raising properties must state an explicit Mode " +
            "(x:Bind defaults to OneTime, which freezes the first value). Missing bindings:\n  " +
            string.Join("\n  ", violations));
    }

    /// <summary>
    /// Derives the set of property names whose change raises PropertyChanged: generated properties
    /// from [ObservableProperty] fields, plus every name in [NotifyPropertyChangedFor] attached to
    /// such a field. Derived from source rather than hardcoded so new properties join the gate free.
    /// </summary>
    private static HashSet<string> CollectMutableProperties()
    {
        var mutables = new HashSet<string>(StringComparer.Ordinal);

        foreach (var relativePath in WizardViewModelSources)
        {
            var root = FindRepoRoot();
            var source = File.ReadAllText(Path.Combine(root, NormalizeRelativePath(relativePath)));

            // Each attribute block attached to a private _camelCase field is one [ObservableProperty]
            // declaration; every NotifyPropertyChangedFor inside it names a derived property that also
            // mutates. Attribute blocks are matched as a whole so the notify list stays with its own
            // field rather than bleeding into the next one.
            foreach (Match match in AttributeBlockRegex.Matches(source))
            {
                var attrs = match.Groups["attrs"].Value;
                if (!attrs.Contains("[ObservableProperty]", StringComparison.Ordinal))
                {
                    continue;
                }

                var fieldName = Regex.Match(match.Groups["decl"].Value, @"_(?<f>[A-Za-z_][A-Za-z0-9_]*)").Groups["f"].Value;
                if (fieldName.Length == 0)
                {
                    continue;
                }

                mutables.Add(ToPropertyName(fieldName));
                foreach (Match notify in NotifyForRegex.Matches(attrs))
                {
                    mutables.Add(notify.Groups["name"].Value);
                }
            }
        }

        return mutables;
    }

    /// <summary>The generator names the property by Pascal-casing the underscore-stripped field.</summary>
    private static string ToPropertyName(string fieldName)
        => char.ToUpperInvariant(fieldName[0]) + fieldName[1..];
}
