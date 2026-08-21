using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Infrastructure.Tests.Storage;

public sealed class ConfigurationPackagePathsTests
{
    [Fact]
    public void Normalize_WindowsSeparator_UsesPortableSlash()
    {
        var result = ConfigurationPackagePaths.Normalize(@"servers\agent.yaml");

        Assert.Equal("servers/agent.yaml", result);
    }
}
