using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using SalmonEgg.Presentation.Core.Resources;
using SalmonEgg.Presentation.Core.Services.Search;
using SalmonEgg.Presentation.Models.Search;

namespace SalmonEgg.Presentation.Core.Tests.GlobalSearch;

public sealed class DefaultGlobalSearchPipelineTests
{
    [Fact]
    public async Task SearchAsync_DoesNotReturnUnsupportedAnimationCommand()
    {
        var pipeline = new DefaultGlobalSearchPipeline(new PassthroughLocalizer());

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

    private sealed class PassthroughLocalizer : IStringLocalizer<CoreStrings>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] => new(name, name);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];

        public IStringLocalizer WithCulture(CultureInfo culture) => this;
    }
}
