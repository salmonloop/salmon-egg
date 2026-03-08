using System.Collections.Generic;

namespace SalmonEgg.Domain.Services.Security
{
    /// <summary>
    /// 路径验证器接口。
    /// 用于验证文件路径的安全性和规范性。
    /// </summary>
    public interface IPathValidator
    {
        /// <summary>
        /// 验证路径是否安全。
        /// 检查路径是否包含遍历攻击模式（如 ".."、"~" 等）。
        /// </summary>
        /// <param name="path">要验证的路径</param>
        /// <returns>如果路径安全返回 true，否则返回 false</returns>
        bool ValidatePath(string path);

        /// <summary>
        /// 获取路径验证的错误列表。
        /// </summary>
        /// <param name="path">要验证的路径</param>
        /// <returns>错误消息列表</returns>
        List<string> GetValidationErrors(string path);

        /// <summary>
        /// 规范化路径。
        /// 将路径转换为标准格式（处理相对路径、多余分隔符等）。
        /// </summary>
        /// <param name="path">要规范化的路径</param>
        /// <returns>规范化后的路径</returns>
        string NormalizePath(string path);

        /// <summary>
        /// 判断路径是否在允许的目录范围内。
        /// </summary>
        /// <param name="path">要检查的路径</param>
        /// <param name="allowedDirectory">允许的根目录</param>
        /// <returns>如果路径在允许范围内返回 true，否则返回 false</returns>
        bool IsPathWithinAllowedDirectory(string path, string allowedDirectory);

        /// <summary>
        /// 判断路径是否为绝对路径。
        /// </summary>
        /// <param name="path">要检查的路径</param>
        /// <returns>如果是绝对路径返回 true，否则返回 false</returns>
        bool IsAbsolutePath(string path);

        /// <summary>
        /// 设置允许的根目录。
        /// </summary>
        /// <param name="allowedDirectory">允许的根目录</param>
        void SetAllowedDirectory(string allowedDirectory);

        /// <summary>
        /// 获取当前允许的根目录。
        /// </summary>
        /// <returns>允许的根目录，如果未设置则返回 null</returns>
        string? GetAllowedDirectory();
    }
}
