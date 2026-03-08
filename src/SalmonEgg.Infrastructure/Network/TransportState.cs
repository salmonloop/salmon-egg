namespace SalmonEgg.Infrastructure.Network
{
    /// <summary>
    /// Represents the state of a transport connection.
    /// </summary>
    public enum TransportState
    {
        /// <summary>
        /// The transport is disconnected.
        /// </summary>
        Disconnected,

        /// <summary>
        /// The transport is in the process of connecting.
        /// </summary>
        Connecting,

        /// <summary>
        /// The transport is connected and ready to send/receive messages.
        /// </summary>
        Connected,

        /// <summary>
        /// The transport is in the process of disconnecting.
        /// </summary>
        Disconnecting,

        /// <summary>
        /// The transport is in an error state.
        /// </summary>
        Error
    }
}
