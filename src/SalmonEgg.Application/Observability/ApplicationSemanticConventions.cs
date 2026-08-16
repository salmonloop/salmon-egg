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
