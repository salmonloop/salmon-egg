using System;
using System.Collections.Generic;

namespace SalmonEgg.Domain.Models.Protocol
{
    /// <summary>
    /// Metadata helpers for ACP client capability extensions.
    /// </summary>
    public static class ClientCapabilityMetadata
    {
        public const string ExtensionsMetaKey = "salmonegg/extensions";
        public const string AskUserExtensionMethod = "_interaction.ask_user";
        public const string LegacyAskUserExtensionMethod = "interaction.ask_user";

        /// <summary>
        /// Creates the default extension metadata advertised by SalmonEgg.
        /// </summary>
        public static Dictionary<string, object?> CreateDefault()
            => new(StringComparer.Ordinal)
            {
                [ExtensionsMetaKey] = new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    [AskUserExtensionMethod] = true
                }
            };
    }
}
