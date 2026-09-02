using System.Reflection;
using System.Runtime.CompilerServices;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Content;
using SalmonEgg.Acp.Mcp;
using SalmonEgg.Acp.Tool;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests.Architecture;

public sealed class PublicSurfaceBaselineTests
{
    [Fact]
    public void ExportedTypes_MatchCheckedInManifest()
    {
        var expected = PublicSurfaceManifest.Load().AllTypeNames;

        var actual = typeof(AcpClient).Assembly
            .GetExportedTypes()
            .Where(static type => !type.IsGenericType || type.IsGenericTypeDefinition)
            .Select(static type => (type.FullName ?? type.Name).Replace("+", "/", StringComparison.Ordinal))
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void WireDtos_UseInitOnlyPublicProperties_AndLeafRecordsAreSealed()
    {
        var assembly = typeof(AcpProtocolObject).Assembly;
        var violations = new List<string>();

        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.Namespace is null)
            {
                continue;
            }

            if (!type.Namespace.StartsWith("SalmonEgg.Acp.", StringComparison.Ordinal)
                || type.Namespace.StartsWith("SalmonEgg.Acp.Client", StringComparison.Ordinal)
                || type.Namespace.StartsWith("SalmonEgg.Acp.JsonRpc", StringComparison.Ordinal)
                || type.Namespace.StartsWith("SalmonEgg.Acp.Serialization", StringComparison.Ordinal))
            {
                // Client event args / helpers and serialization entry are not wire DTOs.
                if (type.Namespace is "SalmonEgg.Acp.Client" or "SalmonEgg.Acp.JsonRpc" or "SalmonEgg.Acp.Serialization")
                {
                    continue;
                }
            }

            if (!typeof(AcpProtocolObject).IsAssignableFrom(type) && type != typeof(AcpProtocolObject))
            {
                continue;
            }

            // Polymorphic wire roots must remain unsealed so derived protocol variants can exist;
            // all other concrete DTOs must be sealed.
            var isPolymorphicRoot =
                type == typeof(SessionUpdate)
                || type == typeof(SessionUpdateParams)
                || type == typeof(ContentBlock)
                || type == typeof(ToolCallContent)
                || type == typeof(McpServer)
                || type == typeof(CreateElicitationRequest)
                || type == typeof(CreateElicitationResponse)
                || type == typeof(ElicitationPropertySchema)
                || type == typeof(MultiSelectItems);
            if (type.IsClass && type.IsSealed == false && type.IsAbstract == false && !isPolymorphicRoot)
            {
                violations.Add($"{type.FullName}: expected sealed or abstract record/class");
            }

            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                var setter = property.GetSetMethod();
                if (setter is null)
                {
                    continue;
                }

                var isInit = setter.ReturnParameter
                    .GetRequiredCustomModifiers()
                    .Any(static modifier => modifier.Name == "IsExternalInit");
                if (!isInit)
                {
                    // System.Text.Json requires JsonExtensionData properties to be settable when
                    // polymorphic records use deserialization constructors.
                    if (type == typeof(SessionUpdate) && property.Name == nameof(SessionUpdate.ExtensionData))
                    {
                        continue;
                    }

                    // Optional wire presence flags require settable properties so omitted fields
                    // do not look "present" via record constructor/init defaults.
                    if (type == typeof(SessionInfoUpdate)
                        && property.Name is nameof(SessionInfoUpdate.Title) or nameof(SessionInfoUpdate.UpdatedAt))
                    {
                        continue;
                    }

                    violations.Add($"{type.FullName}.{property.Name}: expected init-only setter");
                }
            }
        }

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void CoreEntryPoints_AreSealedOrPublicAsRequired()
    {
        Assert.True(typeof(AcpClient).IsSealed);
        Assert.True(typeof(AcpException).IsSealed);
        Assert.True(typeof(AcpJsonContext).IsPublic);
        Assert.False(typeof(AcpJsonContext).IsAbstract); // partial concrete
    }
}
