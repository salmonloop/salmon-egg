using System;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

/// <summary>
/// 基于 <see cref="ISecureStorage"/> 的服务器凭据存在性查询。
/// </summary>
/// <remarks>
/// 凭据写入与清除由 <see cref="ConfigurationManager"/> 统一负责，使安全存储中的值与
/// YAML 中的 authentication mode 保持一致。本类型只提供不回显明文的状态查询。
/// </remarks>
public sealed class ServerCredentialService : IServerCredentialService
{
    private readonly ISecureStorage _secureStorage;

    public ServerCredentialService(ISecureStorage secureStorage)
    {
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
    }

    public async Task<ServerCredentialStatus> GetStatusAsync(string serverId)
    {
        ValidateServerId(serverId);

        var token = await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetTokenKey(serverId)).ConfigureAwait(false);
        var apiKey = await _secureStorage.LoadAsync(ConfigurationSecretKeys.GetApiKeyKey(serverId)).ConfigureAwait(false);

        return new ServerCredentialStatus(
            HasToken: !string.IsNullOrEmpty(token),
            HasApiKey: !string.IsNullOrEmpty(apiKey));
    }

    private static void ValidateServerId(string serverId)
    {
        if (string.IsNullOrWhiteSpace(serverId))
        {
            throw new ArgumentException("Server ID cannot be empty", nameof(serverId));
        }
    }
}
