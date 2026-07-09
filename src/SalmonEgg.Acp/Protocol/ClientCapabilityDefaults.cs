namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Default ACP client capabilities shared by initialization entry points.
    /// </summary>
    public static class ClientCapabilityDefaults
    {
        /// <summary>
        /// Creates the default client capabilities advertised by SalmonEgg.
        /// </summary>
        public static ClientCapabilities Create()
            // Keep fs/terminal implementations internal for now and avoid advertising them
            // until the product is ready to expose the UX contract to agents.
            => new(
                session: new ClientSessionCapabilities
                {
                    ConfigOptions = new SessionConfigOptionsCapabilities()
                },
                meta: ClientCapabilityMetadata.CreateDefault());
    }
}
