using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Presentation.Core.Tests.Ui;

using static SalmonEgg.Presentation.Core.Tests.Ui.XamlComplianceTestHelpers;

public sealed class XamlComplianceThemeResourcesTests
{

    [Fact]
    public void App_MergesSharedTitleBarIconResources()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml");

        Assert.Contains("Styles/TitleBarIcons.xaml", xaml);
    }

    [Fact]
    public void AppResources_DefineNativeSettingsPageLayoutStyles()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml");

        Assert.Contains("x:Key=\"SettingsPageTitleTextStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsPageSummaryTextStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsSectionTitleTextStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsRowTitleTextStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsRowDescriptionTextStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsSectionContainerStyle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SettingsRowGridStyle\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"SettingsRowControlTemplate\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppResources_DoNotReplaceNativeButtonTemplates()
    {
        var appXaml = LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml");
        var agentProfileEditor = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AgentProfileEditorPage.xaml");

        Assert.DoesNotContain("SubtleButtonStyle", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<ControlTemplate TargetType=\"Button\"", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Style=\"{StaticResource SubtleButtonStyle}\"", agentProfileEditor, StringComparison.Ordinal);
    }

    [Fact]
    public void AppThemeDictionaries_UseExplicitThemesAndStaticLightDarkResources()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml");
        var lightDictionary = ExtractSection(
            xaml,
            "<ResourceDictionary x:Key=\"Light\">",
            "</ResourceDictionary>");
        var darkDictionary = ExtractSection(
            xaml,
            "<ResourceDictionary x:Key=\"Dark\">",
            "</ResourceDictionary>");
        var highContrastDictionary = ExtractSection(
            xaml,
            "<ResourceDictionary x:Key=\"HighContrast\">",
            "</ResourceDictionary>");

        Assert.Contains("x:Key=\"Light\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Dark\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HighContrast\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("{ThemeResource", lightDictionary, StringComparison.Ordinal);
        Assert.DoesNotContain("{ThemeResource", darkDictionary, StringComparison.Ordinal);
        Assert.Contains("SystemColorWindowColorBrush", highContrastDictionary, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolCallPillThemeDictionaries_UseExplicitNativeThemes()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ToolCallPill.xaml");

        Assert.Contains("x:Key=\"Light\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"Dark\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"HighContrast\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Key=\"Default\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void UiResources_HaveSameKeysForCanonicalLanguages()
    {
        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw"
        ];

        var resourceKeysByFile = resourceFiles.ToDictionary(
            path => path,
            path => XDocument.Parse(LoadText(path))
                .Descendants("data")
                .Select(data => (string?)data.Attribute("name"))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            StringComparer.Ordinal);
        var allKeys = resourceKeysByFile.Values
            .SelectMany(static keys => keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var failures = new List<string>();

        foreach (var (resourceFile, keys) in resourceKeysByFile)
        {
            var missing = allKeys.Except(keys, StringComparer.Ordinal).ToArray();
            if (missing.Length > 0)
            {
                failures.Add($"{resourceFile} missing: {string.Join(", ", missing)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Xaml_UserVisibleLiteralAttributesAreLocalizedWithUid()
    {
        var root = FindRepoRoot();
        var xamlFiles = Directory
            .EnumerateFiles(Path.Combine(root, "SalmonEgg", "SalmonEgg"), "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var failures = new List<string>();

        foreach (var xamlFile in xamlFiles)
        {
            var document = XDocument.Parse(File.ReadAllText(xamlFile));
            foreach (var element in document.Descendants())
            {
                foreach (var attribute in element.Attributes().Where(IsUserVisibleTextAttribute))
                {
                    if (!IsHardcodedUserVisibleLiteral(attribute.Value)
                        || HasAttributeByLocalName(element, "Uid")
                        || IsVisibleLiteralWhitelist(xamlFile, element, attribute))
                    {
                        continue;
                    }

                    failures.Add($"{Path.GetRelativePath(root, xamlFile)} <{element.Name.LocalName}> {attribute.Name.LocalName}=\"{attribute.Value}\"");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Xaml_UserVisibleAttributesDoNotKeepChineseFallbackText()
    {
        var root = FindRepoRoot();
        var viewsRoot = Path.Combine(root, "SalmonEgg", "SalmonEgg", "Presentation", "Views");
        var xamlFiles = Directory.EnumerateFiles(viewsRoot, "*.xaml", SearchOption.AllDirectories).ToArray();
        var failures = new List<string>();

        foreach (var xamlFile in xamlFiles)
        {
            var document = XDocument.Parse(File.ReadAllText(xamlFile));
            foreach (var element in document.Descendants())
            {
                foreach (var attribute in element.Attributes().Where(IsChineseUserVisibleTextAttribute))
                {
                    if (attribute.Value.Any(IsCjkUnifiedIdeograph))
                    {
                        failures.Add($"{Path.GetRelativePath(root, xamlFile)} <{element.Name.LocalName}> {attribute.Name.LocalName}=\"{attribute.Value}\"");
                    }
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void UiCode_DynamicResourceKeysExistInAllCanonicalResources()
    {
        string[] sourceFiles =
        [
            @"SalmonEgg\SalmonEgg\MainPage.xaml.cs",
            @"SalmonEgg\SalmonEgg\Controls\ToolCallPill.xaml.cs",
            @"SalmonEgg\SalmonEgg\Presentation\Converters\TaskOverviewLocalizationConverters.cs"
        ];
        string[] resourceFiles =
        [
            @"SalmonEgg\SalmonEgg\Strings\en\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\en-US\Resources.resw",
            @"SalmonEgg\SalmonEgg\Strings\zh-Hans\Resources.resw"
        ];
        var keys = sourceFiles
            .SelectMany(path => Regex.Matches(LoadText(path), @"(?:ResolveResourceString|TaskOverviewResourceLabels\.Get)\(\s*""(?<key>[^""]+)"""))
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var failures = new List<string>();

        foreach (var resourceFile in resourceFiles)
        {
            var resourceKeys = XDocument.Parse(LoadText(resourceFile))
                .Descendants("data")
                .Select(data => (string?)data.Attribute("name"))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var key in keys)
            {
                if (!resourceKeys.Contains(key))
                {
                    failures.Add($"{resourceFile} missing dynamic key {key}");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void TitleBarButtons_UseSharedIconTemplates()
    {
        var mainPageXaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");
        var miniChatXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");
        var titleBarIconsDocument = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Styles\TitleBarIcons.xaml"));
        var titleBarButtonStylesDocument = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Styles\TitleBarCommandButtonStyle.xaml"));
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");

        Assert.Contains("ContentTemplate=\"{StaticResource TitleBarBackIconTemplate}\"", mainPageXaml);
        Assert.Contains("ContentTemplate=\"{StaticResource TitleBarToggleLeftNavIconTemplate}\"", mainPageXaml);
        Assert.Contains("ContentTemplate=\"{StaticResource TitleBarOpenMiniWindowIconTemplate}\"", mainPageXaml);
        Assert.DoesNotContain("Glyph=\"&#xE72B;\"", mainPageXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Glyph=\"&#xE700;\"", mainPageXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Glyph=\"&#xEE49;\"", mainPageXaml, StringComparison.Ordinal);
        Assert.Contains("ContentTemplate=\"{StaticResource TitleBarReturnToMainWindowIconTemplate}\"", miniChatXaml);
        Assert.DoesNotContain("Glyph=\"&#xE73F;\"", miniChatXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource MiniTitleBarAccessoryButtonStyle}\"", miniChatXaml);

        var returnIconTemplate = titleBarIconsDocument
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "DataTemplate", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Key")?.Value, "TitleBarReturnToMainWindowIconTemplate", StringComparison.Ordinal));

        Assert.NotNull(returnIconTemplate);

        var returnIconPath = returnIconTemplate!
            .Descendants()
            .FirstOrDefault(element => string.Equals(element.Name.LocalName, "Path", StringComparison.Ordinal));

        Assert.NotNull(returnIconPath);
        Assert.Equal("16", returnIconPath!.Attribute("Width")?.Value);
        Assert.Equal("16", returnIconPath.Attribute("Height")?.Value);

        var miniAccessoryStyle = titleBarButtonStylesDocument
            .Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Key")?.Value, "MiniTitleBarAccessoryButtonStyle", StringComparison.Ordinal));

        Assert.NotNull(miniAccessoryStyle);
        Assert.Contains(miniAccessoryStyle!.Descendants().Where(element => string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal)),
            setter => string.Equals(setter.Attribute("Property")?.Value, "Width", StringComparison.Ordinal)
                && string.Equals(setter.Attribute("Value")?.Value, "40", StringComparison.Ordinal));
        Assert.Contains(miniAccessoryStyle.Descendants().Where(element => string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal)),
            setter => string.Equals(setter.Attribute("Property")?.Value, "Height", StringComparison.Ordinal)
                && string.Equals(setter.Attribute("Value")?.Value, "40", StringComparison.Ordinal));
        Assert.DoesNotContain(miniAccessoryStyle.Descendants(), element => string.Equals(element.Name.LocalName, "Viewbox", StringComparison.Ordinal));
    }

    [Fact]
    public void TitleBarButtonStyles_DoNotReplaceNativeControlTemplates()
    {
        string[] styleFiles =
        [
            @"SalmonEgg\SalmonEgg\Styles\TitleBarCommandButtonStyle.xaml",
            @"SalmonEgg\SalmonEgg\Styles\TitleBarToggleButtonStyle.xaml"
        ];

        foreach (var styleFile in styleFiles)
        {
            var xaml = LoadXaml(styleFile);

            Assert.DoesNotContain("<ControlTemplate", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("VisualStateGroup", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TitleBarButtonStyles_KeepNativeRoundedBorderlessAppearance()
    {
        var commandStyles = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Styles\TitleBarCommandButtonStyle.xaml"));
        var toggleStyles = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Styles\TitleBarToggleButtonStyle.xaml"));

        AssertTitleBarStyleKeepsNativeRoundedBorderlessAppearance(commandStyles, "TitleBarCommandButtonStyle", "8");
        AssertTitleBarStyleKeepsNativeRoundedBorderlessAppearance(commandStyles, "MiniTitleBarAccessoryButtonStyle", "4");
        AssertTitleBarStyleKeepsNativeRoundedBorderlessAppearance(toggleStyles, "TitleBarToggleButtonStyle", "8");
    }

    [Fact]
    public void TitleBarRightButtons_ScopeNativeCheckedStateResources()
    {
        var mainPageXaml = LoadXaml(@"SalmonEgg\SalmonEgg\MainPage.xaml");
        var toggleStyleXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Styles\TitleBarToggleButtonStyle.xaml");
        var toggleStyles = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\Styles\TitleBarToggleButtonStyle.xaml"));
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var style = toggleStyles.Descendants()
            .FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Key")?.Value, "TitleBarToggleButtonStyle", StringComparison.Ordinal));

        Assert.NotNull(style);
        Assert.Equal("{StaticResource DefaultToggleButtonStyle}", style!.Attribute("BasedOn")?.Value);
        Assert.Contains(style.Descendants().Where(element => string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal)),
            setter => string.Equals(setter.Attribute("Property")?.Value, "Background", StringComparison.Ordinal)
                && string.Equals(setter.Attribute("Value")?.Value, "Transparent", StringComparison.Ordinal));
        Assert.DoesNotContain("ToggleButtonBackgroundChecked", toggleStyleXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"TitleBarRightButtons\"", mainPageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ToggleButtonBackgroundChecked\"", mainPageXaml, StringComparison.Ordinal);
        Assert.Contains("ResourceKey=\"ButtonBackground\"", mainPageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ToggleButtonBackgroundCheckedPointerOver\"", mainPageXaml, StringComparison.Ordinal);
        Assert.Contains("ResourceKey=\"ButtonBackgroundPointerOver\"", mainPageXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ToggleButtonBackgroundCheckedPressed\"", mainPageXaml, StringComparison.Ordinal);
        Assert.Contains("ResourceKey=\"ButtonBackgroundPressed\"", mainPageXaml, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"SalmonEgg\SalmonEgg\Presentation\Views\Chat\ChatView.xaml")]
    [InlineData(@"SalmonEgg\SalmonEgg\Presentation\Views\Start\StartView.xaml")]
    public void OverlayScrim_UsesThemeBrush(string relativePath)
    {
        var xaml = LoadXaml(relativePath);

        Assert.DoesNotContain("Background=\"#40000000\"", xaml);
    }

    [Fact]
    public void AppTheme_DoesNotUseHardcodedTintColors()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml");

        Assert.DoesNotContain("TintColor=\"#", xaml);
        Assert.DoesNotContain("FallbackColor=\"#", xaml);
    }

    [Theory]
    [InlineData(@"SalmonEgg\SalmonEgg\Presentation\Converters\PlanStatusToColorConverter.cs")]
    [InlineData(@"SalmonEgg\SalmonEgg\Presentation\Converters\ConnectionStatusToColorConverter.cs")]
    [InlineData(@"SalmonEgg\SalmonEgg\Presentation\Converters\ResourceTypeIconConverter.cs")]
    public void SemanticColorConverters_UseThemeResources(string relativePath)
    {
        var source = LoadText(relativePath);

        Assert.Contains("ThemeBrushConverter.Resolve", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new SolidColorBrush", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ColorHelper.FromArgb", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.UI.Colors", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposerTextBoxes_UseThemeAwareNativeTextBoxStyle()
    {
        var appXaml = LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml");
        var chatInputXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Controls\ChatInputArea.xaml");
        var miniChatXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Presentation\Views\MiniWindow\MiniChatView.xaml");

        Assert.Contains("x:Key=\"ComposerTextBoxStyle\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("BasedOn=\"{StaticResource DefaultTextBoxStyle}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Foreground\" Value=\"{ThemeResource TextFillColorPrimaryBrush}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"PlaceholderForeground\" Value=\"{ThemeResource TextFillColorSecondaryBrush}\"", appXaml, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(chatInputXaml, "Style=\"{StaticResource ComposerTextBoxStyle}\""));
        Assert.Equal(1, CountOccurrences(miniChatXaml, "Style=\"{StaticResource ComposerTextBoxStyle}\""));
    }

    [Fact]
    public void InputBoxes_UseThemeAwareImplicitStyles()
    {
        var appXaml = XDocument.Parse(LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml"));
        var textBoxStyle = FindImplicitStyleByTargetType(appXaml, "TextBox");
        var passwordBoxStyle = FindImplicitStyleByTargetType(appXaml, "PasswordBox");
        var numberBoxStyle = FindImplicitStyleByTargetType(appXaml, "NumberBox");

        Assert.Equal("{StaticResource DefaultTextBoxStyle}", GetAttributeByLocalName(textBoxStyle, "BasedOn"));
        AssertStyleSetter(textBoxStyle, "Foreground", "{ThemeResource TextFillColorPrimaryBrush}");
        AssertStyleSetter(textBoxStyle, "PlaceholderForeground", "{ThemeResource TextFillColorSecondaryBrush}");

        Assert.Equal("{StaticResource DefaultPasswordBoxStyle}", GetAttributeByLocalName(passwordBoxStyle, "BasedOn"));
        AssertStyleSetter(passwordBoxStyle, "Foreground", "{ThemeResource TextFillColorPrimaryBrush}");
        Assert.DoesNotContain(
            passwordBoxStyle.Elements(),
            element => string.Equals(GetAttributeByLocalName(element, "Property"), "PlaceholderForeground", StringComparison.Ordinal));

        Assert.Null(GetAttributeByLocalName(numberBoxStyle, "BasedOn"));
        AssertStyleSetter(numberBoxStyle, "Foreground", "{ThemeResource TextFillColorPrimaryBrush}");

        var settingsPages = new[]
        {
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AcpConnectionSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\AgentProfileEditorPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DataStorageSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\DiagnosticsSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\Settings\McpSettingsPage.xaml",
            @"SalmonEgg\SalmonEgg\Presentation\Views\ConfigurationEditorDialog.xaml"
        };

        foreach (var page in settingsPages)
        {
            var document = XDocument.Parse(LoadXaml(page));
            var textBoxes = document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "TextBox", StringComparison.Ordinal))
                .ToArray();

            Assert.True(textBoxes.Length > 0, $"Expected at least one TextBox in {page}");
            Assert.All(textBoxes, AssertNoInputStyleOverride);

            var passwordBoxes = document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "PasswordBox", StringComparison.Ordinal));
            Assert.All(passwordBoxes, AssertNoInputStyleOverride);

            var numberBoxes = document
                .Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "NumberBox", StringComparison.Ordinal));
            Assert.All(numberBoxes, AssertNoInputStyleOverride);
        }
    }

    [Fact]
    public void SkiaNumberBoxThemeOverride_ReplacesFrameworkScopedTemplateWithoutFocusThemeMismatch()
    {
        var appCode = LoadText(@"SalmonEgg\SalmonEgg\App.xaml.cs");
        var overrideXaml = LoadXaml(@"SalmonEgg\SalmonEgg\Styles\Skia\UnoNumberBoxThemeOverrides.xaml");

        // Both halves - the call and the method - must be behind the platform switch, or the workaround
        // leaks into the Windows WinUI 3 path it exists to stay out of.
        AssertInsideConditionalRegion(appCode, "__UNO_SKIA__ || __WASM__", "TryApplyUnoNumberBoxThemeOverride();");
        AssertInsideConditionalRegion(
            appCode,
            "__UNO_SKIA__ || __WASM__",
            "private void TryApplyUnoNumberBoxThemeOverride()");
        Assert.Contains("TryApplyUnoNumberBoxThemeOverride();", appCode, StringComparison.Ordinal);
        Assert.Contains("Styles/Skia/UnoNumberBoxThemeOverrides.xaml", appCode, StringComparison.Ordinal);
        Assert.Contains("Resources[typeof(Microsoft.UI.Xaml.Controls.NumberBox)] = numberBoxStyle;", appCode, StringComparison.Ordinal);
        Assert.Contains("overrides[\"UnoNumberBoxStyleOverride\"]", appCode, StringComparison.Ordinal);
        Assert.Contains("Copyright (c) Microsoft Corporation", overrideXaml, StringComparison.Ordinal);
        Assert.Contains("Uno 6.6.166 (commit 438b300b6171b3f2712f8897f10ea620784843ca)", overrideXaml, StringComparison.Ordinal);
        Assert.Contains("unoplatform/uno#24021", overrideXaml, StringComparison.Ordinal);
        Assert.Contains("xmlns:skia=\"http://uno.ui/skia\"", overrideXaml, StringComparison.Ordinal);
        Assert.Contains("mc:Ignorable=\"skia\"", overrideXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"UnoNumberBoxStyleOverride\"", overrideXaml, StringComparison.Ordinal);
        Assert.Contains("TargetType=\"controls:NumberBox\"", overrideXaml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource NumberBoxTextBoxStyle}\"", overrideXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"Foreground\" Value=\"{ThemeResource TextFillColorPrimaryBrush}\"", overrideXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"NumberBoxTextBoxStyle\"", overrideXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ContentElement\"", overrideXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"Focused\"", overrideXaml, StringComparison.Ordinal);
        Assert.Contains("skia:SelectionFlyout=\"{TemplateBinding SelectionFlyout}\"", overrideXaml, StringComparison.Ordinal);
        Assert.Contains("skia:ScrollViewer.IsHorizontalRailEnabled=\"{TemplateBinding ScrollViewer.IsHorizontalRailEnabled}\"", overrideXaml, StringComparison.Ordinal);
        Assert.Contains("skia:ScrollViewer.IsVerticalRailEnabled=\"{TemplateBinding ScrollViewer.IsVerticalRailEnabled}\"", overrideXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard.TargetProperty=\"RequestedTheme\"", overrideXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ScrollViewer.IsDeferredScrollingEnabled", overrideXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("TextReadingOrder=\"{TemplateBinding TextReadingOrder}\"", overrideXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("PreventKeyboardDisplayOnProgrammaticFocus=", overrideXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ShouldConstrainToRootBounds=", overrideXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppXaml_DoesNotDeclareASecondUiMotionControllerInstance()
    {
        var xaml = LoadXaml(@"SalmonEgg\SalmonEgg\App.xaml");

        Assert.DoesNotContain("<models:UiMotionController x:Key=", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void AppBootLog_IsConditionalDebugOnly()
    {
        var appCode = LoadText(@"SalmonEgg\SalmonEgg\App.xaml.cs");

        Assert.Contains("[Conditional(\"DEBUG\")]", appCode, StringComparison.Ordinal);
        Assert.Contains("internal static void BootLog(string message)", appCode, StringComparison.Ordinal);
        Assert.Contains("#if DEBUG", appCode, StringComparison.Ordinal);
        Assert.Contains("boot.log", appCode, StringComparison.Ordinal);
    }

    [Fact]
    public void AppMotionPreference_DoesNotOverrideNativeControlTemplateMotion()
    {
        var appCode = LoadText(@"SalmonEgg\SalmonEgg\App.xaml.cs");
        var uiRuntimeCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Services\UiRuntimeService.cs");
        var motionCode = LoadText(@"SalmonEgg\SalmonEgg\Presentation\Models\UiMotionController.cs");
        var repoRoot = FindRepoRoot();
        var reducedMotionDictionary = Path.Combine(
            repoRoot,
            "SalmonEgg",
            "SalmonEgg",
            "Styles",
            "ReducedMotion.xaml");

        Assert.False(
            File.Exists(reducedMotionDictionary),
            "Application motion settings must not override native WinUI control-template animation resources.");
        Assert.DoesNotContain("ReducedMotionDictionary", appCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyReducedMotion", appCode, StringComparison.Ordinal);
        Assert.DoesNotContain("FeatureConfiguration.ThemeAnimation.DefaultThemeAnimationDuration", appCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyReducedMotion", uiRuntimeCode, StringComparison.Ordinal);
        Assert.Contains("IsSystemAnimationEnabled", motionCode, StringComparison.Ordinal);
        Assert.Contains("IsEffectiveAnimationEnabled", motionCode, StringComparison.Ordinal);
        Assert.Contains("Timeline.AllowDependentAnimations", uiRuntimeCode, StringComparison.Ordinal);
        Assert.Contains("UISettings", uiRuntimeCode, StringComparison.Ordinal);
        Assert.Contains("AnimationsEnabledChanged", uiRuntimeCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductCSharp_DoesNotOverrideNativeControlTemplateMotion()
    {
        var forbiddenTokens = new[]
        {
            "ReducedMotionDictionary",
            "ApplyReducedMotion",
            "FeatureConfiguration.ThemeAnimation.DefaultThemeAnimationDuration",
            "ThemeAnimation.DefaultThemeAnimationDuration",
            "UISettingsController",
            "ControlNormalAnimationDuration",
            "ControlFastAnimationDuration",
            "ControlFastAnimationAfterDuration",
            "ControlFasterAnimationDuration",
            "ComboBoxItemScaleAnimationDuration",
            "ScrollBarColorChangeDuration",
            "ScrollBarContractDuration",
            "ScrollBarExpandDuration",
            "ScrollBarOpacityChangeDuration",
            "ScrollViewerSeparatorContractDuration",
            "ScrollViewerSeparatorExpandDuration",
            "ScrollViewScrollBarsNoTouchDuration",
            "ScrollViewScrollBarsSeparatorContractDuration",
            "ScrollViewScrollBarsSeparatorExpandDuration",
            "SplitViewPaneAnimationCloseDuration",
            "SplitViewPaneAnimationOpenDuration",
            "SplitViewPaneAnimationOpenPreDuration"
        };

        var violations = EnumerateProductCSharpFiles()
            .SelectMany(file =>
            {
                var content = File.ReadAllText(file);
                return forbiddenTokens
                    .Where(token => content.Contains(token, StringComparison.Ordinal))
                    .Select(token => $"{Path.GetRelativePath(FindRepoRoot(), file)}: {token}");
            })
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Product C# must not override native WinUI/Uno control-template motion; bind only application-owned transitions."
            + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }
}
