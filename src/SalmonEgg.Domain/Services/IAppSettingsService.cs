using System;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Services;

public interface IAppSettingsService
{
    /// <summary>
    /// 设置已成功落盘后触发，携带刚写入的快照。
    /// </summary>
    /// <remarks>
    /// 存在意义：运行态（如遥测管线）必须跟随「已持久化」的事实，而 app.yaml 有多个写入方
    /// （设置页保存、云配置恢复）。若让每个写入方各自触发副作用，就会出现多套 owner 且必然
    /// 漏掉其中一条路径；订阅这里则天然覆盖全部写入方。
    ///
    /// 顺序保证：在写入互斥区内按落盘顺序触发，因此订阅方看到的顺序与磁盘最终状态一致，
    /// 不需要自己维护版本号去识别乱序。
    ///
    /// 与 <see cref="IConfigChangeSignal"/> 的区别：那个信号服务于「配置文件被外部改动 →
    /// 重新投影 ViewModel」，且在云同步写回期间会被 <c>Suppress()</c> 以避免同步回环；
    /// 本事件描述的是「我方刚写成功」这一事实，不受该抑制影响——运行态必须跟随磁盘真相，
    /// 无论这次写入来自用户编辑还是云端恢复。
    /// </remarks>
    event EventHandler<AppSettingsSavedEventArgs>? Saved;

    Task<AppSettings> LoadAsync();

    Task SaveAsync(AppSettings settings);
}

/// <summary>
/// 携带刚成功落盘的设置快照。
/// </summary>
public sealed record AppSettingsSavedEventArgs(AppSettings Settings);
