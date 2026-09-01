using System;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// ACP major protocol versions modeled by the SDK.
    /// </summary>
    public static class AcpProtocolVersion
    {
        /// <summary>
        /// Stable ACP v1 protocol.
        /// </summary>
        public const int V1 = 1;

        /// <summary>
        /// Draft ACP v2 wire model. Live client support remains disabled until the complete
        /// prompt, update, permission, configuration, and batch lifecycles are implemented.
        /// </summary>
        public const int V2 = 2;

        /// <summary>
        /// Stable protocol used by default for production clients and versionless serialization.
        /// This is the only version a live <see cref="Client.AcpClient"/> will negotiate.
        /// </summary>
        public const int Default = V1;

        /// <summary>
        /// Highest protocol version whose wire contracts are modeled by this SDK. Modeled means the
        /// DTOs and serializers exist for development, not that the live client lifecycle is
        /// complete: initializing a client with this version throws while it is still a draft.
        /// Use <see cref="Default"/> to pick the version a live connection should negotiate.
        /// </summary>
        public const int HighestModeled = V2;

        /// <summary>
        /// Deprecated alias for <see cref="HighestModeled"/>.
        /// </summary>
        [Obsolete(LatestRenamedMessage)]
        public const int Latest = HighestModeled;

        /// <summary>
        /// Obsoletion text for <see cref="Latest"/>. Named so the deprecation contract can be
        /// asserted without duplicating the string in tests.
        /// </summary>
        internal const string LatestRenamedMessage =
            "Renamed to AcpProtocolVersion.HighestModeled. 'Latest' reads as 'the version to use', "
            + "but the value is only the highest version whose wire contracts are modeled, and it is "
            + "still a draft that a live client refuses to negotiate. Use AcpProtocolVersion.Default "
            + "for live connections.";

        public static bool IsSupported(int version)
            => version is V1 or V2;
    }
}
