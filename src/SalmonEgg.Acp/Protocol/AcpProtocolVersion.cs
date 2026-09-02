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
        [Obsolete(LatestRenamedMessage, DiagnosticId = LatestRenamedDiagnosticId)]
        public const int Latest = HighestModeled;

        /// <summary>
        /// Diagnostic id reported when source references <see cref="Latest"/>. A dedicated id is the
        /// point of this deprecation: CS0618 covers every obsolete member at once, so it can only be
        /// escalated or suppressed wholesale, while this id lets the repository escalate exactly the
        /// "production code must not reference the modeled-version ceiling" rule to an error (see
        /// WarningsAsErrors in Directory.Build.props) without touching any other deprecation.
        /// </summary>
        internal const string LatestRenamedDiagnosticId = "SEACP001";

        /// <summary>
        /// Obsoletion text for <see cref="Latest"/>. Named so the deprecation contract can be
        /// asserted without duplicating the string in tests.
        /// </summary>
        internal const string LatestRenamedMessage =
            "Renamed to AcpProtocolVersion.HighestModeled. 'Latest' reads as 'the version to use', "
            + "but the value is only the highest version whose wire contracts are modeled, and it is "
            + "still a draft that a live client refuses to negotiate. Use AcpProtocolVersion.Default "
            + "for live connections.";

        /// <summary>
        /// The single protocol version a live client actually serves. ACP negotiates one version per
        /// connection and both sides then "act according to its specification", so a client is
        /// compliant while supporting exactly one version: this constant is that version, and it is
        /// the authority both the initialize gate and the post-negotiation check answer to.
        /// </summary>
        /// <remarks>
        /// Distinct from <see cref="HighestModeled"/> on purpose. Wire contracts are modeled ahead of
        /// the runtime so draft shapes can be developed and asserted, and <see cref="IsSupported"/>
        /// stays true for those modeled versions because a parser must keep reading them. Serving a
        /// version is the stronger claim, and only this constant makes it.
        /// </remarks>
        public const int RuntimeServed = V1;

        /// <summary>
        /// Whether the SDK models the wire contracts of a version. Modeling is not serving: see
        /// <see cref="RuntimeServed"/> for the version a live connection will actually run.
        /// </summary>
        public static bool IsSupported(int version)
            => version is V1 or V2;

        /// <summary>
        /// Whether a live client will run this version end to end.
        /// </summary>
        public static bool IsRuntimeServed(int version)
            => version == RuntimeServed;
    }
}
