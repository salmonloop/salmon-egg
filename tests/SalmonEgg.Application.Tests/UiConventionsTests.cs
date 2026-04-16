using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using SalmonEgg.Application.Services.Chat;
using SalmonEgg.Domain.Services;

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

    private static string GetInvocationMethodName(InvocationExpressionSyntax invocation)
        => invocation.Expression switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            _ => string.Empty
        };

    private static string? GetFirstGenericTypeArgumentName(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is GenericNameSyntax directGeneric)
        {
            return directGeneric.TypeArgumentList.Arguments.FirstOrDefault()?.ToString();
        }

        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess
            && memberAccess.Name is GenericNameSyntax memberGeneric)
        {
            return memberGeneric.TypeArgumentList.Arguments.FirstOrDefault()?.ToString();
        }

        return null;
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
                string.Equals(GetInvocationMethodName(invocation), "InitializeComponent", StringComparison.Ordinal));
            if (initInvocation is null)
            {
                continue;
            }

            var initIndex = initInvocation.SpanStart;
            var diInvocations = invocations.Where(invocation =>
            {
                var methodName = GetInvocationMethodName(invocation);
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
    public void DependencyInjection_ShouldRegisterChatViewModelAsSingleton()
    {
        var repoRoot = FindRepoRoot();
        var diFile = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "DependencyInjection.cs");
        var root = ReadCSharpSyntaxTree(diFile);
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();

        Assert.Contains(invocations, invocation =>
            string.Equals(GetInvocationMethodName(invocation), "AddSingleton", StringComparison.Ordinal)
            && string.Equals(GetFirstGenericTypeArgumentName(invocation), "ChatViewModel", StringComparison.Ordinal));
        Assert.DoesNotContain(invocations, invocation =>
            string.Equals(GetInvocationMethodName(invocation), "AddTransient", StringComparison.Ordinal)
            && string.Equals(GetFirstGenericTypeArgumentName(invocation), "ChatViewModel", StringComparison.Ordinal));
    }

    [Fact]
    public void DependencyInjection_ShouldRegisterChatBoundaryAdaptersAndCoordinators()
    {
        var repoRoot = FindRepoRoot();
        var diFile = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "DependencyInjection.cs");
        var root = ReadCSharpSyntaxTree(diFile);
        var singletonRegistrations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => string.Equals(GetInvocationMethodName(invocation), "AddSingleton", StringComparison.Ordinal))
            .Select(invocation => GetFirstGenericTypeArgumentName(invocation))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("ISettingsChatConnection", singletonRegistrations);
        Assert.Contains("IChatLaunchWorkflow", singletonRegistrations);
        Assert.Contains("IAcpConnectionCommands", singletonRegistrations);
        Assert.Contains("IAcpChatServiceFactory", singletonRegistrations);
        Assert.Contains("MainNavigationViewModel", singletonRegistrations);
        Assert.Contains("INavigationCoordinator", singletonRegistrations);
        Assert.Contains("AcpConnectionSettingsViewModel", singletonRegistrations);
    }

    [Fact]
    public void MiniWindowCoordinator_ShouldCreateDedicatedMiniChatWindow()
    {
        var repoRoot = FindRepoRoot();
        var coordinatorFile = Path.Combine(
            repoRoot,
            "SalmonEgg",
            "SalmonEgg",
            "Presentation",
            "Services",
            "MiniWindowCoordinator.cs");
        var root = ReadCSharpSyntaxTree(coordinatorFile);
        var objectCreations = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().ToList();
        Assert.Contains(
            objectCreations,
            creation => string.Equals(creation.Type.ToString(), "MiniChatWindow", StringComparison.Ordinal));
        Assert.DoesNotContain(
            objectCreations,
            creation => string.Equals(creation.Type.ToString(), "Microsoft.UI.Xaml.Window", StringComparison.Ordinal));
    }

    [Fact]
    public void MiniChatWindow_ShouldUseNativeTitleBarTakeoverOnWindows()
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

        Assert.True(File.Exists(windowFile), $"Expected mini window implementation at '{windowFile}'.");

        var root = ReadCSharpSyntaxTree(windowFile);
        var assignments = root.DescendantNodes().OfType<AssignmentExpressionSyntax>().ToList();
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        var identifiers = root.DescendantNodes().OfType<IdentifierNameSyntax>().Select(node => node.Identifier.ValueText).ToArray();
        var memberAccesses = root.DescendantNodes().OfType<MemberAccessExpressionSyntax>().Select(node => node.ToString()).ToArray();

        Assert.Contains(
            assignments,
            assignment => string.Equals(assignment.Left.ToString(), "ExtendsContentIntoTitleBar", StringComparison.Ordinal)
                && string.Equals(assignment.Right.ToString(), "true", StringComparison.Ordinal));
        Assert.Contains(
            invocations,
            invocation => string.Equals(GetInvocationMethodName(invocation), "SetTitleBar", StringComparison.Ordinal));
        Assert.Contains("InputNonClientPointerSource", identifiers);
        Assert.Contains("NonClientRegionKind.Passthrough", memberAccesses);
    }

    [Fact]
    public void ChatServiceFactory_ShouldNotDependOnAppLevelCapabilityManager()
    {
        var constructor = typeof(ChatServiceFactory).GetConstructors().Single();

        Assert.DoesNotContain(
            constructor.GetParameters(),
            parameter => parameter.ParameterType == typeof(ICapabilityManager));
    }

    [Fact]
    public void DependencyInjection_ShouldNotRegisterCapabilityManagerAsApplicationSingleton()
    {
        var repoRoot = FindRepoRoot();
        var diFile = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "DependencyInjection.cs");
        var root = ReadCSharpSyntaxTree(diFile);
        var singletonRegistrations = root.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Where(invocation => string.Equals(GetInvocationMethodName(invocation), "AddSingleton", StringComparison.Ordinal))
            .Select(invocation => GetFirstGenericTypeArgumentName(invocation))
            .Where(name => !string.IsNullOrWhiteSpace(name));

        Assert.DoesNotContain("ICapabilityManager", singletonRegistrations);
    }

    [Fact]
    public void AcpClient_ShouldNotInstantiateCapabilityManagerInternally()
    {
        var constructor = typeof(SalmonEgg.Infrastructure.Client.AcpClient)
            .GetConstructors()
            .OrderByDescending(ctor => ctor.GetParameters().Length)
            .First();

        Assert.DoesNotContain(
            constructor.GetParameters(),
            parameter => parameter.ParameterType == typeof(ICapabilityManager));
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
        var iconDictionaryXaml = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "Styles", "AuxiliaryPanelIcons.xaml");
        var codeBehindText = File.ReadAllText(mainPageCodeBehind);
        var mainPage = ReadXml(mainPageXaml);
        var bottomButton = FindElementByXName(mainPage, "ToggleButton", "BottomPanelButton");
        var diffButton = FindElementByXName(mainPage, "ToggleButton", "DiffPanelButton");
        var todoButton = FindElementByXName(mainPage, "ToggleButton", "TodoPanelButton");

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
        Assert.Contains("ms-appx:///Styles/AuxiliaryPanelIcons.xaml", appSources);

        var iconKeys = ReadXamlKeys(iconDictionaryXaml);
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
}
