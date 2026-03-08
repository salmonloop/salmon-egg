using System.Threading.Tasks;

namespace SalmonEgg.Infrastructure.Storage
{
    /// <summary>
    /// 安全存储接口，用于跨平台的敏感数据存储
    /// </summary>
    public interface ISecureStorage
    {
        /// <summary>
        /// 安全保存数据
        /// </summary>
        /// <param name="key">存储键</param>
        /// <param name="value">要保存的值</param>
        Task SaveAsync(string key, string value);

        /// <summary>
        /// 加载安全存储的数据
        /// </summary>
        /// <param name="key">存储键</param>
        /// <returns>存储的值，如果不存在则返回 null</returns>
        Task<string?> LoadAsync(string key);

        /// <summary>
        /// 删除安全存储的数据
        /// </summary>
        /// <param name="key">存储键</param>
        Task DeleteAsync(string key);
    }
}
