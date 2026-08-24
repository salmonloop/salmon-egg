using SalmonEgg.Infrastructure.Storage;
using Xunit;

namespace SalmonEgg.Infrastructure.Tests.Storage;

/// <summary>
/// Pins the on-disk schema versions to absolute numbers.
/// </summary>
/// <remarks>
/// These numbers are a compatibility contract with files written by other builds, not an
/// implementation detail: the write guards refuse to overwrite a file whose version is newer than
/// the running build supports, so an accidental bump makes the previous release refuse to write back
/// configuration it can otherwise read. Every other schema assertion in the suite either interpolates
/// the constant or echoes a version the test itself wrote, so none of them can see an unintended bump
/// — this fixture is the only tripwire.
///
/// Bumping a version deliberately means updating the number here in the same commit, and adding a
/// load test for the previous version's files: purely additive keys need no migration (see
/// <c>ConfigurationManagerTests.LoadConfigurationAsync_WithSchemaVersion2File_HydratesEmptyEnvironmentWithoutMigration</c>),
/// while a rename or a semantic change does.
/// </remarks>
public sealed class ConfigSchemaVersionContractTests
{
    [Fact]
    public void ServerConfigurationSchemaVersion_IsPinnedToTheCurrentContract()
    {
        Assert.Equal(3, ConfigurationManager.CurrentServerConfigurationSchemaVersion);
    }

    [Fact]
    public void AppSettingsSchemaVersion_IsPinnedToTheCurrentContract()
    {
        Assert.Equal(3, AppSettingsService.CurrentAppSettingsSchemaVersion);
    }
}
