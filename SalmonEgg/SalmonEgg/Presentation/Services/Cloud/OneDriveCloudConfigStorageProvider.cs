using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Authentication;
using Microsoft.Kiota.Abstractions.Serialization;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Presentation.Services.Cloud;

public sealed class OneDriveCloudConfigStorageProvider : ICloudConfigStorageProvider
{
    public const string ProviderId = "onedrive";

    private static readonly string[] DefaultScopes = ["Files.ReadWrite.AppFolder", "offline_access"];
    private const string RemotePath = "config-sync/salmonegg-config.zip";

    private readonly OneDriveCloudConfigOptions _options;
    private readonly IAppDataService _appData;
    private readonly Lazy<IPublicClientApplication?> _publicClientApplication;
    private GraphServiceClient? _graphClient;
    private bool _cacheRegistered;

    public OneDriveCloudConfigStorageProvider(IAppDataService appData)
        : this(appData, OneDriveCloudConfigOptions.FromAssembly(typeof(OneDriveCloudConfigStorageProvider).Assembly))
    {
    }

    internal OneDriveCloudConfigStorageProvider(IAppDataService appData, OneDriveCloudConfigOptions options)
    {
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _publicClientApplication = new Lazy<IPublicClientApplication?>(CreatePublicClientApplication);
    }

    public CloudConfigProviderDescriptor Descriptor =>
        new(ProviderId, "OneDrive", _options.IsConfigured);

    public async Task<CloudConfigAuthorizationResult> EnsureAuthorizedAsync(
        bool interactive,
        CancellationToken cancellationToken = default)
    {
        var application = _publicClientApplication.Value;
        if (application is null)
        {
            return CloudConfigAuthorizationResult.Failed("OneDrive app registration is not configured.");
        }

        await TryRegisterCacheAsync(application).ConfigureAwait(false);

        try
        {
            if (interactive)
            {
                await application.AcquireTokenInteractive(_options.Scopes).ExecuteAsync(cancellationToken).ConfigureAwait(false);
                EnsureGraphClient(application);
                return CloudConfigAuthorizationResult.Success();
            }

            var account = (await application.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
            if (account is null)
            {
                return CloudConfigAuthorizationResult.InteractionRequired("OneDrive authorization is required.");
            }

            await application.AcquireTokenSilent(_options.Scopes, account).ExecuteAsync(cancellationToken).ConfigureAwait(false);
            EnsureGraphClient(application);
            return CloudConfigAuthorizationResult.Success();
        }
        catch (MsalUiRequiredException)
        {
            return CloudConfigAuthorizationResult.InteractionRequired("OneDrive authorization is required.");
        }
        catch (MsalException ex)
        {
            return CloudConfigAuthorizationResult.Failed(ex.Message);
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        var application = _publicClientApplication.Value;
        if (application is null)
        {
            return;
        }

        foreach (var account in await application.GetAccountsAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await application.RemoveAsync(account).ConfigureAwait(false);
        }
    }

    public async Task<CloudConfigRemoteFile?> TryDownloadAsync(CancellationToken cancellationToken = default)
    {
        var client = GetGraphClient();
        try
        {
            var item = await client.RequestAdapter.SendAsync(
                    CreateRequest(Method.GET, CreateItemUrl()),
                    DriveItem.CreateFromDiscriminatorValue,
                    CreateErrorMapping(),
                    cancellationToken)
                .ConfigureAwait(false);
            if (item is null)
            {
                return null;
            }

            var content = await client.RequestAdapter.SendPrimitiveAsync<Stream>(
                    CreateRequest(Method.GET, CreateContentUrl()),
                    CreateErrorMapping(),
                    cancellationToken)
                .ConfigureAwait(false);
            if (content is null)
            {
                return null;
            }

            using var output = new MemoryStream();
            await content.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
            return new CloudConfigRemoteFile(output.ToArray(), item.ETag, item.LastModifiedDateTime);
        }
        catch (Exception ex) when (IsNotFound(ex))
        {
            return null;
        }
    }

    public async Task<CloudConfigUploadResult> UploadAsync(
        byte[] content,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));

        var client = GetGraphClient();
        try
        {
            using var input = new MemoryStream(content, writable: false);
            var request = CreateRequest(Method.PUT, CreateContentUrl());
            request.SetStreamContent(input, "application/zip");
            if (!string.IsNullOrWhiteSpace(expectedETag))
            {
                request.Headers.Add("If-Match", expectedETag);
            }

            var item = await client.RequestAdapter.SendAsync(
                    request,
                    DriveItem.CreateFromDiscriminatorValue,
                    CreateErrorMapping(),
                    cancellationToken)
                .ConfigureAwait(false);

            return CloudConfigUploadResult.Uploaded(item?.ETag);
        }
        catch (Exception ex) when (IsPreconditionFailed(ex))
        {
            return CloudConfigUploadResult.PreconditionFailed("OneDrive config package changed remotely.");
        }
        catch (Exception ex)
        {
            return CloudConfigUploadResult.Failed(ex.Message);
        }
    }

    private IPublicClientApplication? CreatePublicClientApplication()
    {
        if (!_options.IsConfigured)
        {
            return null;
        }

        var builder = PublicClientApplicationBuilder
            .Create(_options.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, _options.TenantId);

        builder = string.IsNullOrWhiteSpace(_options.RedirectUri)
            ? builder.WithDefaultRedirectUri()
            : builder.WithRedirectUri(_options.RedirectUri);

        return builder.Build();
    }

    private async Task TryRegisterCacheAsync(IPublicClientApplication application)
    {
        if (_cacheRegistered)
        {
            return;
        }

        try
        {
            var cacheDirectory = Path.Combine(_appData.AppDataRootPath, "msal-cache");
            var properties = new StorageCreationPropertiesBuilder("onedrive.msalcache", cacheDirectory).Build();
            var helper = await MsalCacheHelper.CreateAsync(properties).ConfigureAwait(false);
            helper.RegisterCache(application.UserTokenCache);
        }
        catch
        {
            // MSAL still keeps an in-memory token cache when the platform cache helper is unavailable.
        }

        _cacheRegistered = true;
    }

    private GraphServiceClient GetGraphClient()
    {
        var application = _publicClientApplication.Value;
        if (application is null)
        {
            throw new InvalidOperationException("OneDrive app registration is not configured.");
        }

        return EnsureGraphClient(application);
    }

    private GraphServiceClient EnsureGraphClient(IPublicClientApplication application)
    {
        _graphClient ??= new GraphServiceClient(
            new BaseBearerTokenAuthenticationProvider(
                new MsalGraphAccessTokenProvider(application, _options.Scopes)));
        return _graphClient;
    }

    private static bool IsNotFound(Exception ex) =>
        ContainsStatusCode(ex, 404) || ContainsText(ex, "notfound") || ContainsText(ex, "itemNotFound");

    private static bool IsPreconditionFailed(Exception ex) =>
        ContainsStatusCode(ex, 412) || ContainsText(ex, "precondition");

    private static bool ContainsStatusCode(Exception ex, int statusCode) =>
        (ex is ApiException apiException && apiException.ResponseStatusCode == statusCode) ||
        ex.Message.Contains(statusCode.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

    private static bool ContainsText(Exception ex, string value) =>
        ex.Message.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static RequestInformation CreateRequest(Method method, string url) =>
        new()
        {
            HttpMethod = method,
            URI = new Uri(url)
        };

    private static string CreateItemUrl() =>
        "https://graph.microsoft.com/v1.0/me/drive/special/approot:/" + EscapePath(RemotePath);

    private static string CreateContentUrl() => CreateItemUrl() + ":/content";

    private static string EscapePath(string path) =>
        string.Join("/", path.Split('/').Select(Uri.EscapeDataString));

    private static Dictionary<string, ParsableFactory<IParsable>> CreateErrorMapping() => new();

    internal sealed class OneDriveCloudConfigOptions
    {
        public string ClientId { get; init; } = string.Empty;

        public string TenantId { get; init; } = "common";

        public string RedirectUri { get; init; } = string.Empty;

        public string[] Scopes { get; init; } = DefaultScopes;

        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

        public static OneDriveCloudConfigOptions FromAssembly(Assembly assembly)
        {
            if (assembly is null) throw new ArgumentNullException(nameof(assembly));

            var scopes = GetMetadataValue(assembly, "SalmonEgg.OneDrive.Scopes");
            return new OneDriveCloudConfigOptions
            {
                ClientId = GetMetadataValue(assembly, "SalmonEgg.OneDrive.ClientId"),
                TenantId = GetMetadataValue(assembly, "SalmonEgg.OneDrive.TenantId", "common"),
                RedirectUri = GetMetadataValue(assembly, "SalmonEgg.OneDrive.RedirectUri"),
                Scopes = string.IsNullOrWhiteSpace(scopes)
                    ? DefaultScopes
                    : scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            };
        }

        private static string GetMetadataValue(Assembly assembly, string key, string fallback = "")
        {
            foreach (var attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
            {
                if (string.Equals(attribute.Key, key, StringComparison.Ordinal))
                {
                    return string.IsNullOrWhiteSpace(attribute.Value) ? fallback : attribute.Value.Trim();
                }
            }

            return fallback;
        }
    }

    private sealed class MsalGraphAccessTokenProvider : IAccessTokenProvider
    {
        private readonly IPublicClientApplication _application;
        private readonly string[] _scopes;

        public MsalGraphAccessTokenProvider(IPublicClientApplication application, string[] scopes)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        }

        public AllowedHostsValidator AllowedHostsValidator { get; } = new(["graph.microsoft.com"]);

        public async Task<string> GetAuthorizationTokenAsync(
            Uri uri,
            Dictionary<string, object>? additionalAuthenticationContext = null,
            CancellationToken cancellationToken = default)
        {
            var account = (await _application.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
            if (account is null)
            {
                throw new InvalidOperationException("OneDrive authorization is required.");
            }

            var result = await _application.AcquireTokenSilent(_scopes, account).ExecuteAsync(cancellationToken)
                .ConfigureAwait(false);
            return result.AccessToken;
        }
    }
}
