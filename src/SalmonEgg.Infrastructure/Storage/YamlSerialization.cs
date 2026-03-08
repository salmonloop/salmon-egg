using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SalmonEgg.Infrastructure.Storage;

internal static class YamlSerialization
{
    internal static ISerializer CreateSerializer() =>
        new SerializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .DisableAliases()
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();

    internal static IDeserializer CreateDeserializer() =>
        new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
}

