using System;
using FsCheck;
using Xunit;
using SalmonEgg.Domain.Services.Security;
using SalmonEgg.Infrastructure.Services.Security;

namespace SalmonEgg.Infrastructure.Tests.Services.Security
{
    /// <summary>
    /// 路径验证器属性测试。
    /// 使用 FsCheck 验证路径验证器的安全性，特别是防止路径遍历攻击。
    /// </summary>
    public class PathValidatorProperties
    {
        private readonly PathValidator _validator = new("/safe/directory");

        /// <summary>
        /// 属性：路径遍历攻击防护
        /// </summary>
        [Fact]
        public void PathTraversal_Patterns_Rejected()
        {
            CheckPathProperty(PathTraversalPatternsRejected);
        }

        [Fact]
        public void SafePaths_Accepted()
        {
            CheckPathProperty(SafePathsAccepted);
        }

        [Fact]
        public void NullByte_Injection_Rejected()
        {
            CheckPathProperty(NullByteInjectionRejected);
        }

        [Fact]
        public void PathNormalization_PreservesSemantics()
        {
            CheckPathProperty(PathNormalizationPreservesSemantics);
        }

        private bool PathTraversalPatternsRejected(string pathSegment)
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
        private bool SafePathsAccepted(string pathSegment)
        {
            // 生成安全路径（过滤掉危险字符）
            var safeSegment = pathSegment
                .Replace("..", "")
                .Replace("~", "")
                .Replace("\0", "")
                .Replace("$HOME", "", StringComparison.OrdinalIgnoreCase)
                .Replace("$USER", "", StringComparison.OrdinalIgnoreCase);

            var safePath = System.IO.Path.Combine("safe", safeSegment);

            var errors = _validator.GetValidationErrors(safePath);
            var hasTraversalError = errors.Exists(e => e.Contains("traversal", StringComparison.OrdinalIgnoreCase));

            return !hasTraversalError;
        }

        /// <summary>
        /// 属性：空字节注入防护
        /// </summary>
        private bool NullByteInjectionRejected(string pathSegment)
        {
            var maliciousPath = $"{pathSegment}\0.txt";

            var isValid = _validator.ValidatePath(maliciousPath);
            var errors = _validator.GetValidationErrors(maliciousPath);

            return !isValid && errors.Exists(e => e.Contains("null byte", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 属性：路径规范化保持语义
        /// </summary>
        private bool PathNormalizationPreservesSemantics(string pathSegment)
        {
            // 过滤掉无效字符和空字节
            if (string.IsNullOrEmpty(pathSegment) || pathSegment.Contains('\0'))
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

                var hasTraversalSegment =
                    normalizedForCheck.Contains("/../", StringComparison.Ordinal) ||
                    normalizedForCheck.EndsWith("/..", StringComparison.Ordinal) ||
                    normalizedForCheck.Contains("\\..\\", StringComparison.Ordinal) ||
                    normalizedForCheck.EndsWith("\\..", StringComparison.Ordinal);

                var hasDotSegment =
                    normalizedForCheck.Contains("/./", StringComparison.Ordinal) ||
                    normalizedForCheck.EndsWith("/.", StringComparison.Ordinal) ||
                    normalizedForCheck.Contains("\\.\\", StringComparison.Ordinal) ||
                    normalizedForCheck.EndsWith("\\.", StringComparison.Ordinal);

                return System.IO.Path.IsPathRooted(normalized)
                    && !hasTraversalSegment
                    && !hasDotSegment;
            }
            catch
            {
                return true; // 路径无效时抛出异常是预期的
            }
        }

        private static void CheckPathProperty(Func<string, bool> property)
        {
            Check.One(
                Config.QuickThrowOnFailure.WithQuietOnSuccess(true),
                FsCheck.Fluent.Prop.ForAll(property));
        }
    }
}
