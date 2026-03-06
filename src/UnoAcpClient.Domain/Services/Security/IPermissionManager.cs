using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace UnoAcpClient.Domain.Services.Security
{
    /// <summary>
    /// 权限管理器接口。
    /// 用于管理权限请求、授予和检查。
    /// </summary>
    public interface IPermissionManager
    {
        /// <summary>
        /// 请求权限。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="operation">请求的操作</param>
        /// <param name="path">相关的路径（可选）</param>
        /// <param name="options">可用的权限选项</param>
        /// <returns>权限请求结果</returns>
        Task<PermissionResponse> RequestPermissionAsync(string sessionId, string operation, string? path = null, List<PermissionOption>? options = null);

        /// <summary>
        /// 判断是否允许执行指定操作。
        /// </summary>
        /// <param name="operation">操作名称</param>
        /// <param name="path">相关的路径（可选）</param>
        /// <returns>如果允许返回 true，否则返回 false</returns>
        bool IsOperationAllowed(string operation, string? path = null);

        /// <summary>
        /// 授予权限。
        /// </summary>
        /// <param name="operation">操作名称</param>
        /// <param name="path">相关的路径（可选）</param>
        /// <param name="duration">授权持续时间（可选）</param>
        void GrantPermission(string operation, string? path = null, TimeSpan? duration = null);

        /// <summary>
        /// 撤销权限。
        /// </summary>
        /// <param name="operation">操作名称</param>
        /// <param name="path">相关的路径（可选）</param>
        void RevokePermission(string operation, string? path = null);

        /// <summary>
        /// 清除所有权限。
        /// </summary>
        void ClearAllPermissions();

        /// <summary>
        /// 获取当前所有的权限配置。
        /// </summary>
        /// <returns>权限配置列表</returns>
        IEnumerable<PermissionEntry> GetAllPermissions();
    }

    /// <summary>
    /// 权限响应类。
    /// </summary>
    public class PermissionResponse
    {
        /// <summary>
        /// 结果类型（selected, cancelled, denied 等）。
        /// </summary>
        public string Outcome { get; set; } = string.Empty;

        /// <summary>
        /// 选中的选项 ID（如果适用）。
        /// </summary>
        public string? OptionId { get; set; }

        /// <summary>
        /// 是否被允许。
        /// </summary>
        public bool IsAllowed => Outcome == "selected" || Outcome == "allowed";

        /// <summary>
        /// 创建新的权限响应。
        /// </summary>
        public PermissionResponse()
        {
        }

        /// <summary>
        /// 创建新的权限响应。
        /// </summary>
        /// <param name="outcome">结果类型</param>
        /// <param name="optionId">选中的选项 ID</param>
        public PermissionResponse(string outcome, string? optionId = null)
        {
            Outcome = outcome;
            OptionId = optionId;
        }

        /// <summary>
        /// 创建选择的响应。
        /// </summary>
        /// <param name="optionId">选中的选项 ID</param>
        /// <returns>选择的响应</returns>
        public static PermissionResponse Selected(string? optionId = null)
        {
            return new PermissionResponse("selected", optionId);
        }

        /// <summary>
        /// 创建拒绝的响应。
        /// </summary>
        /// <returns>拒绝的响应</returns>
        public static PermissionResponse Denied()
        {
            return new PermissionResponse("denied");
        }

        /// <summary>
        /// 创建取消的响应。
        /// </summary>
        /// <returns>取消的响应</returns>
        public static PermissionResponse Cancelled()
        {
            return new PermissionResponse("cancelled");
        }
    }

    /// <summary>
    /// 权限选项类。
    /// </summary>
    public class PermissionOption
    {
        /// <summary>
        /// 选项 ID。
        /// </summary>
        public string OptionId { get; set; } = string.Empty;

        /// <summary>
        /// 选项显示名称。
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 选项类型（例如 "allow", "deny", "ask_always", "ask_never"）。
        /// </summary>
        public string Kind { get; set; } = string.Empty;

        /// <summary>
        /// 选项描述。
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 创建新的权限选项。
        /// </summary>
        public PermissionOption()
        {
        }

        /// <summary>
        /// 创建新的权限选项。
        /// </summary>
        /// <param name="optionId">选项 ID</param>
        /// <param name="name">显示名称</param>
        /// <param name="kind">类型</param>
        /// <param name="description">描述</param>
        public PermissionOption(string optionId, string name, string kind, string? description = null)
        {
            OptionId = optionId;
            Name = name;
            Kind = kind;
            Description = description;
        }
    }

    /// <summary>
    /// 权限配置条目类。
    /// </summary>
    public class PermissionEntry
    {
        /// <summary>
        /// 操作名称。
        /// </summary>
        public string Operation { get; set; } = string.Empty;

        /// <summary>
        /// 相关的路径（可选）。
        /// </summary>
        public string? Path { get; set; }

        /// <summary>
        /// 是否允许。
        /// </summary>
        public bool IsAllowed { get; set; }

        /// <summary>
        /// 过期时间（可选）。
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        /// <summary>
        /// 创建新的权限条目。
        /// </summary>
        public PermissionEntry()
        {
        }

        /// <summary>
        /// 创建新的权限条目。
        /// </summary>
        /// <param name="operation">操作名称</param>
        /// <param name="path">路径</param>
        /// <param name="isAllowed">是否允许</param>
        /// <param name="expiresAt">过期时间</param>
        public PermissionEntry(string operation, string? path = null, bool isAllowed = true, DateTime? expiresAt = null)
        {
            Operation = operation;
            Path = path;
            IsAllowed = isAllowed;
            ExpiresAt = expiresAt;
        }

        /// <summary>
        /// 判断权限是否已过期。
        /// </summary>
        public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;
    }
}
