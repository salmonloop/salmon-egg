using System;
using System.Threading.Tasks;
using SalmonEgg.Acp.Protocol;

namespace SalmonEgg.Acp.Client
{
    /// <summary>
    /// Carries an inbound <c>elicitation/create</c> request together with the callbacks that answer it.
    /// </summary>
    /// <remarks>
    /// Exactly one of the three responders may be invoked per request, mirroring the three actions the
    /// specification allows. Every consumer must be able to answer <c>decline</c> and <c>cancel</c>, since
    /// an Agent must not assume an elicitation succeeds.
    /// </remarks>
    public sealed class ElicitationRequestEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the event payload for an inbound elicitation request.
        /// </summary>
        /// <param name="messageId">The JSON-RPC id the response must echo.</param>
        /// <param name="request">The parsed request.</param>
        /// <param name="accept">Submits accepted content (or no content) for the request.</param>
        /// <param name="decline">Declines the request on the user's behalf.</param>
        /// <param name="cancel">Cancels the request when the user dismisses it.</param>
        public ElicitationRequestEventArgs(
            object messageId,
            CreateElicitationRequest request,
            Func<ElicitationAcceptContent?, Task<bool>> accept,
            Func<Task<bool>> decline,
            Func<Task<bool>> cancel)
        {
            MessageId = messageId ?? throw new ArgumentNullException(nameof(messageId));
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Accept = accept ?? throw new ArgumentNullException(nameof(accept));
            Decline = decline ?? throw new ArgumentNullException(nameof(decline));
            Cancel = cancel ?? throw new ArgumentNullException(nameof(cancel));
        }

        /// <summary>
        /// The JSON-RPC id the response must echo.
        /// </summary>
        public object MessageId { get; }

        /// <summary>
        /// The parsed elicitation request.
        /// </summary>
        public CreateElicitationRequest Request { get; }

        /// <summary>
        /// The session this elicitation belongs to, or <c>null</c> when it is request-scoped.
        /// </summary>
        public string? SessionId => Request.Scope.SessionId;

        /// <summary>
        /// Submits accepted content for the request. Pass <c>null</c> to accept without content, which is
        /// the normal shape for a URL-mode consent.
        /// </summary>
        public Func<ElicitationAcceptContent?, Task<bool>> Accept { get; }

        /// <summary>
        /// Declines the request on the user's behalf.
        /// </summary>
        public Func<Task<bool>> Decline { get; }

        /// <summary>
        /// Cancels the request when the user dismisses it without choosing.
        /// </summary>
        public Func<Task<bool>> Cancel { get; }
    }

    /// <summary>
    /// Carries an inbound <c>elicitation/complete</c> notification.
    /// </summary>
    public sealed class ElicitationCompletedEventArgs : EventArgs
    {
        /// <summary>
        /// Creates the event payload for a completed URL elicitation.
        /// </summary>
        /// <param name="elicitationId">The id of the elicitation that completed.</param>
        public ElicitationCompletedEventArgs(string elicitationId)
        {
            ElicitationId = elicitationId ?? throw new ArgumentNullException(nameof(elicitationId));
        }

        /// <summary>
        /// The id of the elicitation that completed. Opaque to the client.
        /// </summary>
        public string ElicitationId { get; }
    }
}
