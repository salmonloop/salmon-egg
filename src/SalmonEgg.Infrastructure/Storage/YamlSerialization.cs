using System;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
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
            .WithNodeTypeResolver(new StrictNodeTypeResolver(), s => s.OnTop())
            .Build();

    internal sealed class StrictNodeTypeResolver : INodeTypeResolver
    {
        public bool Resolve(NodeEvent? nodeEvent, ref Type currentType)
        {
            if (nodeEvent?.Tag != null && !nodeEvent.Tag.IsEmpty)
            {
                var tagValue = nodeEvent.Tag.Value;
                if (!tagValue.StartsWith("tag:yaml.org,2002:", StringComparison.Ordinal))
                {
                    throw new YamlException(
                        nodeEvent.Start,
                        nodeEvent.End,
                        $"Insecure deserialization blocked: Unrecognized tag '{tagValue}'");
                }
            }

            return false;
        }
    }
}
