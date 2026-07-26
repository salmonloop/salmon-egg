using System.Linq;

namespace SalmonEgg.Infrastructure.Tests.Architecture;

public sealed class PermissionArchitectureTests
{
    // §5.6 架构回归护栏:legacy 本地权限管理器已被 ACP 协议权限流取代,禁止复活。
    // 用编译产物反射断言类型不存在——类型不存在则任何工程(含 DI)都无法引用它,
    // 不再用源码字符串扫描(注释/重命名即误报,§5.5 禁止)。
    [Fact]
    public void LegacyLocalPermissionAbstractions_DoNotExistInCompiledAssemblies()
    {
        var assemblies = new[]
        {
            typeof(SalmonEgg.Domain.Services.IErrorLogger).Assembly,
            typeof(SalmonEgg.Infrastructure.Services.SessionManager).Assembly,
            typeof(SalmonEgg.Acp.Client.AcpClient).Assembly
        };

        foreach (var assembly in assemblies)
        {
            Assert.DoesNotContain(
                assembly.GetTypes(),
                type => type.Name is "IPermissionManager" or "PermissionManager");
        }
    }
}
