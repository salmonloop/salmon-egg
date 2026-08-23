using System;

namespace SalmonEgg.Application.Observability;

/// <summary>
/// Application 层拥有的属性键名。
///
/// 归属规则：只定义**本层埋点实际使用**的应用私有键（<c>salmonegg.*</c> 前缀）。
/// - 标准键（<c>exception.*</c> / <c>error.type</c> / <c>http.*</c> / <c>rpc.*</c>）
///   不在此重复定义：异常相关的见 <c>OtelExceptionAttributes</c>，其余由
///   Infrastructure 层的 <c>SemanticConventions</c> 持有。
/// - Session 相关键归 Infrastructure（SessionManager 实现在那一层），此处不再定义，
///   避免同一个键在两层各有一份常量而漂移。
///
/// 应用私有键统一加 <c>salmonegg.</c> 前缀，防止与未来进入规范的标准键冲突
/// （规范明确要求自定义属性不得占用无前缀的通用命名空间）。
/// </summary>
public static class ApplicationSemanticConventions
{
    /// <summary>
    /// OTel GenAI 语义约定中本层实际发射的键。
    ///
    /// 这些是**标准键**（无 <c>salmonegg.</c> 前缀），故与本文件其余部分的私有键
    /// 规则不同：标准键必须一字不差，加前缀反而会让后端的 GenAI 视图认不出来。
    /// 之所以放在本层而非 Infrastructure 的 <c>SemanticConventions</c>：埋点位于
    /// 本层（ChatService），而 Infrastructure 引用 Application，反向引用会成环。
    ///
    /// 稳定性：整个 <c>gen_ai.*</c> 命名空间在规范中仍是 Development
    /// （已迁至独立仓库 open-telemetry/semantic-conventions-genai，尚无 release），
    /// 键名可能变化。故只发**有真实数据源**的键，不为凑齐"看起来完整"而编造。
    /// </summary>
    public static class GenAi
    {
        /// <summary>
        /// 操作名。本层唯一取值是 <c>invoke_agent</c>（见 <see cref="InvokeAgentOperation"/>）。
        /// </summary>
        public const string OperationName = "gen_ai.operation.name";

        /// <summary>Agent 的可读名称，取自 ACP <c>initialize</c> 响应的 <c>agentInfo.name</c>。</summary>
        public const string AgentName = "gen_ai.agent.name";

        /// <summary>Agent 版本，取自 ACP <c>initialize</c> 响应的 <c>agentInfo.version</c>。</summary>
        public const string AgentVersion = "gen_ai.agent.version";

        /// <summary>
        /// 会话标识。用 ACP 的 <c>sessionId</c>——它正是规范所说的
        /// "instrumented library has one readily available"。
        /// </summary>
        /// <remarks>
        /// 规范明文禁止兜底：<i>"a new UUID, a trace identifier, or a hash of request
        /// content SHOULD NOT be used as a fallback value"</i>。因此 sessionId 缺失时
        /// 一律不发该键，不得临时生成。
        ///
        /// 用它而不用 <c>session.id</c>：后者 requirement level 是 **Opt-In**
        /// （"Instrumentation that doesn't support configuration MUST NOT populate
        /// Opt-In attributes"），我们没有对应的用户开关，发了即违规。
        /// </remarks>
        public const string ConversationId = "gen_ai.conversation.id";

        /// <summary><c>gen_ai.operation.name</c> 的枚举取值。</summary>
        public const string InvokeAgentOperation = "invoke_agent";

        /// <summary>
        /// 一次 agent 调用的端到端耗时分布。
        /// </summary>
        /// <remarks>
        /// 单位是**秒**（规范 <c>unit: "s"</c>，值类型 double），不是毫秒。
        /// 属性只要 <c>gen_ai.agent.name</c> + <c>error.type</c>——该指标的属性组是
        /// <c>attributes.gen_ai.error</c> + <c>attributes.gen_ai.invoked_agent.internal.common</c>，
        /// **不含** <c>metric_attributes.gen_ai</c>，所以不要求 <c>gen_ai.provider.name</c>。
        /// 这正是我们能诚实发射它的原因（见 <see cref="OperationName"/> 处的说明）。
        /// </remarks>
        public const string InvokeAgentDurationMetric = "gen_ai.invoke_agent.duration";
    }

    /// <summary>
    /// Chat 相关的应用私有属性。
    /// </summary>
    public static class Chat
    {
        public const string TransportType = "salmonegg.chat.transport_type";
        public const string ServiceType = "salmonegg.chat.service_type";
        [Obsolete("Sensitive chat configuration must not be exported.")]
        public const string Command = "salmonegg.chat.command";
        [Obsolete("Sensitive chat configuration must not be exported.")]
        public const string Url = "salmonegg.chat.url";
        [Obsolete("Profile identity must not be exported.")]
        public const string ProfileId = "salmonegg.chat.profile_id";
    }
}
