using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SalmonEgg.Domain.Services.Security;

namespace SalmonEgg.Infrastructure.Services.Security
{
    /// <summary>
    /// 路径验证器实现。
    /// 用于验证文件路径的安全性，防止路径遍历攻击。
    /// </summary>
    public class PathValidator : IPathValidator
    {
        private static readonly Regex PathTraversalPattern = new Regex(@"(\.\.|~|\$HOME|\$USER)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private string? _allowedDirectory;

        /// <summary>
        /// 创建新的 PathValidator 实例。
        /// </summary>
        public PathValidator()
        {
        }

        /// <summary>
        /// 创建新的 PathValidator 实例，并设置允许的根目录。
        /// </summary>
        /// <param name="allowedDirectory">允许的根目录</param>
        public PathValidator(string allowedDirectory)
        {
            SetAllowedDirectory(allowedDirectory);
        }

        /// <summary>
        /// 验证路径是否安全。
        /// </summary>
        /// <param name="path">要验证的路径</param>
        /// <returns>如果路径安全返回 true，否则返回 false</returns>
        public bool ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var errors = GetValidationErrors(path);
            return errors.Count == 0;
        }

        /// <summary>
        /// 获取路径验证的错误列表。
        /// </summary>
        /// <param name="path">要验证的路径</param>
        /// <returns>错误消息列表</returns>
        public List<string> GetValidationErrors(string path)
        {
            var errors = new List<string>();

            if (string.IsNullOrWhiteSpace(path))
            {
                errors.Add("路径不能为空");
                return errors;
            }

            // 检查路径遍历模式
            if (PathTraversalPattern.IsMatch(path))
            {
                errors.Add("路径包含不允许的遍历模式（..、~、$HOME 等）");
            }

            // 检查空字节
            if (path.Contains('\0'))
            {
                errors.Add("路径包含空字节");
            }

            // 如果设置了允许目录，检查路径是否在其中
            if (!string.IsNullOrWhiteSpace(_allowedDirectory))
            {
                try
                {
                    var normalizedPath = NormalizePath(path);
                    var normalizedAllowed = NormalizePath(_allowedDirectory);

                    if (!IsPathWithinAllowedDirectory(normalizedPath, normalizedAllowed))
                    {
                        errors.Add($"路径不在允许的目录范围内：{_allowedDirectory}");
                    }
                }
                catch (Exception)
                {
                    errors.Add("路径规范化失败");
                }
            }

            // 检查非法字符（Windows 和 Unix）
            var invalidChars = Path.GetInvalidPathChars();
            foreach (var c in path)
            {
                if (invalidChars.Contains(c))
                {
                    errors.Add($"路径包含非法字符：'{c}'");
                    break;
                }
            }

            // 检查是否尝试访问根目录
            if (path == "/" || path == "\\" || path.StartsWith("./") || path.StartsWith(".\\"))
            {
                // 允许相对路径，但需要进一步检查
                var normalized = NormalizePath(path);
                if (normalized == "/" || normalized == "\\")
                {
                    errors.Add("不允许访问根目录");
                }
            }

            return errors;
        }

        /// <summary>
        /// 规范化路径。
        /// </summary>
        /// <param name="path">要规范化的路径</param>
        /// <returns>规范化后的路径</returns>
        public string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            try
            {
                // 处理相对路径
                if (!Path.IsPathRooted(path))
                {
                    // 如果是相对路径，先转换为绝对路径
                    path = Path.GetFullPath(path);
                }

                // 规范化路径（处理 ..、.、多余的分隔符等）
                return Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"路径规范化失败：{path}", ex);
            }
        }

        /// <summary>
        /// 判断路径是否在允许的目录范围内。
        /// </summary>
        /// <param name="path">要检查的路径</param>
        /// <param name="allowedDirectory">允许的根目录</param>
        /// <returns>如果路径在允许范围内返回 true，否则返回 false</returns>
        public bool IsPathWithinAllowedDirectory(string path, string allowedDirectory)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(allowedDirectory))
            {
                return false;
            }

            try
            {
                var normalizedPath = NormalizePath(path);
                var normalizedAllowed = NormalizePath(allowedDirectory);

                // 确保允许目录以路径分隔符结尾（如果不是根目录）
                var allowedPrefix = normalizedAllowed;
                if (!normalizedAllowed.EndsWith(Path.DirectorySeparatorChar.ToString()) &&
                    !normalizedAllowed.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
                {
                    allowedPrefix += Path.DirectorySeparatorChar;
                }

                // 检查路径是否以允许目录开头
                return normalizedPath.Equals(normalizedAllowed, StringComparison.OrdinalIgnoreCase) ||
                       normalizedPath.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 判断路径是否为绝对路径。
        /// </summary>
        /// <param name="path">要检查的路径</param>
        /// <returns>如果是绝对路径返回 true，否则返回 false</returns>
        public bool IsAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                return Path.IsPathRooted(path);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 设置允许的根目录。
        /// </summary>
        /// <param name="allowedDirectory">允许的根目录</param>
        public void SetAllowedDirectory(string allowedDirectory)
        {
            if (string.IsNullOrWhiteSpace(allowedDirectory))
            {
                _allowedDirectory = null;
                return;
            }

            try
            {
                // 规范化并验证目录路径
                var normalized = NormalizePath(allowedDirectory);

                // 检查目录是否存在（可选，可以根据需求调整）
                if (!Directory.Exists(normalized))
                {
                    // 如果目录不存在，记录警告但不阻止设置
                    // 在实际使用中，可能需要抛出异常或返回错误
                }

                _allowedDirectory = normalized;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"无法设置允许的目录：{allowedDirectory}", ex);
            }
        }

        /// <summary>
        /// 获取当前允许的根目录。
        /// </summary>
        /// <returns>允许的根目录，如果未设置则返回 null</returns>
        public string? GetAllowedDirectory()
        {
            return _allowedDirectory;
        }

        /// <summary>
        /// 验证文件路径并返回规范化路径，如果无效则抛出异常。
        /// </summary>
        /// <param name="path">要验证的路径</param>
        /// <returns>规范化后的路径</returns>
        /// <exception cref="ArgumentException">当路径无效时抛出</exception>
        public string ValidateAndNormalize(string path)
        {
            var errors = GetValidationErrors(path);

            if (errors.Count > 0)
            {
                throw new ArgumentException($"路径验证失败：{string.Join("; ", errors)}");
            }

            return NormalizePath(path);
        }

        /// <summary>
        /// 验证文件路径是否在允许的目录内，并返回规范化路径。
        /// </summary>
        /// <param name="path">要验证的路径</param>
        /// <param name="allowedDirectory">允许的根目录（如果未设置，则使用实例的允许目录）</param>
        /// <returns>规范化后的路径</returns>
        /// <exception cref="ArgumentException">当路径无效时抛出</exception>
        public string ValidateWithinAllowed(string path, string? allowedDirectory = null)
        {
            var directory = allowedDirectory ?? _allowedDirectory;

            if (string.IsNullOrWhiteSpace(directory))
            {
                return ValidateAndNormalize(path);
            }

            var errors = GetValidationErrors(path);

            try
            {
                var normalizedPath = NormalizePath(path);
                var normalizedAllowed = NormalizePath(directory);

                if (!IsPathWithinAllowedDirectory(normalizedPath, normalizedAllowed))
                {
                    errors.Add($"路径不在允许的目录范围内：{directory}");
                }
            }
            catch (Exception)
            {
                errors.Add("路径规范化失败");
            }

            if (errors.Count > 0)
            {
                throw new ArgumentException($"路径验证失败：{string.Join("; ", errors)}");
            }

            return NormalizePath(path);
        }
    }
}
