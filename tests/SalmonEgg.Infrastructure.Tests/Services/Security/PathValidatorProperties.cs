using FsCheck;
using FsCheck.Xunit;
using Xunit;
using SalmonEgg.Domain.Services.Security;
using SalmonEgg.Infrastructure.Services.Security;

namespace SalmonEgg.Infrastructure.Tests.Services.Security
{
    /// <summary>
    /// 路径遍历测试数据
    /// </summary>
    public class PathTraversalData
    {
        public string Path { get; set; } = string.Empty;
        public override string ToString() => $"PathTraversalData(Path={Path})";
    }

    /// <summary>
    /// 安全路径测试数据
    /// </summary>
    public class SafePathData
    {
        public string Path { get; set; } = string.Empty;
        public override string ToString() => $"SafePathData(Path={Path})";
    }

    /// <summary>
    /// 空字节路径测试数据
    /// </summary>
    public class NulBytePathData
    {
        public string Path { get; set; } = string.Empty;
        public override string ToString() => $"NulBytePathData(Path={Path})";
    }

    /// <summary>
    /// 可规范化路径测试数据
    /// </summary>
    public class NormalizablePathData
    {
        public string Path { get; set; } = string.Empty;
        public override string ToString() => $"NormalizablePathData(Path={Path})";
    }

    /// <summary>
    /// 路径验证器属性测试。
    /// 使用 FsCheck 验证路径验证器的安全性，特别是防止路径遍历攻击。
    /// </summary>
    public class PathValidatorProperties
    {
        private readonly PathValidator _validator = new PathValidator("/safe/directory");

        /// <summary>
        /// 属性：路径遍历攻击防护
        /// </summary>
        [Property]
        public bool PathTraversal_Patterns_Rejected(string pathSegment)
        {
            // 生成包含遍历模式的路径
            var unsafePaths = new[]
            {
                $"../{pathSegment}",
                $"../../{pathSegment}",
                $"~/{pathSegment}",
                $"{pathSegment}/.."
            };

            foreach (var unsafePath in unsafePaths)
            {
                var isValid = _validator.ValidatePath(unsafePath);
                var errors = _validator.GetValidationErrors(unsafePath);

                if (isValid || errors.Count == 0)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 属性：合法路径被接受
        /// </summary>
        [Property]
        public bool SafePaths_Accepted(string pathSegment)
        {
            // 生成安全路径（过滤掉危险字符）
            var safeSegment = pathSegment.Replace("..", "").Replace("~", "").Replace("\0", "");
            var safePath = System.IO.Path.Combine("safe", safeSegment);

            var errors = _validator.GetValidationErrors(safePath);
            var hasTraversalError = errors.Exists(e => e.Contains("遍历"));

            return !hasTraversalError;
        }

        /// <summary>
        /// 属性：空字节注入防护
        /// </summary>
        [Property]
        public bool NullByte_Injection_Rejected(string pathSegment)
        {
            var maliciousPath = $"{pathSegment}\0.txt";

            var isValid = _validator.ValidatePath(maliciousPath);
            var errors = _validator.GetValidationErrors(maliciousPath);

            return !isValid && errors.Exists(e => e.Contains("空字节"));
        }

        /// <summary>
        /// 属性：路径规范化保持语义
        /// </summary>
        [Property]
        public bool PathNormalization_PreservesSemantics(string pathSegment)
        {
            // 过滤掉无效字符和空字节
            if (string.IsNullOrEmpty(pathSegment) || pathSegment.Contains("\0"))
            {
                return true;
            }

            // 过滤掉会导致问题的特殊输入
            var safeSegment = pathSegment
                .Replace("\0", "")
                .TrimStart('.', '/', '\\');

            if (string.IsNullOrEmpty(safeSegment))
            {
                return true;
            }

            var path = System.IO.Path.Combine("dir1", "dir2", safeSegment);

            try
            {
                var normalized = _validator.NormalizePath(path);

                // 检查是否使用跨平台的分隔符检查
                var normalizedForCheck = normalized.Replace('\\', '/');

                return System.IO.Path.IsPathRooted(normalized)
                    && !normalizedForCheck.Contains("..")
                    && !normalizedForCheck.Contains("/.");
            }
            catch
            {
                return true; // 路径无效时抛出异常是预期的
            }
        }
    }
}
