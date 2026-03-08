namespace SalmonEgg.Domain.Models
{
    /// <summary>
    /// Constants for ACP message types.
    /// </summary>
    public static class AcpMessageType
    {
        /// <summary>
        /// Request message type - client sends a request to the server.
        /// Must include Method and optionally Params.
        /// </summary>
        public const string Request = "request";

        /// <summary>
        /// Response message type - server responds to a client request.
        /// Must include either Result or Error (but not both).
        /// </summary>
        public const string Response = "response";

        /// <summary>
        /// Notification message type - one-way message that doesn't expect a response.
        /// Must include Method and optionally Params.
        /// </summary>
        public const string Notification = "notification";

        /// <summary>
        /// Initialize message type - used for protocol initialization and version negotiation.
        /// </summary>
        public const string Initialize = "initialize";

        /// <summary>
        /// Checks if the given message type is valid.
        /// </summary>
        /// <param name="type">The message type to validate.</param>
        /// <returns>True if the type is valid, false otherwise.</returns>
        public static bool IsValid(string type)
        {
            return type == Request 
                || type == Response 
                || type == Notification 
                || type == Initialize;
        }
    }
}
