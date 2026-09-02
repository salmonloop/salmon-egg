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
                meta: ClientCapabilityMetadata.CreateDefault())
            {
                // Only form mode is advertised: URL mode obliges the client to open the target in a
                // context the agent's model cannot inspect, and that surface does not exist yet.
                // Advertising it would invite agents to send OAuth flows this client cannot honour, and
                // the specification forbids them from falling back to form mode for those.
                Elicitation = new ElicitationCapabilities
                {
                    Form = new ElicitationFormCapabilities()
                }
            };
    }
}
