using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.Core.Services.Search;
using SalmonEgg.Presentation.Models.Search;

namespace SalmonEgg.Presentation.Core.Tests.GlobalSearch;

public sealed class DefaultGlobalSearchPipelineTests
{
    [Fact]
    public async Task SearchAsync_SettingsAndCommandsUseLocalizedResources()
    {
        var localizer = new TestCoreStringLocalizer();
        var pipeline = new DefaultGlobalSearchPipeline(localizer);

        // Query against English localized copy (and command tag "theme") so the pipeline
        // fixture tracks production CoreStrings.en rather than Chinese-only fixture text.
        var result = await pipeline.SearchAsync(
            "theme",
            new GlobalSearchSourceSnapshot(
                ImmutableArray<GlobalSearchSessionSource>.Empty,
                ImmutableArray<GlobalSearchProjectSource>.Empty),
            CancellationToken.None);

        var items = result.Groups.SelectMany(group => group.Items).ToArray();

        Assert.Contains(
            items,
            item => item.Kind == SearchResultKind.Setting
                && item.Id == "General"
                && item.Title == localizer["SettingsSection_General"]
                && item.Subtitle == localizer["SettingsSearchSubtitle_General"]);
        Assert.Contains(
            items,
            item => item.Kind == SearchResultKind.Command
                && item.Id == "toggle_theme"
                && item.Title == localizer["SearchCommand_ToggleThemeTitle"]
                && item.Subtitle == localizer["SearchCommand_ToggleThemeSubtitle"]);
    }

    [Fact]
    public async Task SearchAsync_DoesNotReturnUnsupportedAnimationCommand()
    {
        var pipeline = new DefaultGlobalSearchPipeline(new TestCoreStringLocalizer());

        var result = await pipeline.SearchAsync(
            "toggle_anim",
            new GlobalSearchSourceSnapshot(
                ImmutableArray<GlobalSearchSessionSource>.Empty,
                ImmutableArray<GlobalSearchProjectSource>.Empty),
            CancellationToken.None);

        Assert.DoesNotContain(
            result.Groups.SelectMany(group => group.Items),
            item => item.Kind == SearchResultKind.Command && item.Id == "toggle_anim");
    }
}
