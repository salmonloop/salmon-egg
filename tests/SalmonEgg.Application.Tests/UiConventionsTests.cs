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
}
