using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Domain.Services;

/// <summary>
/// 本次安装的稳定标识，用于让遥测能区分「多少台设备在用」而不采集任何个人信息。
/// </summary>
/// <remarks>
/// 对应 OTel 的 <c>app.installation.id</c>（Recommended，故可默认上报，无需用户开关）。
/// 规范对取值的硬约束：跨启动与跨应用升级保持不变；卸载后应改变；**硬件 ID
/// （序列号 / IMEI / MAC）MUST NOT 用作该值**。因此实现只能是持久化的随机 GUID，
/// 不得从机器名、用户名、MAC 等派生。
///
/// 与 <c>service.instance.id</c> 的分工必须分清：后者规范上只保证「同时存在的实例之间
/// 唯一」，SDK 默认每进程随机生成，因此它是**进程/启动**判别符，`count(distinct)` 约等于
/// 启动次数而非设备数。两者都要有，但不可互相顶替。
///
/// 也不要改用 <c>device.id</c>（Opt-In，允许硬件派生，规范自称会招致应用商店拒绝）或
/// <c>host.id</c>（machine-id 类，跨应用共享且卸载不变，恰好破坏上面那条硬件 ID 禁令的目的）。
/// </remarks>
public interface IInstallationIdentityService
{
    /// <summary>
    /// 取得本次安装的标识；首次调用时生成并持久化，之后恒返回同一值。
    /// </summary>
    /// <remarks>
    /// 实现必须容忍持久化失败：遥测是旁路能力，读写标识文件出错只应让本次会话退化为
    /// 无装机标识，不得让启动流程失败。
    /// </remarks>
    Task<string?> GetOrCreateAsync(CancellationToken cancellationToken = default);
}
