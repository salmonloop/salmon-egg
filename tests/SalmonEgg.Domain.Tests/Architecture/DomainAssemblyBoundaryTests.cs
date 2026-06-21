using NUnit.Framework;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Tests.Architecture;

public sealed class DomainAssemblyBoundaryTests
{
    [Test]
    public void DomainAssembly_DoesNotExposeHostEndpointOrFileSystemPersistencePolicyTypes()
    {
        var exportedTypeNames = typeof(TransportType).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName ?? type.Name)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(
                exportedTypeNames,
                Has.None.Contains("TransportEndpointAccess"),
                "Endpoint reachability depends on the current host runtime and belongs outside the ACP/domain core.");
            Assert.That(
                exportedTypeNames,
                Has.None.Contains("FileSystemPersistence"),
                "Platform-backed file-system synchronization is an infrastructure detail and should not be part of the ACP/domain core.");
        });
    }
}
