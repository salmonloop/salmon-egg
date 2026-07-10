using Xunit;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Tests.Architecture;

public sealed class DomainAssemblyBoundaryTests
{
    [Fact]
    public void DomainAssembly_DoesNotExposeHostEndpointOrFileSystemPersistencePolicyTypes()
    {
        var exportedTypeNames = typeof(TransportType).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.DoesNotContain(exportedTypeNames, item => item?.Contains("TransportEndpointAccess", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(exportedTypeNames, item => item?.Contains("FileSystemPersistence", StringComparison.Ordinal) == true);
    }
}
