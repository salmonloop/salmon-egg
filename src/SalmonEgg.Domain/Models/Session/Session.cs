using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace SalmonEgg.Domain.Models.Session
{
    /// <summary>
    /// 会话聚合。表示与 Agent 的一次完整对话会话，持有会话状态、历史和配置。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本类型自己持有同步：所有可变状态都在私有 <see cref="Lock"/> 下读写，历史列表绝不外泄。
    /// 这样"谁来保护 Session"就有了唯一答案——它自己；调用方无论从哪条线程拿到同一个实例
    /// （<c>GetSession</c> 交出的就是 live 引用），都不需要、也无法再外挂一把锁。
    /// </para>
    /// <para>
    /// 由此本类型刻意<b>不</b>暴露可变的集合或嵌套可变对象：<c>History</c> 与 <c>Mode</c> 只能经
    /// 具名操作读写，读出的一律是快照/深拷贝。否则调用方拿到内部引用后在锁外改动，聚合的同步
    /// 就形同虚设——这正是官方并发准则所说的"不要把内部可变状态暴露给外部同步"。
    /// </para>
    /// <para>
    /// 本类型不参与 JSON 序列化：会话的持久化形态是 <c>ConversationRecord</c>，这里只是进程内的
    /// 运行时投影，因此不带任何序列化契约。
    /// </para>
    /// </remarks>
    public sealed class Session
    {
        private readonly Lock _gate = new();
        private readonly List<SessionUpdateEntry> _history = new();
        private SessionModeState _mode = new();
        private string? _displayName;
        private string _cwd;
        private SessionState _state = SessionState.Active;
        private DateTime _createdAt;
        private DateTime _lastActivityAt;

        /// <summary>
        /// 创建会话实例。
        /// </summary>
        /// <param name="sessionId">会话 ID</param>
        /// <param name="cwd">工作目录</param>
        public Session(string sessionId, string cwd)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
            ArgumentException.ThrowIfNullOrWhiteSpace(cwd);

            SessionId = sessionId;
            _cwd = cwd;
            _createdAt = DateTime.UtcNow;
            _lastActivityAt = _createdAt;
        }

        /// <summary>
        /// 会话的唯一标识符。会话身份在创建时确定且终生不变。
        /// </summary>
        public string SessionId { get; }

        /// <summary>
        /// 会话显示名称缓存。远程 ACP 会话必须由 session metadata title 投影写入。
        /// </summary>
        public string? DisplayName
        {
            get { lock (_gate) { return _displayName; } }
            set { lock (_gate) { _displayName = value; } }
        }

        /// <summary>
        /// 会话的工作目录。
        /// </summary>
        /// <remarks>
        /// ACP 要求 <c>session/new</c>、<c>session/load</c>、<c>session/resume</c> 都携带 cwd，所以一个
        /// 会话不可能没有工作目录——创建时即必填，避免"先建会话、之后再补 cwd"这种信息不全就落地的形态。
        /// 但它可写：协议没有改变既有会话 cwd 的方法，可我们持久化的这份**副本**可能过期或错误，
        /// 而 <c>session/list</c> 报告的是权威值。写入只应表示"采纳权威值纠正本地副本"，
        /// 不表示会话的工作目录真的变了。
        /// </remarks>
        public string Cwd
        {
            get { lock (_gate) { return _cwd; } }
        }

        /// <summary>
        /// 会话的当前状态。
        /// </summary>
        public SessionState State
        {
            get { lock (_gate) { return _state; } }
        }

        /// <summary>
        /// 会话的创建时间。
        /// </summary>
        public DateTime CreatedAt
        {
            get { lock (_gate) { return _createdAt; } }
        }

        /// <summary>
        /// 会话的最后活动时间。
        /// </summary>
        public DateTime LastActivityAt
        {
            get { lock (_gate) { return _lastActivityAt; } }
        }

        /// <summary>
        /// 判断会话是否活跃。
        /// </summary>
        public bool IsActive
        {
            get { lock (_gate) { return IsActiveState(_state); } }
        }

        /// <summary>
        /// 判断会话是否已完成或终止。
        /// </summary>
        public bool IsTerminated
        {
            get { lock (_gate) { return IsTerminatedState(_state); } }
        }

        /// <summary>
        /// 采纳 Agent 报告的权威工作目录，纠正本地副本。
        /// </summary>
        /// <returns>本地副本确实被改写时为 <see langword="true"/>；已与权威值一致则为 <see langword="false"/>。</returns>
        public bool AdoptAuthoritativeCwd(string cwd)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(cwd);

            lock (_gate)
            {
                if (string.Equals(_cwd, cwd, StringComparison.Ordinal))
                {
                    return false;
                }

                _cwd = cwd;
                return true;
            }
        }

        /// <summary>
        /// 设置会话状态。
        /// </summary>
        public void SetState(SessionState state)
        {
            lock (_gate)
            {
                _state = state;
            }
        }

        /// <summary>
        /// 若会话尚未终止则将其置为已取消。判定与写入在同一临界区内完成，
        /// 因此并发取消只会有一方成功。
        /// </summary>
        /// <returns>本次调用完成了取消则为 <see langword="true"/>；会话已终止则为 <see langword="false"/>。</returns>
        public bool TryCancel()
        {
            lock (_gate)
            {
                if (IsTerminatedState(_state))
                {
                    return false;
                }

                _state = SessionState.Cancelled;
                _lastActivityAt = DateTime.UtcNow;
                return true;
            }
        }

        /// <summary>
        /// 恢复会话的时间戳。用于把持久化记录里的时间投影回运行时容器，
        /// 不代表会话此刻有新活动。
        /// </summary>
        public void RestoreTimestamps(DateTime createdAt, DateTime lastActivityAt)
        {
            lock (_gate)
            {
                _createdAt = createdAt;
                _lastActivityAt = lastActivityAt;
            }
        }

        /// <summary>
        /// 更新会话的最后活动时间。
        /// </summary>
        public void UpdateActivity()
        {
            lock (_gate)
            {
                _lastActivityAt = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// 读出会话模式状态的深拷贝。
        /// </summary>
        /// <remarks>
        /// 返回深拷贝而非内部实例：<see cref="SessionModeState"/> 可变，交出引用等于把内部状态
        /// 暴露到锁外，调用方随后的改动既绕过本聚合的同步，也会与并发读者竞争。
        /// </remarks>
        public SessionModeState SnapshotMode()
        {
            lock (_gate)
            {
                return _mode.DeepCopy();
            }
        }

        /// <summary>
        /// 写入会话模式状态。传入 <see langword="null"/> 表示重置为空状态。
        /// </summary>
        /// <remarks>存入深拷贝，避免调用方之后改动自己手上的对象而穿透到聚合内部。</remarks>
        public void SetMode(SessionModeState? mode)
        {
            var stored = mode?.DeepCopy() ?? new SessionModeState();
            lock (_gate)
            {
                _mode = stored;
            }
        }

        /// <summary>
        /// 只切换当前模式 ID，并同步解析出对应的模式对象；可用模式列表保持不变。
        /// </summary>
        public void SetCurrentModeId(string modeId)
        {
            ArgumentNullException.ThrowIfNull(modeId);

            lock (_gate)
            {
                _mode.CurrentModeId = modeId;
                _mode.CurrentMode = _mode.GetModeById(modeId);
                _lastActivityAt = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// 追加一条历史条目，并记为一次会话活动。
        /// </summary>
        public void AppendHistory(SessionUpdateEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            lock (_gate)
            {
                _history.Add(entry);
                _lastActivityAt = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// 拷贝并返回会话历史快照。
        /// </summary>
        /// <remarks>
        /// 调用方永远拿不到 live 列表：否则它一边枚举、追加线程一边写入，就会撞上
        /// 集合被并发修改的异常。
        /// </remarks>
        public IReadOnlyList<SessionUpdateEntry> SnapshotHistory()
        {
            lock (_gate)
            {
                return _history.ToArray();
            }
        }

        /// <summary>
        /// 清空会话历史。用于在 Agent 权威重放（<c>session/load</c> 或 <c>resume + replayFrom</c>）
        /// 之前洗净容器，避免重复条目。
        /// </summary>
        public void ClearHistory()
        {
            lock (_gate)
            {
                _history.Clear();
            }
        }

        /// <summary>
        /// 把会话重置为"新建即活跃、历史为空"的状态，供 <c>session/new</c> 成功后落地。
        /// </summary>
        public void ResetForNewSession()
        {
            lock (_gate)
            {
                _history.Clear();
                _state = SessionState.Active;
            }
        }

        /// <summary>
        /// 用先前捕获的快照整体回滚会话状态、模式与历史。三者一次性写入，
        /// 中途不会被其他线程观察到"改了一半"的会话。
        /// </summary>
        /// <remarks>
        /// 不恢复 cwd：会话的工作目录在创建时确定、协议没有改变它的方法，回滚无从恢复也无需恢复。
        /// 也不改动最后活动时间：回滚是撤销失败操作，不是一次新活动。
        /// </remarks>
        public void RestoreSnapshot(SessionState state, SessionModeState? mode, IReadOnlyList<SessionUpdateEntry> history)
        {
            ArgumentNullException.ThrowIfNull(history);

            var restoredMode = mode?.DeepCopy() ?? new SessionModeState();
            lock (_gate)
            {
                _state = state;
                _mode = restoredMode;
                _history.Clear();
                _history.AddRange(history);
            }
        }

        private static bool IsActiveState(SessionState state)
            => state == SessionState.Active || state == SessionState.Waiting;

        private static bool IsTerminatedState(SessionState state)
            => state == SessionState.Completed
                || state == SessionState.Cancelled
                || state == SessionState.Error;
    }

    /// <summary>
    /// Domain-owned session history entry.
    /// Captures a projection of an ACP session/update for in-process recovery without
    /// retaining protocol wire types in the Domain model.
    /// </summary>
    public sealed class SessionUpdateEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// ACP sessionUpdate discriminator (for example agent_message_chunk, plan).
        /// </summary>
        public string SessionUpdateType { get; set; } = string.Empty;

        /// <summary>
        /// Flattened text for content-bearing updates (agent/user/thought message chunks).
        /// Non-text content is intentionally not retained in Domain history.
        /// </summary>
        public string? TextContent { get; set; }

        /// <summary>
        /// Content block type when TextContent is present (for example text).
        /// </summary>
        public string? ContentType { get; set; }

        public IReadOnlyList<SessionPlanHistoryEntry>? PlanEntries { get; set; }

        public string? ToolCallId { get; set; }

        public string? Title { get; set; }

        /// <summary>
        /// Open ACP tool-call kind wire value.
        /// </summary>
        public string? ToolCallKind { get; set; }

        /// <summary>
        /// Open ACP tool-call status wire value.
        /// </summary>
        public string? ToolCallStatus { get; set; }

        public string? ModeId { get; set; }

        public SessionUpdateEntry()
        {
        }

        public static SessionUpdateEntry CreateTextMessage(string text)
        {
            return new SessionUpdateEntry
            {
                SessionUpdateType = "agent_message_chunk",
                ContentType = "text",
                TextContent = text ?? string.Empty,
                Timestamp = DateTime.UtcNow
            };
        }

        public static SessionUpdateEntry CreatePlan(IEnumerable<SessionPlanHistoryEntry> entries)
        {
            return new SessionUpdateEntry
            {
                SessionUpdateType = "plan",
                PlanEntries = entries?.ToList() ?? new List<SessionPlanHistoryEntry>(),
                Timestamp = DateTime.UtcNow
            };
        }

        public static SessionUpdateEntry CreateModeChange(string modeId)
        {
            return new SessionUpdateEntry
            {
                SessionUpdateType = "current_mode_update",
                ModeId = modeId,
                Timestamp = DateTime.UtcNow
            };
        }
    }

    /// <summary>
    /// Domain projection of a plan entry retained in session history.
    /// Status/priority are open ACP wire strings.
    /// </summary>
    public sealed class SessionPlanHistoryEntry
    {
        public string Content { get; set; } = string.Empty;

        public string Status { get; set; } = "pending";

        public string Priority { get; set; } = "medium";

        public SessionPlanHistoryEntry()
        {
        }

        public SessionPlanHistoryEntry(string content, string? status = null, string? priority = null)
        {
            Content = content ?? string.Empty;
            Status = string.IsNullOrWhiteSpace(status) ? "pending" : status;
            Priority = string.IsNullOrWhiteSpace(priority) ? "medium" : priority;
        }
    }
}
