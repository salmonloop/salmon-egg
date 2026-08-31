using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SalmonEgg.Cli.Hosting;

/// <summary>
/// Source-generated serializer for the environment payload printed by <see cref="CliPrintEnvironment"/>.
/// </summary>
/// <remarks>
/// Not indented: the payload is machine-read from a pipe, and keeping it on one line means a shell that
/// interleaves its own output cannot split the JSON across the marker boundary.
///
/// Names are emitted verbatim. These are environment variable names, not model properties — a naming
/// policy would rewrite <c>PATH</c> into something the reader does not expect.
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class CliPrintEnvironmentJson : JsonSerializerContext
{
}
