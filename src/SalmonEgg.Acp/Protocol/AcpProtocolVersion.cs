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
        /// </summary>
        public const int Default = V1;

        /// <summary>
        /// Highest protocol version whose wire contracts are modeled by this SDK. This does not
        /// imply that the live client runtime is ready to negotiate that draft version.
        /// </summary>
        public const int Latest = V2;

        public static bool IsSupported(int version)
            => version is V1 or V2;
    }
}
