using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace SalmonEgg.Acp.Protocol
{
    /// <summary>
    /// Request parameters for the <c>authenticate</c> method.
    /// Used to initiate an authentication request against the Agent.
    /// </summary>
    public sealed record AuthenticateParams : AcpProtocolObject
    {
        /// <summary>
        /// Agent-advertised authentication method id (from initializeResponse.authMethods[].id in v1 or
        /// initializeResponse.authMethods[].methodId in v2).
        /// </summary>
        [JsonPropertyName("methodId")]
        public required string MethodId { get; init; }

        /// <summary>
        /// Create params for a specific method id.
        /// </summary>
        /// <param name="methodId">Authentication method id</param>
        [JsonConstructor]
        [SetsRequiredMembers]
        public AuthenticateParams(string methodId)
        {
            MethodId = methodId ?? throw new System.ArgumentNullException(nameof(methodId));
        }
    }

    /// <summary>
    /// Response for the <c>authenticate</c> method.
    /// </summary>
    public sealed record AuthenticateResponse : AcpProtocolObject
    {
    }

    /// <summary>
    /// Request parameters for the <c>logout</c> method.
    /// </summary>
    public sealed record LogoutParams : AcpProtocolObject
    {
    }

    /// <summary>
    /// Response for the <c>logout</c> method.
    /// </summary>
    public sealed record LogoutResponse : AcpProtocolObject
    {
        public static readonly LogoutResponse Completed = new();
    }

    /// <summary>
    /// Authentication methods.
    /// </summary>
    public enum AuthMethod
    {
        /// <summary>
        /// Bearer token authentication.
        /// </summary>
        Bearer,

        /// <summary>
        /// API key authentication.
        /// </summary>
        ApiKey,

        /// <summary>
        /// Any other authentication method.
        /// </summary>
        Other
    }
}
