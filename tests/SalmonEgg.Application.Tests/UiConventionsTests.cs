using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace SalmonEgg.Application.Tests;

public class UiConventionsTests
{
    private static CompilationUnitSyntax ReadCSharpSyntaxTree(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var parseOptions = new CSharpParseOptions(preprocessorSymbols: ["WINDOWS"]);
        var tree = CSharpSyntaxTree.ParseText(text, parseOptions);
        return tree.GetCompilationUnitRoot();
    }

    private static XDocument ReadXml(string filePath) => XDocument.Load(filePath);

    private static XElement FindElementByXName(XDocument document, string elementLocalName, string xName)
        => document
            .Descendants()
            .Single(element =>
                string.Equals(element.Name.LocalName, elementLocalName, StringComparison.Ordinal)
                && string.Equals(
                    element.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, "Name", StringComparison.Ordinal))?.Value,
                    xName,
                    StringComparison.Ordinal));

    private static string? GetAttributeValueByLocalName(XElement element, string attributeLocalName)
        => element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, attributeLocalName, StringComparison.Ordinal))
            ?.Value;

    private static string? GetAttributeValueByXamlName(XElement element, string attributeName)
        => element.Attributes()
            .FirstOrDefault(attribute =>
                string.Equals(attribute.Name.LocalName, attributeName, StringComparison.Ordinal)
                || string.Equals(attribute.Name.ToString(), attributeName, StringComparison.Ordinal))
            ?.Value;

    private static List<string> EnumerateUiXamlFiles(string repoRoot)
    {
        var uiRoot = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg");
        return Directory.EnumerateFiles(uiRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static IEnumerable<string> EnumerateXmlTextValues(XDocument document)
        => document
            .Descendants()
            .SelectMany(element =>
                element.Attributes().Select(attribute => attribute.Value)
                    .Concat(element.Nodes().OfType<XText>().Select(text => text.Value)));

    private static HashSet<string> ReadXamlKeys(string filePath)
    {
        var doc = ReadXml(filePath);
        return doc
            .Descendants()
            .Select(element => element.Attributes().FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, "Key", StringComparison.Ordinal))?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> ReadReswKeys(string filePath)
    {
        var doc = XDocument.Load(filePath);
        return doc
            .Descendants("data")
            .Select(node => node.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "SalmonEgg.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Unable to locate repo root (SalmonEgg.sln not found).");
    }

    [Fact]
    public void XamlCodeBehind_ShouldResolveViewModelsBeforeInitializeComponent()
    {
        var repoRoot = FindRepoRoot();
        var uiRoot = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg");

        var files = Directory.EnumerateFiles(uiRoot, "*.xaml.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(files);

        var failures = new List<string>();

        foreach (var file in files)
        {
            var root = ReadCSharpSyntaxTree(file);
            var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
            var initInvocation = invocations.FirstOrDefault(invocation =>
                string.Equals(invocation.Expression.ToString(), "InitializeComponent", StringComparison.Ordinal));
            if (initInvocation is null)
            {
                continue;
            }

            var initIndex = initInvocation.SpanStart;
            var diInvocations = invocations.Where(invocation =>
            {
                var methodName = invocation.Expression switch
                {
                    IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                    MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
                    GenericNameSyntax genericName => genericName.Identifier.ValueText,
                    _ => string.Empty
                };
                return string.Equals(methodName, "GetRequiredService", StringComparison.Ordinal)
                    || string.Equals(methodName, "GetService", StringComparison.Ordinal)
                    || string.Equals(methodName, "CreateScope", StringComparison.Ordinal);
            });

            if (diInvocations.Any(invocation => invocation.SpanStart > initIndex))
            {
                failures.Add($"{file}: DI resolution occurs after InitializeComponent()");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void MiniChatWindow_ShouldUseWinUiTitleBarControlOnWindows()
    {
        var repoRoot = FindRepoRoot();
        var windowFile = Path.Combine(
            repoRoot,
            "SalmonEgg",
            "SalmonEgg",
            "Presentation",
            "Views",
            "MiniWindow",
            "MiniChatWindow.cs");
        var viewCodeBehindFile = Path.Combine(
            repoRoot,
            "SalmonEgg",
            "SalmonEgg",
            "Presentation",
            "Views",
            "MiniWindow",
            "MiniChatView.xaml.cs");

        Assert.True(File.Exists(windowFile), $"Expected mini window implementation at '{windowFile}'.");
        Assert.True(File.Exists(viewCodeBehindFile), $"Expected mini window view implementation at '{viewCodeBehindFile}'.");

        var windowText = File.ReadAllText(windowFile);
        var viewText = File.ReadAllText(viewCodeBehindFile);

        Assert.Contains("ExtendsContentIntoTitleBar = true", windowText, StringComparison.Ordinal);
        Assert.Contains("SetTitleBar(", windowText, StringComparison.Ordinal);
        Assert.Contains("TitleBar", viewText, StringComparison.Ordinal);
        Assert.DoesNotContain("InputNonClientPointerSource", windowText, StringComparison.Ordinal);
        Assert.DoesNotContain("NonClientRegionKind", windowText, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageManifest_ShouldReferenceWindowsSpecificIconAssets()
    {
        var repoRoot = FindRepoRoot();
        var manifestFile = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Package.appxmanifest");
        var document = ReadXml(manifestFile);
        var application = document
            .Descendants()
            .Single(element => string.Equals(element.Name.LocalName, "Application", StringComparison.Ordinal));
        var visualElements = application
            .Elements()
            .Single(element => string.Equals(element.Name.LocalName, "VisualElements", StringComparison.Ordinal));
        var defaultTile = visualElements
            .Elements()
            .Single(element => string.Equals(element.Name.LocalName, "DefaultTile", StringComparison.Ordinal));
        var splashScreen = visualElements
            .Elements()
            .Single(element => string.Equals(element.Name.LocalName, "SplashScreen", StringComparison.Ordinal));
        var protocolLogo = application
            .Descendants()
            .Single(element => string.Equals(element.Name.LocalName, "Logo", StringComparison.Ordinal)
                && string.Equals(element.Parent?.Name.LocalName, "Protocol", StringComparison.Ordinal));
        var logo = document
            .Descendants()
            .SingleOrDefault(element => string.Equals(element.Name.LocalName, "Logo", StringComparison.Ordinal)
                && string.Equals(element.Parent?.Name.LocalName, "Properties", StringComparison.Ordinal));
        var properties = document
            .Descendants()
            .Single(element => string.Equals(element.Name.LocalName, "Properties", StringComparison.Ordinal));
        var displayName = properties
            .Elements()
            .Single(element => string.Equals(element.Name.LocalName, "DisplayName", StringComparison.Ordinal));
        var publisherDisplayName = properties
            .Elements()
            .Single(element => string.Equals(element.Name.LocalName, "PublisherDisplayName", StringComparison.Ordinal));

        Assert.NotNull(logo);
        Assert.Equal("Salmon Egg", displayName.Value.Trim());
        Assert.Equal("Salmon Egg", publisherDisplayName.Value.Trim());
        Assert.Equal(@"Assets\Icons\Windows\iconLogo.png", logo!.Value.Trim());
        Assert.Equal("transparent", GetAttributeValueByLocalName(visualElements, "BackgroundColor"));
        Assert.Equal(@"Assets\Icons\Windows\iconLogo44.png", GetAttributeValueByLocalName(visualElements, "Square44x44Logo"));
        Assert.Equal(@"Assets\Icons\Windows\iconLogo150.png", GetAttributeValueByLocalName(visualElements, "Square150x150Logo"));
        Assert.Equal(@"Assets\Icons\Windows\SmallTile.png", GetAttributeValueByLocalName(defaultTile, "Square71x71Logo"));
        Assert.Equal(@"Assets\Icons\Windows\WideTile.png", GetAttributeValueByLocalName(defaultTile, "Wide310x150Logo"));
        Assert.Equal(@"Assets\Icons\Windows\LargeTile.png", GetAttributeValueByLocalName(defaultTile, "Square310x310Logo"));
        Assert.Equal(@"Assets\Icons\Windows\SplashScreen.png", GetAttributeValueByLocalName(splashScreen, "Image"));
        Assert.Equal(@"Assets\Icons\Windows\iconLogo.png", protocolLogo.Value.Trim());
        Assert.DoesNotContain(properties.Elements().Select(element => element.Value), value => value.Contains("appicon", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PackageManifest_ShouldKeepSquare44AndSquare150LogoResourceFamiliesWithinWackLimits()
    {
        var repoRoot = FindRepoRoot();
        var manifestFile = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Package.appxmanifest");
        var document = ReadXml(manifestFile);
        var visualElements = document
            .Descendants()
            .Single(element => string.Equals(element.Name.LocalName, "VisualElements", StringComparison.Ordinal));

        var square44Logo = GetAttributeValueByLocalName(visualElements, "Square44x44Logo");
        var square150Logo = GetAttributeValueByLocalName(visualElements, "Square150x150Logo");

        Assert.Equal(@"Assets\Icons\Windows\iconLogo44.png", square44Logo);
        Assert.Equal(@"Assets\Icons\Windows\iconLogo150.png", square150Logo);
        Assert.NotEqual(square44Logo, square150Logo);

        AssertWindowsIconScaleDimensions(repoRoot, square44Logo!, 44);
        AssertWindowsIconScaleDimensions(repoRoot, square150Logo!, 150);
    }

    [Fact]
    public void UnoSingleProject_ShouldDeclareCustomPngAsUnoIconSourceAndWindowsIco()
    {
        var repoRoot = FindRepoRoot();
        var projectFile = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "SalmonEgg.csproj");
        var document = ReadXml(projectFile);
        var propertyGroups = document
            .Descendants("PropertyGroup")
            .ToList();
        var itemGroups = document
            .Descendants("ItemGroup")
            .ToList();
        var appIcon = itemGroups
            .Elements("UnoIcon")
            .SingleOrDefault(element =>
                string.Equals(element.Attribute("Include")?.Value?.Trim(), @"Assets\Icons\icon.png", StringComparison.Ordinal));
        var applicationIcon = propertyGroups
            .Elements("ApplicationIcon")
            .SingleOrDefault();

        Assert.NotNull(appIcon);
        Assert.NotNull(applicationIcon);
        Assert.Equal(@"Assets\Icons\Windows\icon.ico", applicationIcon!.Value.Trim());
        Assert.Empty(propertyGroups.Elements("UnoIconBackgroundFile"));
        Assert.Empty(propertyGroups.Elements("UnoIconForegroundFile"));
        Assert.True(File.Exists(Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Assets", "Icons", "icon.png")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Assets", "Icons", "Windows", "icon.ico")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Assets", "Icons", "Windows", "iconLogo44.scale-100.png")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Assets", "Icons", "Windows", "iconLogo.scale-100.png")));
        Assert.True(File.Exists(Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Assets", "Icons", "Windows", "iconLogo150.scale-100.png")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Assets", "Icons", "appicon.png")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Assets", "Icons", "icon.svg")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Assets", "Icons", "icon_foreground.svg")));
    }

    [Theory]
    [InlineData("src", "SalmonEgg.Domain", "SalmonEgg.Domain.csproj")]
    [InlineData("src", "SalmonEgg.Infrastructure", "SalmonEgg.Infrastructure.csproj")]
    public void MultiTargetLibraries_ShouldScopeSystemTextJsonPackageToNetstandard(
        string projectRoot,
        string projectDirectory,
        string projectFileName)
    {
        var repoRoot = FindRepoRoot();
        var projectFile = Path.Combine(repoRoot, projectRoot, projectDirectory, projectFileName);
        var xml = XDocument.Load(projectFile);
        var root = xml.Root;
        Assert.NotNull(root);

        var targetFrameworks = root!
            .Descendants("TargetFrameworks")
            .Select(node => node.Value.Trim())
            .FirstOrDefault();
        Assert.Equal("netstandard2.1;net10.0", targetFrameworks);

        var itemGroups = root
            .Descendants("ItemGroup")
            .Where(group => string.Equals(
                group.Attribute("Condition")?.Value?.Trim(),
                "'$(TargetFramework)' == 'netstandard2.1'",
                StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(itemGroups);

        var hasScopedSystemTextJsonReference = itemGroups.Any(group =>
            group.Elements("PackageReference").Any(packageReference =>
                string.Equals(
                    packageReference.Attribute("Include")?.Value?.Trim(),
                    "System.Text.Json",
                    StringComparison.Ordinal)));

        Assert.True(hasScopedSystemTextJsonReference);
    }

    [Fact]
    public void Xaml_ShouldAvoidLegacySystemControlHighlightBrushes()
    {
        var repoRoot = FindRepoRoot();
        var files = EnumerateUiXamlFiles(repoRoot);

        Assert.NotEmpty(files);

        var forbidden = new[]
        {
            "SystemControlHighlightLowBrush",
            "SystemControlHighlightBaseLowBrush",
            "SystemControlHighlightAccentBrush",
            "SystemControlForegroundBaseLowBrush",
            "SystemControlForegroundBaseMediumBrush",
            "SystemControlForegroundBaseHighBrush",
        };

        var failures = new List<string>();
        foreach (var file in files)
        {
            var doc = ReadXml(file);
            var values = EnumerateXmlTextValues(doc).ToArray();
            foreach (var key in forbidden)
            {
                if (values.Any(value => value.Contains(key, StringComparison.Ordinal)))
                {
                    failures.Add($"{file}: contains legacy resource '{key}'");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Xaml_ShouldNotUse_RuntimeBindingMarkupExtension()
    {
        var repoRoot = FindRepoRoot();
        var files = EnumerateUiXamlFiles(repoRoot);
        Assert.NotEmpty(files);

        var failures = new List<string>();
        foreach (var file in files)
        {
            var doc = ReadXml(file);
            var values = EnumerateXmlTextValues(doc).ToArray();

            // Enforce "all x:Bind" convention: runtime {Binding ...} is forbidden.
            if (values.Any(value =>
                    value.Contains("{Binding", StringComparison.Ordinal) ||
                    value.Contains("{ Binding", StringComparison.Ordinal) ||
                    value.Contains("{binding", StringComparison.OrdinalIgnoreCase) ||
                    value.Contains("{ binding", StringComparison.OrdinalIgnoreCase)))
            {
                failures.Add($"{file}: contains runtime '{{Binding}}' markup extension");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void BottomPanelButton_Localization_ShouldUseToolTipServicePropertyKey()
    {
        var repoRoot = FindRepoRoot();
        var reswFiles = new[]
        {
            Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Strings", "en", "Resources.resw"),
            Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Strings", "en-US", "Resources.resw"),
            Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Strings", "zh-Hans", "Resources.resw")
        };

        var failures = new List<string>();
        foreach (var file in reswFiles)
        {
            var keys = ReadReswKeys(file);
            if (!keys.Contains("BottomPanelButton.ToolTipService.ToolTip"))
            {
                failures.Add($"{file}: missing BottomPanelButton.ToolTipService.ToolTip");
            }

            if (keys.Contains("BottomPanelButton.ToolTip"))
            {
                failures.Add($"{file}: contains invalid BottomPanelButton.ToolTip key");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void AuxiliaryPanelButtons_ShouldUseLocalizedTooltipsAndAutomationNames()
    {
        var repoRoot = FindRepoRoot();
        var mainPageXaml = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "MainPage.xaml");
        var mainPage = ReadXml(mainPageXaml);
        var diffButton = FindElementByXName(mainPage, "ToggleButton", "DiffPanelButton");
        var todoButton = FindElementByXName(mainPage, "ToggleButton", "TodoPanelButton");
        Assert.Equal("DiffPanelButton", GetAttributeValueByLocalName(diffButton, "Uid"));
        Assert.Equal("TodoPanelButton", GetAttributeValueByLocalName(todoButton, "Uid"));
        Assert.Contains(
            diffButton.Attributes(),
            attribute => string.Equals(attribute.Name.LocalName, "AutomationProperties.Name", StringComparison.Ordinal)
                && string.Equals(attribute.Value, string.Empty, StringComparison.Ordinal));
        Assert.Contains(
            todoButton.Attributes(),
            attribute => string.Equals(attribute.Name.LocalName, "AutomationProperties.Name", StringComparison.Ordinal)
                && string.Equals(attribute.Value, string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain(
            diffButton.Attributes(),
            attribute => string.Equals(attribute.Name.LocalName, "ToolTip", StringComparison.Ordinal)
                && string.Equals(attribute.Value, "Diff", StringComparison.Ordinal));
        Assert.DoesNotContain(
            todoButton.Attributes(),
            attribute => string.Equals(attribute.Name.LocalName, "ToolTip", StringComparison.Ordinal)
                && string.Equals(attribute.Value, "Todo", StringComparison.Ordinal));

        var reswFiles = new[]
        {
            Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Strings", "en", "Resources.resw"),
            Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Strings", "en-US", "Resources.resw"),
            Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Strings", "zh-Hans", "Resources.resw")
        };

        var failures = new List<string>();
        foreach (var file in reswFiles)
        {
            var keys = ReadReswKeys(file);
            if (!keys.Contains("DiffPanelButton.ToolTipService.ToolTip"))
            {
                failures.Add($"{file}: missing DiffPanelButton.ToolTipService.ToolTip");
            }

            if (!keys.Contains("DiffPanelButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"))
            {
                failures.Add($"{file}: missing DiffPanelButton automation name");
            }

            if (!keys.Contains("TodoPanelButton.ToolTipService.ToolTip"))
            {
                failures.Add($"{file}: missing TodoPanelButton.ToolTipService.ToolTip");
            }

            if (!keys.Contains("TodoPanelButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"))
            {
                failures.Add($"{file}: missing TodoPanelButton automation name");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void AuxiliaryPanelButtons_ShouldUseDeterministicVectorIcons()
    {
        var repoRoot = FindRepoRoot();
        var mainPageXaml = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "MainPage.xaml");
        var mainPageCodeBehind = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "MainPage.xaml.cs");
        var appXaml = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "App.xaml");
        var iconDictionaryXaml = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Styles", "TitleBarIcons.xaml");
        var titleBarButtonStylesXaml = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Styles", "TitleBarCommandButtonStyle.xaml");
        var iconDictionaryText = File.ReadAllText(iconDictionaryXaml);
        var titleBarButtonStyles = ReadXml(titleBarButtonStylesXaml);
        var iconDictionary = ReadXml(iconDictionaryXaml);
        var codeBehindText = File.ReadAllText(mainPageCodeBehind);
        var mainPage = ReadXml(mainPageXaml);
        var miniChatView = ReadXml(Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Presentation", "Views", "MiniWindow", "MiniChatView.xaml"));
        var xNamespace = XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml");
        var backButton = FindElementByXName(mainPage, "Button", "TitleBarBackButton");
        var navButton = FindElementByXName(mainPage, "Button", "TitleBarToggleLeftNavButton");
        var miniWindowButton = FindElementByXName(mainPage, "Button", "TitleBarMiniWindowButton");
        var miniReturnButton = FindElementByXName(miniChatView, "Button", "MiniTitleBarReturnButton");
        var bottomButton = FindElementByXName(mainPage, "ToggleButton", "BottomPanelButton");
        var diffButton = FindElementByXName(mainPage, "ToggleButton", "DiffPanelButton");
        var todoButton = FindElementByXName(mainPage, "ToggleButton", "TodoPanelButton");

        Assert.Equal("{StaticResource TitleBarCommandButtonStyle}", GetAttributeValueByLocalName(backButton, "Style"));
        Assert.Equal("{StaticResource TitleBarBackIconTemplate}", GetAttributeValueByLocalName(backButton, "ContentTemplate"));
        Assert.Equal("{StaticResource TitleBarCommandButtonStyle}", GetAttributeValueByLocalName(navButton, "Style"));
        Assert.Equal("{StaticResource TitleBarToggleLeftNavIconTemplate}", GetAttributeValueByLocalName(navButton, "ContentTemplate"));
        Assert.Equal("{StaticResource TitleBarCommandButtonStyle}", GetAttributeValueByLocalName(miniWindowButton, "Style"));
        Assert.Equal("{StaticResource TitleBarOpenMiniWindowIconTemplate}", GetAttributeValueByLocalName(miniWindowButton, "ContentTemplate"));
        Assert.Equal("{StaticResource MiniTitleBarAccessoryButtonStyle}", GetAttributeValueByLocalName(miniReturnButton, "Style"));
        Assert.Equal("{StaticResource TitleBarReturnToMainWindowIconTemplate}", GetAttributeValueByLocalName(miniReturnButton, "ContentTemplate"));
        Assert.Equal("{StaticResource TitleBarToggleButtonStyle}", GetAttributeValueByLocalName(bottomButton, "Style"));
        Assert.Equal("{x:Bind GetBottomPanelButtonIconTemplate(LayoutVM.BottomPanelMode), Mode=OneWay}", GetAttributeValueByLocalName(bottomButton, "ContentTemplate"));
        Assert.Equal("{x:Bind LayoutVM.BottomPanelMode, Mode=OneWay, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=Dock}", GetAttributeValueByLocalName(bottomButton, "IsChecked"));
        Assert.Equal("{x:Bind GetDiffPanelButtonIconTemplate(LayoutVM.RightPanelMode), Mode=OneWay}", GetAttributeValueByLocalName(diffButton, "ContentTemplate"));
        Assert.Equal("{x:Bind LayoutVM.RightPanelMode, Mode=OneWay, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=Diff}", GetAttributeValueByLocalName(diffButton, "IsChecked"));
        Assert.Equal("{x:Bind GetTodoPanelButtonIconTemplate(LayoutVM.RightPanelMode), Mode=OneWay}", GetAttributeValueByLocalName(todoButton, "ContentTemplate"));
        Assert.Equal("{x:Bind LayoutVM.RightPanelMode, Mode=OneWay, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=Todo}", GetAttributeValueByLocalName(todoButton, "IsChecked"));

        var allAttributeValues = mainPage
            .Descendants()
            .SelectMany(element => element.Attributes())
            .Select(attribute => attribute.Value)
            .ToArray();
        Assert.DoesNotContain("LayoutVM.DesiredRightPanelMode", allAttributeValues);
        Assert.DoesNotContain("LayoutVM.DesiredBottomPanelMode", allAttributeValues);
        Assert.DoesNotContain("BottomPanelVectorIcon", allAttributeValues);
        Assert.DoesNotContain("DiffPanelVectorIcon", allAttributeValues);
        Assert.DoesNotContain("TodoPanelVectorIcon", allAttributeValues);
        Assert.DoesNotContain("PreferWindowsAuxiliaryGlyphs", allAttributeValues);

        var app = ReadXml(appXaml);
        var appSources = app
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "ResourceDictionary", StringComparison.Ordinal))
            .Select(element => GetAttributeValueByLocalName(element, "Source"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        Assert.DoesNotContain("ms-appx:///Styles/AuxiliaryPanelIcons.xaml", appSources);
        Assert.Contains("ms-appx:///Styles/TitleBarIcons.xaml", appSources);
        Assert.Contains("Picture In Picture Enter", iconDictionaryText, StringComparison.Ordinal);
        Assert.Contains("Picture In Picture Exit", iconDictionaryText, StringComparison.Ordinal);
        Assert.DoesNotContain("Desktop Arrow Right", iconDictionaryText, StringComparison.Ordinal);
        Assert.DoesNotContain("Tab Desktop Arrow Left", iconDictionaryText, StringComparison.Ordinal);
        Assert.Contains("Glyph=\"&#xE700;\"", iconDictionaryText, StringComparison.Ordinal);

        var miniAccessoryStyle = titleBarButtonStyles
            .Descendants()
            .Single(element =>
                string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Key")?.Value, "MiniTitleBarAccessoryButtonStyle", StringComparison.Ordinal));
        var miniAccessorySetters = miniAccessoryStyle
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Setter", StringComparison.Ordinal))
            .ToArray();
        Assert.Contains(miniAccessorySetters, setter =>
            string.Equals(setter.Attribute("Property")?.Value, "Width", StringComparison.Ordinal)
            && string.Equals(setter.Attribute("Value")?.Value, "40", StringComparison.Ordinal));
        Assert.Contains(miniAccessorySetters, setter =>
            string.Equals(setter.Attribute("Property")?.Value, "Height", StringComparison.Ordinal)
            && string.Equals(setter.Attribute("Value")?.Value, "40", StringComparison.Ordinal));
        Assert.DoesNotContain(miniAccessoryStyle.Descendants(), element => string.Equals(element.Name.LocalName, "Viewbox", StringComparison.Ordinal));

        var returnIconTemplate = iconDictionary
            .Descendants()
            .Single(element =>
                string.Equals(element.Name.LocalName, "DataTemplate", StringComparison.Ordinal)
                && string.Equals(element.Attribute(xNamespace + "Key")?.Value, "TitleBarReturnToMainWindowIconTemplate", StringComparison.Ordinal));
        var returnIconPath = returnIconTemplate
            .Descendants()
            .Single(element => string.Equals(element.Name.LocalName, "Path", StringComparison.Ordinal));
        Assert.Equal("16", returnIconPath.Attribute("Width")?.Value);
        Assert.Equal("16", returnIconPath.Attribute("Height")?.Value);

        var iconKeys = ReadXamlKeys(iconDictionaryXaml);
        Assert.Contains("TitleBarOpenMiniWindowIconTemplate", iconKeys);
        Assert.Contains("TitleBarReturnToMainWindowIconTemplate", iconKeys);
        Assert.Contains("TitleBarBackIconTemplate", iconKeys);
        Assert.Contains("TitleBarToggleLeftNavIconTemplate", iconKeys);
        Assert.Contains("BottomPanelTitleBarRegularIconTemplate", iconKeys);
        Assert.Contains("BottomPanelTitleBarFilledIconTemplate", iconKeys);
        Assert.Contains("DiffPanelTitleBarRegularIconTemplate", iconKeys);
        Assert.Contains("DiffPanelTitleBarFilledIconTemplate", iconKeys);
        Assert.Contains("TodoPanelTitleBarRegularIconTemplate", iconKeys);
        Assert.Contains("TodoPanelTitleBarFilledIconTemplate", iconKeys);
        Assert.DoesNotContain("BottomPanelTitleBarToggleButtonStyle", iconKeys);
        Assert.DoesNotContain("DiffPanelTitleBarToggleButtonStyle", iconKeys);
        Assert.DoesNotContain("TodoPanelTitleBarToggleButtonStyle", iconKeys);
        Assert.Contains("GetBottomPanelButtonIconTemplate", codeBehindText);
        Assert.Contains("GetDiffPanelButtonIconTemplate", codeBehindText);
        Assert.Contains("GetTodoPanelButtonIconTemplate", codeBehindText);
        Assert.Contains("GetAuxiliaryIconTemplate", codeBehindText);
    }

    [Fact]
    public void ShellAuxiliaryPanels_ShouldRenderBottomPanelOutsideChatOverlayBlock()
    {
        var repoRoot = FindRepoRoot();
        var mainPage = ReadXml(Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "MainPage.xaml"));
        var chatView = ReadXml(Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Presentation", "Views", "Chat", "ChatView.xaml"));

        var mainPageBottomPanel = mainPage
            .Descendants()
            .SingleOrDefault(element => string.Equals(element.Name.LocalName, "BottomPanelHost", StringComparison.Ordinal));
        var chatViewBottomPanel = chatView
            .Descendants()
            .SingleOrDefault(element => string.Equals(element.Name.LocalName, "BottomPanelHost", StringComparison.Ordinal));
        var overlayPresenter = FindElementByXName(mainPage, "ContentControl", "ShellLoadingOverlayPresenter");

        Assert.NotNull(mainPageBottomPanel);
        Assert.Equal("1", GetAttributeValueByXamlName(mainPageBottomPanel!, "Grid.Row"));
        Assert.Contains(
            mainPage.Descendants().Where(element => string.Equals(element.Name.LocalName, "RowDefinition", StringComparison.Ordinal)),
            rowDefinition => string.Equals(
                GetAttributeValueByLocalName(rowDefinition, "Height"),
                "{x:Bind LayoutVM.BottomPanelHeight, Mode=OneWay, Converter={StaticResource GridLengthConverter}}",
                StringComparison.Ordinal));
        Assert.Null(chatViewBottomPanel);
        Assert.Equal("0", GetAttributeValueByXamlName(overlayPresenter, "Grid.Row"));
    }

    [Fact]
    public void MainPage_ShouldBindAuxiliaryPanelLayoutToShellLayoutViewModel()
    {
        var repoRoot = FindRepoRoot();
        var mainPage = ReadXml(Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "MainPage.xaml"));
        var mainPageXaml = File.ReadAllText(Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "MainPage.xaml"));
        var mainPageText = File.ReadAllText(Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "MainPage.xaml.cs"));
        var rightPanelSplitView = FindElementByXName(mainPage, "SplitView", "RightPanelSplitView");
        var rightPanelPane = FindElementByXName(mainPage, "Grid", "RightPanelPane");
        var mainNavView = FindElementByXName(mainPage, "NavigationView", "MainNavView");

        Assert.Equal("Right", GetAttributeValueByLocalName(rightPanelSplitView, "PanePlacement"));
        Assert.Equal("Inline", GetAttributeValueByLocalName(rightPanelSplitView, "DisplayMode"));
        Assert.Null(GetAttributeValueByLocalName(rightPanelSplitView, "CompactPaneLength"));
        Assert.Equal("{x:Bind LayoutVM.RightPanelVisible, Mode=OneWay}", GetAttributeValueByLocalName(rightPanelSplitView, "IsPaneOpen"));
        Assert.Equal("{x:Bind LayoutVM.RightPanelOpenPaneLength, Mode=OneWay}", GetAttributeValueByLocalName(rightPanelSplitView, "OpenPaneLength"));
        Assert.Equal("NavigationView.Content", rightPanelSplitView.Parent?.Name.LocalName);
        Assert.Equal(mainNavView, rightPanelSplitView.Parent?.Parent);
        Assert.False(
            mainNavView.Ancestors().Any(element => string.Equals(element.Name.LocalName, "SplitView", StringComparison.Ordinal)),
            "MainNavView must remain the outer native navigation shell; right panel SplitView belongs inside NavigationView.Content.");
        Assert.Contains(
            rightPanelSplitView.Descendants(),
            element => string.Equals(element.Name.LocalName, "Frame", StringComparison.Ordinal)
                && string.Equals(GetAttributeValueByXamlName(element, "Name"), "ContentFrame", StringComparison.Ordinal));
        Assert.Contains(
            rightPanelSplitView.Descendants(),
            element => string.Equals(element.Name.LocalName, "BottomPanelHost", StringComparison.Ordinal));
        Assert.Null(GetAttributeValueByLocalName(rightPanelPane, "Visibility"));
        Assert.DoesNotContain("RightPanelColumnDefinition", mainPageXaml, StringComparison.Ordinal);
        Assert.Contains(
            "Height=\"{x:Bind LayoutVM.BottomPanelHeight, Mode=OneWay, Converter={StaticResource GridLengthConverter}}\"",
            mainPageXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "Visibility=\"{x:Bind LayoutVM.BottomPanelVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}\"",
            mainPageXaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(ShellLayoutViewModel.RightPanelWidth)", mainPageText, StringComparison.Ordinal);
        Assert.DoesNotContain("nameof(ShellLayoutViewModel.BottomPanelHeight)", mainPageText, StringComparison.Ordinal);
        Assert.DoesNotContain("RightPanelSplitView.IsPaneOpen =", mainPageText, StringComparison.Ordinal);
        Assert.DoesNotContain("RightPanelPane.Visibility =", mainPageText, StringComparison.Ordinal);
        Assert.DoesNotContain("AuxiliaryPanelAnimation", mainPageText, StringComparison.Ordinal);
        Assert.DoesNotContain("ConfigureShellLayoutAnimations", mainPageText, StringComparison.Ordinal);
    }

    [Fact]
    public void AppTheme_ShouldNotOverrideNavigationViewPaneBackgroundThemeResources()
    {
        var repoRoot = FindRepoRoot();
        var appXaml = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "App.xaml");
        var appText = File.ReadAllText(appXaml);
        var keys = ReadXamlKeys(appXaml);

        Assert.DoesNotContain("NavigationViewDefaultPaneBackground", keys);
        Assert.DoesNotContain("NavigationViewExpandedPaneBackground", keys);
        Assert.DoesNotContain("NavigationViewTopPaneBackground", keys);
        Assert.DoesNotContain("x:Key=\"NavigationViewDefaultPaneBackground\"", appText);
        Assert.DoesNotContain("x:Key=\"NavigationViewExpandedPaneBackground\"", appText);
        Assert.DoesNotContain("x:Key=\"NavigationViewTopPaneBackground\"", appText);
    }

    [Fact]
    public void Xaml_ShouldNotOverrideNavigationViewPaneThemeResourceKeys()
    {
        var repoRoot = FindRepoRoot();
        var xamlFiles = EnumerateUiXamlFiles(repoRoot);

        var forbiddenKeys = new HashSet<string>(StringComparer.Ordinal)
        {
            "NavigationViewDefaultPaneBackground",
            "NavigationViewExpandedPaneBackground",
            "NavigationViewTopPaneBackground"
        };

        var violations = new List<string>();
        foreach (var file in xamlFiles)
        {
            var keys = ReadXamlKeys(file);
            var hit = keys.Where(forbiddenKeys.Contains).ToArray();
            if (hit.Length > 0)
            {
                violations.Add($"{file}: {string.Join(", ", hit)}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Do not override WinUI NavigationView pane theme resource keys in app XAML." + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Xaml_ShouldNotOverrideNativeControlMotionThemeResourceKeys()
    {
        var repoRoot = FindRepoRoot();
        var xamlFiles = EnumerateUiXamlFiles(repoRoot);

        var forbiddenKeys = new HashSet<string>(StringComparer.Ordinal)
        {
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

        var violations = new List<string>();
        foreach (var file in xamlFiles)
        {
            var keys = ReadXamlKeys(file);
            var hit = keys.Where(forbiddenKeys.Contains).ToArray();
            if (hit.Length > 0)
            {
                violations.Add($"{file}: {string.Join(", ", hit)}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Do not override native WinUI control motion resources in app XAML; bind application-owned transitions instead." + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Xaml_ShouldNotSetPaneBackgroundForNavigationShell()
    {
        var repoRoot = FindRepoRoot();
        var xamlFiles = EnumerateUiXamlFiles(repoRoot);

        var violations = new List<string>();
        foreach (var file in xamlFiles)
        {
            var text = File.ReadAllText(file);
            if (text.Contains("PaneBackground=", StringComparison.Ordinal))
            {
                violations.Add(file);
            }
        }

        Assert.True(
            violations.Count == 0,
            "Navigation shell should keep native pane background behavior. Remove manual PaneBackground assignments." + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void SkiaThemeOverrides_ShouldStayIsolatedToSingleNavigationMarginKey()
    {
        var repoRoot = FindRepoRoot();
        var skiaOverrides = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Styles", "Skia", "SkiaThemeOverrides.xaml");

        Assert.True(File.Exists(skiaOverrides), $"Expected Skia theme override dictionary at '{skiaOverrides}'.");

        var keys = ReadXamlKeys(skiaOverrides);
        Assert.Single(keys);
        Assert.Contains("NavigationViewPaneContentGridMargin", keys);

        var text = File.ReadAllText(skiaOverrides);
        Assert.DoesNotContain("NavigationViewDefaultPaneBackground", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigationViewExpandedPaneBackground", text, StringComparison.Ordinal);
        Assert.DoesNotContain("NavigationViewTopPaneBackground", text, StringComparison.Ordinal);
        Assert.DoesNotContain("PaneBackground=", text, StringComparison.Ordinal);
    }

    private static void AssertWindowsIconScaleDimensions(string repoRoot, string manifestAssetPath, int baseSize)
    {
        var normalizedPath = manifestAssetPath.Replace('\\', Path.DirectorySeparatorChar);
        var assetDirectory = Path.GetDirectoryName(normalizedPath);
        var assetBaseName = Path.GetFileNameWithoutExtension(normalizedPath);

        Assert.False(string.IsNullOrWhiteSpace(assetDirectory));
        Assert.False(string.IsNullOrWhiteSpace(assetBaseName));

        foreach (var (scale, expectedSize) in new[] { (100, baseSize), (125, baseSize * 125 / 100), (150, baseSize * 150 / 100), (200, baseSize * 2), (400, baseSize * 4) })
        {
            var imagePath = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", assetDirectory!, $"{assetBaseName}.scale-{scale}.png");

            Assert.True(File.Exists(imagePath), $"Missing {scale}% scaled image for '{manifestAssetPath}'.");

            var (width, height) = ReadPngDimensions(imagePath);
            Assert.Equal(expectedSize, width);
            Assert.Equal(expectedSize, height);
        }
    }

    private static (int Width, int Height) ReadPngDimensions(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new BinaryReader(stream);

        var signature = reader.ReadBytes(8);
        Assert.True(signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }), $"'{filePath}' is not a valid PNG file.");

        var chunkLengthBytes = reader.ReadBytes(4);
        var chunkTypeBytes = reader.ReadBytes(4);
        Assert.True(chunkLengthBytes.Length == 4 && chunkTypeBytes.Length == 4, $"'{filePath}' is missing the PNG IHDR header.");
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(chunkTypeBytes));

        var width = ReadBigEndianInt32(reader.ReadBytes(4));
        var height = ReadBigEndianInt32(reader.ReadBytes(4));
        return (width, height);
    }

    private static int ReadBigEndianInt32(byte[] bytes)
    {
        Assert.Equal(4, bytes.Length);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToInt32(bytes, 0);
    }
}
