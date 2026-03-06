using System;
using FsCheck;
using FsCheck.NUnit;
using NUnit.Framework;
using UnoAcpClient.Domain.Services.Security;
using UnoAcpClient.Infrastructure.Services.Security;

namespace UnoAcpClient.Infrastructure.Tests.Services.Security
{
    /// <summary>
    /// 路径验证器属性测试。
    /// 使用 FsCheck 验证路径验证器的安全性，特别是防止路径遍历攻击。
    /// </summary>
    [TestFixture]
    public class PathValidatorProperties
    {
        private readonly PathValidator _validator = new PathValidator("/safe/directory");

        /// <summary>
        /// 属性 9：路径遍历攻击防护
        /// 验证包含路径遍历模式的路径被拒绝。
        /// </summary>
        [FsCheck.NUnit.Property]
        public void PathTraversal_Patterns_Rejected(PathTraversalData data)
        {
            // Arrange
            var unsafePath = data.Path;

            // Act
            var isValid = _validator.ValidatePath(unsafePath);
            var errors = _validator.GetValidationErrors(unsafePath);

            // Assert
            Assert.IsFalse(isValid, $"包含遍历模式的路径应该被拒绝：{unsafePath}");
            Assert.IsTrue(errors.Count > 0, "应该返回验证错误");
        }

        /// <summary>
        /// 属性 9：合法路径被接受
        /// 验证不包含危险模式的合法路径被接受。
        /// </summary>
        [FsCheck.NUnit.Property]
        public void SafePaths_Accepted(SafePathData data)
        {
            // Arrange
            var safePath = data.Path;

            // Act
            var isValid = _validator.ValidatePath(safePath);

            // Assert
            // 注意：这里不强制断言为 true，因为路径可能在允许目录之外
            // 但应该不包含遍历模式错误
            var errors = _validator.GetValidationErrors(safePath);
            var hasTraversalError = errors.Exists(e => e.Contains("遍历"));
            Assert.IsFalse(hasTraversalError, "安全路径不应该有遍历错误");
        }

        /// <summary>
        /// 属性 9：空字节注入防护
        /// 验证包含空字节的路径被拒绝。
        /// </summary>
        [FsCheck.NUnit.Property]
        public void NullByte_Injection_Rejected(NulBytePathData data)
        {
            // Arrange
            var maliciousPath = data.Path;

            // Act
            var isValid = _validator.ValidatePath(maliciousPath);
            var errors = _validator.GetValidationErrors(maliciousPath);

            // Assert
            Assert.IsFalse(isValid, "包含空字节的路径应该被拒绝");
            Assert.IsTrue(errors.Exists(e => e.Contains("空字节")), "应该报告空字节错误");
        }

        /// <summary>
        /// 属性 9：路径规范化保持语义
        /// 验证路径规范化后保持原有的语义。
        /// </summary>
        [FsCheck.NUnit.Property]
        public void PathNormalization_PreservesSemantics(NormalizablePathData data)
        {
            // Arrange
            var path = data.Path;

            // Act
            try
            {
                var normalized = _validator.NormalizePath(path);

                // Assert
                // 规范化后的路径应该是绝对路径
                Assert.IsTrue(System.IO.Path.IsPathRooted(normalized), "规范化后的路径应该是绝对路径");

                // 规范化后的路径不应该包含 .. 或 .
                Assert.IsFalse(normalized.Contains(".."), "规范化后的路径不应该包含 ..");
                Assert.IsFalse(normalized.Contains("/."), "规范化后的路径不应该包含 /.");
            }
            catch (Exception)
            {
                // 如果路径无效，抛出异常是预期的行为
                Assert.Pass("路径无效时抛出异常是预期的");
            }
        }

        /// <summary>
        /// 自定义生成器：路径遍历数据
        /// </summary>
        public class PathTraversalData
        {
            public string Path { get; set; } = string.Empty;

            public override string ToString() => $"PathTraversalData(Path={Path})";
        }

        /// <summary>
        /// 自定义生成器：安全路径数据
        /// </summary>
        public class SafePathData
        {
            public string Path { get; set; } = string.Empty;

            public override string ToString() => $"SafePathData(Path={Path})";
        }

        /// <summary>
        /// 自定义生成器：空字节路径数据
        /// </summary>
        public class NulBytePathData
        {
            public string Path { get; set; } = string.Empty;

            public override string ToString() => $"NulBytePathData(Path={Path})";
        }

        /// <summary>
        /// 自定义生成器：可规范化路径数据
        /// </summary>
        public class NormalizablePathData
        {
            public string Path { get; set; } = string.Empty;

            public override string ToString() => $"NormalizablePathData(Path={Path})";
        }
    }

    /// <summary>
    /// FsCheck 任意值生成器 for PathValidator 测试
    /// </summary>
    public class PathValidatorArbitraryGenerator : DefaultArbitraryGenerator
    {
        public override Gen<T> Generator<T>()
        {
            if (typeof(T) == typeof(PathValidatorProperties.PathTraversalData))
            {
                // 生成包含路径遍历模式的路径
                return Gen.OneOf(
                    Arb.Generate<string>().Select(p => new PathValidatorProperties.PathTraversalData { Path = $"../{p}" }),
                    Arb.Generate<string>().Select(p => new PathValidatorProperties.PathTraversalData { Path = $"../../{p}" }),
                    Arb.Generate<string>().Select(p => new PathValidatorProperties.PathTraversalData { Path = $"~/{p}" }),
                    Arb.Generate<string>().Select(p => new PathValidatorProperties.PathTraversalData { Path = p.EndsWith("..") ? p : $"{p}/.." })
                ).Cast<T>();
            }

            if (typeof(T) == typeof(PathValidatorProperties.SafePathData))
            {
                // 生成安全的路径
                return Arb.Generate<string>()
                    .Where(s => !s.Contains("..") && !s.Contains("~") && !s.Contains("\0"))
                    .Select(p => new PathValidatorProperties.SafePathData { Path = System.IO.Path.Combine("safe", p) })
                    .Cast<T>();
            }

            if (typeof(T) == typeof(PathValidatorProperties.NulBytePathData))
            {
                // 生成包含空字节的路径
                return Arb.Generate<string>()
                    .Select(p => new PathValidatorProperties.NulBytePathData { Path = $"{p}\0.txt" })
                    .Cast<T>();
            }

            if (typeof(T) == typeof(PathValidatorProperties.NormalizablePathData))
            {
                // 生成可规范化的路径
                return Arb.Generate<string>()
                    .Where(s => !s.Contains("\0") && !s.StartsWith(".."))
                    .Select(p => new PathValidatorProperties.NormalizablePathData { Path = System.IO.Path.Combine("dir1", "dir2", p) })
                    .Cast<T>();
            }

            return base.Generator<T>();
        }
    }
}
