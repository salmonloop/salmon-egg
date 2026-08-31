using System.Text.Json;
using SalmonEgg.Presentation.ViewModels.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public sealed class ConfigOptionViewModelTests
{
    [Fact]
    public void DisplayValue_UsesEnglishPlaceholdersForUnsetAndBooleans()
    {
        Assert.Equal("Not set", new ConfigOptionViewModel().DisplayValue);
        Assert.Equal("Yes", new ConfigOptionViewModel { Value = true }.DisplayValue);
        Assert.Equal("No", new ConfigOptionViewModel { Value = false }.DisplayValue);
    }

    [Fact]
    public void DisplayValue_UsesEnglishPlaceholdersForJsonKinds()
    {
        using var trueDoc = JsonDocument.Parse("true");
        using var falseDoc = JsonDocument.Parse("false");
        using var arrayDoc = JsonDocument.Parse("[1,2]");
        using var objectDoc = JsonDocument.Parse("{\"a\":1}");

        Assert.Equal("Yes", new ConfigOptionViewModel { Value = trueDoc.RootElement.Clone() }.DisplayValue);
        Assert.Equal("No", new ConfigOptionViewModel { Value = falseDoc.RootElement.Clone() }.DisplayValue);
        Assert.Equal("[Array]", new ConfigOptionViewModel { Value = arrayDoc.RootElement.Clone() }.DisplayValue);
        Assert.Equal("{Object}", new ConfigOptionViewModel { Value = objectDoc.RootElement.Clone() }.DisplayValue);
    }
}
