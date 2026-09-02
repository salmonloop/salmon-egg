namespace SalmonEgg.Acp
{
    /// <summary>
    /// Single source of truth for the compile-time marking of the ACP v2 draft public surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The v2 wire contracts are modeled ahead of the runtime so draft shapes can be developed and
    /// asserted, but no live client negotiates v2 (see
    /// <see cref="Protocol.AcpProtocolVersion.RuntimeServed"/>). Shipping those contracts as bare
    /// public API would let a consumer build against a protocol the SDK refuses to speak, with no
    /// signal until the connection is refused at runtime, so every draft type carries
    /// <see cref="System.Diagnostics.CodeAnalysis.ExperimentalAttribute"/> keyed by
    /// <see cref="DiagnosticId"/>.
    /// </para>
    /// <para>
    /// The values live here rather than being repeated at each attribute site because three
    /// independent places have to agree on them: the attributes, the two project-level
    /// <c>NoWarn</c> entries that keep the SDK and its tests buildable, and the gate tests that
    /// assert the marking is complete. A literal at each site would let any one of them drift
    /// without breaking a build.
    /// </para>
    /// <para>
    /// Internal on purpose. Consumers suppress the diagnostic by its literal id in <c>NoWarn</c> or
    /// <c>#pragma</c>; exposing a constant for it would add a public API whose only job is to name
    /// a suppression, and that constant would then outlive the draft surface it describes.
    /// </para>
    /// </remarks>
    internal static class AcpDraftProtocol
    {
        /// <summary>
        /// Diagnostic id reported for every use of the v2 draft surface.
        /// </summary>
        /// <remarks>
        /// Deliberately one id for the whole draft surface. Per-family ids would suggest the
        /// families can be adopted independently, but upstream v2 is Draft as a whole and the
        /// client lifecycle is unimplemented as a whole, so "terminal updates are ready but diffs
        /// are not" is not a state that exists. AGENTS.md forbids exactly that illusion of partial
        /// enablement. SEACP001 is taken by the modeled-version ceiling deprecation.
        /// </remarks>
        internal const string DiagnosticId = "SEACP002";

        /// <summary>
        /// Diagnostic text. Replaces the default "for evaluation purposes only" wording, which is
        /// true but omits the one fact a consumer needs: these contracts cannot reach a real Agent
        /// today, because the client will not negotiate v2 at all.
        /// </summary>
        internal const string Message =
            "This is ACP v2 draft surface: the wire contract is modeled, but v2 is still an upstream "
            + "draft and no live SalmonEgg.Acp client negotiates it, so nothing built on this type can "
            + "talk to a real Agent yet. Shapes may change or be removed as upstream v2 settles. See "
            + "https://github.com/salmonloop/salmon-egg/blob/main/src/SalmonEgg.Acp/README.md#acp-v2-draft-surface-seacp002";

        /// <summary>
        /// Documentation link surfaced by IDEs for the diagnostic. Carries no <c>{0}</c> placeholder:
        /// the whole draft surface shares one id, so there is one page to point at.
        /// </summary>
        internal const string UrlFormat =
            "https://github.com/salmonloop/salmon-egg/blob/main/src/SalmonEgg.Acp/README.md#acp-v2-draft-surface-seacp002";
    }
}
