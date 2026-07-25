using System;

namespace SalmonEgg.Domain.Services;

/// <summary>
/// 云配置内容寻址 3-way 方向决策的纯结果。
/// Coordinator 只负责取指纹与执行副作用；判定本身无 IO、无时钟。
/// </summary>
public enum CloudSyncContentAction
{
    /// <summary>两侧内容一致：仅刷新本地基线。</summary>
    RefreshBaseline,

    /// <summary>仅本地相对基线变化：上传本地。</summary>
    UploadLocal,

    /// <summary>仅远端相对基线变化，或首次采用 PreferRemote：restore 远端。</summary>
    RestoreRemote,

    /// <summary>真冲突或基线未知且 RequireManual：不覆盖任何一侧。</summary>
    FailClosedConflict
}

/// <summary>
/// 内容 3-way 输入。指纹为规范化内容哈希；空 syncedFingerprint 表示基线未建立。
/// </summary>
public readonly record struct CloudSyncContentDecisionInput(
    string LocalFingerprint,
    string RemoteFingerprint,
    string SyncedFingerprint,
    bool BaselineKnown,
    CloudSyncFirstAdoptPolicy FirstAdoptPolicy);

public readonly record struct CloudSyncContentDecision(
    CloudSyncContentAction Action,
    string Reason);

/// <summary>
/// 内容寻址 3-way 方向判定的唯一纯函数 owner。
/// 禁止引入时钟、ETag 或文件系统；调用方保证指纹已规范化。
/// </summary>
public static class CloudSyncContentDecisionMaker
{
    public static CloudSyncContentDecision Decide(CloudSyncContentDecisionInput input)
    {
        var local = input.LocalFingerprint ?? string.Empty;
        var remote = input.RemoteFingerprint ?? string.Empty;
        var synced = input.SyncedFingerprint ?? string.Empty;

        var localMatchesRemote = string.Equals(local, remote, StringComparison.Ordinal);
        if (localMatchesRemote)
        {
            return new CloudSyncContentDecision(
                CloudSyncContentAction.RefreshBaseline,
                "local and remote content already converge");
        }

        if (input.BaselineKnown)
        {
            var localMatchesSynced = string.Equals(local, synced, StringComparison.Ordinal);
            var remoteMatchesSynced = string.Equals(remote, synced, StringComparison.Ordinal);

            if (!localMatchesSynced && remoteMatchesSynced)
            {
                return new CloudSyncContentDecision(
                    CloudSyncContentAction.UploadLocal,
                    "local dirty against unchanged remote baseline");
            }

            if (localMatchesSynced && !remoteMatchesSynced)
            {
                return new CloudSyncContentDecision(
                    CloudSyncContentAction.RestoreRemote,
                    "remote advanced against clean local baseline");
            }

            // 真冲突：两侧相对基线都变且内容不同。永不静默 LWW。
            return new CloudSyncContentDecision(
                CloudSyncContentAction.FailClosedConflict,
                "true conflict: both sides diverged from synced baseline");
        }

        // 基线未知：首次采用。策略必须显式，禁止时钟启发式。
        return input.FirstAdoptPolicy switch
        {
            CloudSyncFirstAdoptPolicy.PreferRemote => new CloudSyncContentDecision(
                CloudSyncContentAction.RestoreRemote,
                "first adopt with PreferRemote policy"),
            _ => new CloudSyncContentDecision(
                CloudSyncContentAction.FailClosedConflict,
                "first adopt requires manual resolution")
        };
    }
}
