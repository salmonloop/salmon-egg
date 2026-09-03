using System.Text.Json.Serialization.Metadata;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;

namespace SalmonEgg.Acp.Tests;

/// <summary>
/// Contracts for one negotiated protocol version, for tests that assert wire shape.
/// </summary>
/// <remarks>
/// <para>
/// Wire assertions have to name a version. The <c>sessionUpdate</c> vocabularies of v1 and v2 are
/// different sets - v2 adds eight variants and removes three - so "the default context" is not a
/// neutral choice, it is the v1 surface. A test that reaches for it while asserting a v2 shape is not
/// version-agnostic; it is asserting that v1 accepts v2 wire, which is the defect.
/// </para>
/// <para>
/// Use <see cref="V1{T}"/> or <see cref="V2{T}"/> so the version under test is visible at the call
/// site rather than inherited from a default.
/// </para>
/// </remarks>
internal static class Wire
{
    internal static JsonTypeInfo<T> Of<T>(int protocolVersion) =>
        AcpWireFormat.For(protocolVersion).TypeInfo<T>();

    internal static JsonTypeInfo<T> V1<T>() => Of<T>(AcpProtocolVersion.V1);

    internal static JsonTypeInfo<T> V2<T>() => Of<T>(AcpProtocolVersion.V2);
}
