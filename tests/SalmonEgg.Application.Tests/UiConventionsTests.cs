using System.Text.RegularExpressions;

namespace SalmonEgg.Application.Tests;

public class UiConventionsTests
{
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
            var text = File.ReadAllText(file);

            var initIndex = text.IndexOf("InitializeComponent();", StringComparison.Ordinal);
            if (initIndex < 0)
            {
                continue;
            }

            // If the code-behind resolves VMs via DI, it must do so before InitializeComponent()
            // to keep x:Bind stable (x:Bind is compile-time and does not rebind after assignment).
            var diMatch = Regex.Match(
                text,
                @"\b(GetRequiredService|GetService|CreateScope)\b",
                RegexOptions.CultureInvariant);

            if (!diMatch.Success)
            {
                continue;
            }

            if (diMatch.Index > initIndex)
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
        var text = File.ReadAllText(diFile);

        Assert.Contains("AddSingleton<ChatViewModel>", text);
        Assert.DoesNotContain("AddTransient<ChatViewModel>", text);
    }

    [Fact]
    public void DependencyInjection_ShouldRegisterChatBoundaryAdaptersAndCoordinators()
    {
        var repoRoot = FindRepoRoot();
        var diFile = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg", "DependencyInjection.cs");
        var text = File.ReadAllText(diFile);

        Assert.Contains("AddSingleton<ISettingsChatConnection>", text);
        Assert.Contains("AddSingleton<IChatLaunchWorkflow>", text);
        Assert.Contains("AddSingleton<IAcpConnectionCommands, AcpChatCoordinator>", text);
        Assert.Contains("AddSingleton<IAcpChatServiceFactory>", text);
        Assert.Contains("AddSingleton<MainNavigationViewModel>(sp =>", text);
        Assert.Contains("AddSingleton<INavigationCoordinator>(sp =>", text);
        Assert.Contains("AddSingleton<AcpConnectionSettingsViewModel>(sp =>", text);
    }

    [Fact]
    public void Xaml_ShouldAvoidLegacySystemControlHighlightBrushes()
    {
        var repoRoot = FindRepoRoot();
        var uiRoot = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg");

        var files = Directory.EnumerateFiles(uiRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

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
            var text = File.ReadAllText(file);
            foreach (var key in forbidden)
            {
                if (text.Contains(key, StringComparison.Ordinal))
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
        var uiRoot = Path.Combine(repoRoot, "SalmonEgg", "SalmonEgg");

        var files = Directory.EnumerateFiles(uiRoot, "*.xaml", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.NotEmpty(files);

        var failures = new List<string>();
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);

            // Enforce "all x:Bind" convention: runtime {Binding ...} is forbidden.
            if (text.Contains("{Binding", StringComparison.Ordinal) ||
                text.Contains("{ Binding", StringComparison.Ordinal) ||
                text.Contains("{binding", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("{ binding", StringComparison.OrdinalIgnoreCase))
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
            var text = File.ReadAllText(file);
            if (!text.Contains("BottomPanelButton.ToolTipService.ToolTip", StringComparison.Ordinal))
            {
                failures.Add($"{file}: missing BottomPanelButton.ToolTipService.ToolTip");
            }

            if (text.Contains("<data name=\"BottomPanelButton.ToolTip\" ", StringComparison.Ordinal))
            {
                failures.Add($"{file}: contains invalid BottomPanelButton.ToolTip key");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
