using SalmonEgg.Presentation.Core.ViewModels.Chat.Selectors;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Selectors;

public sealed class ModelSelectorPolicyTests
{
    [Fact]
    public void Project_WhenNoModelConfig_ReturnsDisabledEmptyProjection()
    {
        var projection = new ModelSelectorPolicy().Project(new ModelSelectorPolicyInput(
            Identity: "profile|conn|cwd|model|0",
            CurrentIdentity: "profile|conn|cwd|model|0",
            ModelOptions: Array.Empty<OptionValueViewModel>(),
            SelectedModelValue: null,
            IsAuthoritative: true,
            IsLoading: false,
            HasError: false,
            Labels: Labels()));

        Assert.Empty(projection.RealItems);
        Assert.Null(projection.Placeholder);
        Assert.False(projection.SelectorEnabled);
    }

    [Fact]
    public void Project_WhenReadyWithModels_UsesRealItemsWithoutPlaceholder()
    {
        var projection = new ModelSelectorPolicy().Project(new ModelSelectorPolicyInput(
            Identity: "profile|conn|cwd|model|2",
            CurrentIdentity: "profile|conn|cwd|model|2",
            ModelOptions:
            [
                Model("claude-haiku", "Haiku"),
                Model("claude-sonnet", "Sonnet")
            ],
            SelectedModelValue: "claude-sonnet",
            IsAuthoritative: true,
            IsLoading: false,
            HasError: false,
            Labels: Labels()));

        Assert.Null(projection.Placeholder);
        Assert.True(projection.SelectorEnabled);
        Assert.Equal("claude-sonnet", projection.SelectedSemanticValue);
        Assert.Equal(ComposerSelectorKind.Model, projection.RealItems[0].Kind);
        Assert.Equal(["claude-haiku", "claude-sonnet"], projection.RealItems.Select(item => item.SemanticValue).ToArray());
    }

    [Fact]
    public void Project_WhenLoadingWithExistingModels_UsesNonBlockingLoadingPlaceholder()
    {
        var projection = new ModelSelectorPolicy().Project(new ModelSelectorPolicyInput(
            Identity: "profile|conn|cwd|model|1",
            CurrentIdentity: "profile|conn|cwd|model|1",
            ModelOptions: [Model("claude-sonnet", "Sonnet")],
            SelectedModelValue: "claude-sonnet",
            IsAuthoritative: false,
            IsLoading: true,
            HasError: false,
            Labels: Labels()));

        Assert.Equal(SelectorPlaceholderKind.Loading, projection.Placeholder!.PlaceholderKind);
        Assert.False(projection.Placeholder.BlocksSubmit);
        Assert.True(projection.ReplaceSelectionWithPlaceholder);
    }

    private static OptionValueViewModel Model(string value, string name)
        => new()
        {
            Value = value,
            Name = name,
            Description = string.Empty
        };

    private static ModelSelectorPlaceholderLabels Labels()
        => new(
            Unresolved: "model-unresolved",
            Loading: "model-loading",
            Error: "model-error");
}
