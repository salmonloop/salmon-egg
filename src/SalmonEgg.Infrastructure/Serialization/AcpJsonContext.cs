using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using SalmonEgg.Domain.Models;
using SalmonEgg.Acp.Content;
using SalmonEgg.Domain.Models.Diagnostics;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Domain.Models.Mcp;
using SalmonEgg.Acp.Plan;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Models.Session;
using SalmonEgg.Acp.Tool;

namespace SalmonEgg.Infrastructure.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(AcpMessage))]
[JsonSerializable(typeof(AcpError))]
[JsonSerializable(typeof(JsonRpcRequest))]
[JsonSerializable(typeof(JsonRpcResponse))]
[JsonSerializable(typeof(JsonRpcNotification))]
[JsonSerializable(typeof(JsonRpcError))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(InitializeResponse))]
[JsonSerializable(typeof(ClientInfo))]
[JsonSerializable(typeof(ClientCapabilities))]
[JsonSerializable(typeof(ClientSessionCapabilities))]
[JsonSerializable(typeof(SessionConfigOptionsCapabilities))]
[JsonSerializable(typeof(FsCapability))]
[JsonSerializable(typeof(AgentInfo))]
[JsonSerializable(typeof(AgentCapabilities))]
[JsonSerializable(typeof(AgentAuthCapabilities))]
[JsonSerializable(typeof(LogoutCapabilities))]
[JsonSerializable(typeof(PromptCapabilities))]
[JsonSerializable(typeof(McpCapabilities))]
[JsonSerializable(typeof(SessionCapabilities))]
[JsonSerializable(typeof(SessionListCapabilities))]
[JsonSerializable(typeof(SessionResumeCapabilities))]
[JsonSerializable(typeof(SessionCloseCapabilities))]
[JsonSerializable(typeof(SessionDeleteCapabilities))]
[JsonSerializable(typeof(SessionAdditionalDirectoriesCapabilities))]
[JsonSerializable(typeof(AuthMethodDefinition))]
[JsonSerializable(typeof(SessionNewParams))]
[JsonSerializable(typeof(SessionNewResponse))]
[JsonSerializable(typeof(SessionModesState))]
[JsonSerializable(typeof(Domain.Models.Protocol.SessionMode))]
[JsonSerializable(typeof(SessionPromptParams))]
[JsonSerializable(typeof(SessionPromptResponse))]
[JsonSerializable(typeof(SessionLoadParams))]
[JsonSerializable(typeof(SessionLoadResponse))]
[JsonSerializable(typeof(SessionResumeParams))]
[JsonSerializable(typeof(SessionResumeResponse))]
[JsonSerializable(typeof(SessionCloseParams))]
[JsonSerializable(typeof(SessionCloseResponse))]
[JsonSerializable(typeof(SessionDeleteParams))]
[JsonSerializable(typeof(SessionDeleteResponse))]
[JsonSerializable(typeof(SessionListParams))]
[JsonSerializable(typeof(SessionListResponse))]
[JsonSerializable(typeof(AgentSessionInfo))]
[JsonSerializable(typeof(SessionSetModeParams))]
[JsonSerializable(typeof(SessionSetModeResponse))]
[JsonSerializable(typeof(SessionSetConfigOptionParams))]
[JsonSerializable(typeof(SessionSetConfigOptionResponse))]
[JsonSerializable(typeof(SessionCancelParams))]
[JsonSerializable(typeof(AuthenticateParams))]
[JsonSerializable(typeof(AuthenticateResponse))]
[JsonSerializable(typeof(LogoutParams))]
[JsonSerializable(typeof(LogoutResponse))]
[JsonSerializable(typeof(AskUserRequest))]
[JsonSerializable(typeof(AskUserQuestion))]
[JsonSerializable(typeof(AskUserOption))]
[JsonSerializable(typeof(AskUserResponse))]
[JsonSerializable(typeof(SessionUpdateParams))]
[JsonSerializable(typeof(SessionUpdate))]
[JsonSerializable(typeof(AgentMessageUpdate))]
[JsonSerializable(typeof(UserMessageUpdate))]
[JsonSerializable(typeof(AgentThoughtUpdate))]
[JsonSerializable(typeof(ToolCallUpdate))]
[JsonSerializable(typeof(ToolCallStatusUpdate))]
[JsonSerializable(typeof(PlanUpdate))]
[JsonSerializable(typeof(CurrentModeUpdate))]
[JsonSerializable(typeof(ConfigUpdateUpdate))]
[JsonSerializable(typeof(ConfigOptionUpdate))]
[JsonSerializable(typeof(SessionInfoUpdate))]
[JsonSerializable(typeof(UsageUpdate))]
[JsonSerializable(typeof(UsageCost))]
[JsonSerializable(typeof(AvailableCommandsUpdate))]
[JsonSerializable(typeof(AvailableCommand))]
[JsonSerializable(typeof(AvailableCommandInput))]
[JsonSerializable(typeof(ConfigOption))]
[JsonSerializable(typeof(ConfigOptionValue))]
[JsonSerializable(typeof(TerminalCreateRequest))]
[JsonSerializable(typeof(TerminalCreateResponse))]
[JsonSerializable(typeof(TerminalOutputRequest))]
[JsonSerializable(typeof(TerminalOutputResponse))]
[JsonSerializable(typeof(TerminalExitStatus))]
[JsonSerializable(typeof(TerminalWaitForExitRequest))]
[JsonSerializable(typeof(TerminalWaitForExitResponse))]
[JsonSerializable(typeof(TerminalKillRequest))]
[JsonSerializable(typeof(TerminalKillResponse))]
[JsonSerializable(typeof(TerminalReleaseRequest))]
[JsonSerializable(typeof(TerminalReleaseResponse))]
[JsonSerializable(typeof(EnvVariable))]
[JsonSerializable(typeof(McpServer))]
[JsonSerializable(typeof(StdioMcpServer))]
[JsonSerializable(typeof(HttpMcpServer))]
[JsonSerializable(typeof(SseMcpServer))]
[JsonSerializable(typeof(McpHttpHeader))]
[JsonSerializable(typeof(McpEnvVariable))]
[JsonSerializable(typeof(ContentBlock))]
[JsonSerializable(typeof(TextContentBlock))]
[JsonSerializable(typeof(ImageContentBlock))]
[JsonSerializable(typeof(AudioContentBlock))]
[JsonSerializable(typeof(ResourceContentBlock))]
[JsonSerializable(typeof(ResourceLinkContentBlock))]
[JsonSerializable(typeof(Annotations))]
[JsonSerializable(typeof(EmbeddedResource))]
[JsonSerializable(typeof(ToolCallContent))]
[JsonSerializable(typeof(ContentToolCallContent))]
[JsonSerializable(typeof(DiffToolCallContent))]
[JsonSerializable(typeof(TerminalToolCallContent))]
[JsonSerializable(typeof(ToolCallLocation))]
[JsonSerializable(typeof(PlanEntry))]
[JsonSerializable(typeof(PlanEntryStatus))]
[JsonSerializable(typeof(PlanEntryPriority))]
[JsonSerializable(typeof(StopReason))]
[JsonSerializable(typeof(ToolCallKind))]
[JsonSerializable(typeof(ToolCallStatus))]
[JsonSerializable(typeof(Dictionary<string, object?>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(long))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(List<ContentBlock>))]
[JsonSerializable(typeof(List<ToolCallContent>))]
[JsonSerializable(typeof(List<ToolCallLocation>))]
[JsonSerializable(typeof(List<Domain.Models.Protocol.SessionMode>))]
[JsonSerializable(typeof(List<ConfigOption>))]
[JsonSerializable(typeof(List<McpServer>))]
[JsonSerializable(typeof(List<AskUserQuestion>))]
[JsonSerializable(typeof(List<PlanEntry>))]
[JsonSerializable(typeof(PermissionOutcomeResult))]
[JsonSerializable(typeof(ReadTextFileResult))]
[JsonSerializable(typeof(DiagnosticsSnapshot))]
internal partial class AcpJsonContext : JsonSerializerContext
{
}

internal sealed class PermissionOutcomeResult
{
    [JsonPropertyName("outcome")]
    public PermissionOutcome Outcome { get; set; } = new();
}

internal sealed class PermissionOutcome
{
    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = string.Empty;

    [JsonPropertyName("optionId")]
    public string? OptionId { get; set; }
}

internal sealed class ReadTextFileResult
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}
