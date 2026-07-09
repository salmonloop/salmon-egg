using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Presentation.Services.Cloud;

public sealed class WebDavCloudConfigStorageProvider : IConfigurableCloudConfigStorageProvider
{
    public const string ProviderId = "webdav";
    public const string FileUrlOptionKey = "file_url";
    public const string UsernameOptionKey = "username";
    public const string PasswordSecretKey = "password";

    private const string SecureStoragePasswordKey = "salmonegg/cloud-sync/webdav/password";

    private readonly IAppSettingsService _appSettings;
    private readonly ISecureStorage _secureStorage;
    private readonly HttpClient _httpClient;

    public WebDavCloudConfigStorageProvider(IAppSettingsService appSettings, ISecureStorage secureStorage)
        : this(appSettings, secureStorage, new HttpClient())
    {
    }

    internal WebDavCloudConfigStorageProvider(
        IAppSettingsService appSettings,
        ISecureStorage secureStorage,
        HttpClient httpClient)
    {
        _appSettings = appSettings ?? throw new ArgumentNullException(nameof(appSettings));
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public CloudConfigProviderDescriptor Descriptor => new(ProviderId, "WebDAV", true);

    public async Task<CloudConfigProviderConfigurationResult> ConfigureAsync(
        IReadOnlyDictionary<string, string> options,
        IReadOnlyDictionary<string, string> secrets,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetNormalizedFileUrl(options, out _, out var validationError))
        {
            return CloudConfigProviderConfigurationResult.Failed(validationError);
        }

        var password = GetValue(secrets, PasswordSecretKey);
        if (!string.IsNullOrEmpty(password))
        {
            await _secureStorage.SaveAsync(SecureStoragePasswordKey, password).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(GetValue(options, UsernameOptionKey)) &&
                 string.IsNullOrEmpty(await _secureStorage.LoadAsync(SecureStoragePasswordKey).ConfigureAwait(false)))
        {
            return CloudConfigProviderConfigurationResult.Failed("WebDAV password is required when a username is set.");
        }

        return CloudConfigProviderConfigurationResult.Success();
    }

    public async Task<CloudConfigProviderConfigurationStatus> GetConfigurationStatusAsync(
        IReadOnlyDictionary<string, string> options,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetNormalizedFileUrl(options, out _, out var validationError))
        {
            return CloudConfigProviderConfigurationStatus.Missing(validationError);
        }

        var username = GetValue(options, UsernameOptionKey).Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            return CloudConfigProviderConfigurationStatus.NotRequired();
        }

        var password = await _secureStorage.LoadAsync(SecureStoragePasswordKey).ConfigureAwait(false);
        return string.IsNullOrEmpty(password)
            ? CloudConfigProviderConfigurationStatus.Missing("WebDAV password is required when a username is set.")
            : CloudConfigProviderConfigurationStatus.NotRequired();
    }

    public async Task<CloudConfigAuthorizationResult> EnsureAuthorizedAsync(
        bool interactive,
        CancellationToken cancellationToken = default)
    {
        var configuration = await LoadConfigurationAsync().ConfigureAwait(false);
        if (!configuration.IsConfigured)
        {
            return CloudConfigAuthorizationResult.Failed(configuration.ErrorMessage ?? "WebDAV configuration is incomplete.");
        }

        return CloudConfigAuthorizationResult.Success();
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
        => _secureStorage.DeleteAsync(SecureStoragePasswordKey);

    public async Task<CloudConfigRemoteFile?> TryDownloadAsync(CancellationToken cancellationToken = default)
    {
        var configuration = await LoadConfigurationAsync().ConfigureAwait(false);
        if (!configuration.IsConfigured)
        {
            return null;
        }

        using var request = CreateRequest(HttpMethod.Get, configuration);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "WebDAV download failed with status {0}.",
                    (int)response.StatusCode));
        }

        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        await content.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        return new CloudConfigRemoteFile(
            output.ToArray(),
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified ?? response.Headers.Date);
    }

    public async Task<CloudConfigUploadResult> UploadAsync(
        byte[] content,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));

        var configuration = await LoadConfigurationAsync().ConfigureAwait(false);
        if (!configuration.IsConfigured)
        {
            return CloudConfigUploadResult.Failed(configuration.ErrorMessage ?? "WebDAV configuration is incomplete.");
        }

        try
        {
            using var request = CreateRequest(HttpMethod.Put, configuration);
            request.Content = new ByteArrayContent(content);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            if (!string.IsNullOrWhiteSpace(expectedETag))
            {
                request.Headers.TryAddWithoutValidation("If-Match", expectedETag);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return CloudConfigUploadResult.PreconditionFailed("WebDAV config package changed remotely.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return CloudConfigUploadResult.Failed(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "WebDAV upload failed with status {0}.",
                        (int)response.StatusCode));
            }

            return CloudConfigUploadResult.Uploaded(response.Headers.ETag?.Tag);
        }
        catch (HttpRequestException ex)
        {
            return CloudConfigUploadResult.Failed(ex.Message);
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, WebDavConfiguration configuration)
    {
        var request = new HttpRequestMessage(method, configuration.FileUrl);
        if (!string.IsNullOrWhiteSpace(configuration.Username))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(configuration.Username + ":" + configuration.Password));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        return request;
    }

    private async Task<WebDavConfiguration> LoadConfigurationAsync()
    {
        var settings = await _appSettings.LoadAsync().ConfigureAwait(false);
        if (!settings.CloudConfigSync.ProviderOptions.TryGetValue(ProviderId, out var options))
        {
            return WebDavConfiguration.NotConfigured("WebDAV folder URL is required.");
        }

        if (!TryGetNormalizedFileUrl(options, out var fileUrl, out var validationError))
        {
            return WebDavConfiguration.NotConfigured(validationError);
        }

        var username = GetValue(options, UsernameOptionKey).Trim();
        var password = await _secureStorage.LoadAsync(SecureStoragePasswordKey).ConfigureAwait(false) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(username) && string.IsNullOrEmpty(password))
        {
            return WebDavConfiguration.NotConfigured("WebDAV password is required when a username is set.");
        }

        return new WebDavConfiguration(fileUrl!, username, password, null);
    }

    private static bool TryGetNormalizedFileUrl(
        IReadOnlyDictionary<string, string> options,
        out Uri? fileUrl,
        out string errorMessage)
    {
        fileUrl = null;
        var value = GetValue(options, FileUrlOptionKey).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            errorMessage = "WebDAV folder URL is required.";
            return false;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            errorMessage = "WebDAV folder URL must be an absolute HTTP or HTTPS URL.";
            return false;
        }

        fileUrl = ResolvePackageFileUrl(uri);
        errorMessage = string.Empty;
        return true;
    }

    private static Uri ResolvePackageFileUrl(Uri directoryUrl)
    {
        var lastSegment = directoryUrl.Segments.Length == 0
            ? string.Empty
            : Uri.UnescapeDataString(directoryUrl.Segments[^1].Trim('/'));
        if (string.Equals(lastSegment, CloudConfigSyncDefaults.RemotePackageFileName, StringComparison.OrdinalIgnoreCase))
        {
            return directoryUrl;
        }

        var builder = new UriBuilder(directoryUrl);
        var path = string.IsNullOrEmpty(builder.Path) ? "/" : builder.Path;
        if (!path.EndsWith("/", StringComparison.Ordinal))
        {
            path += "/";
        }

        builder.Path = path + Uri.EscapeDataString(CloudConfigSyncDefaults.RemotePackageFileName);
        return builder.Uri;
    }

    private static string GetValue(IReadOnlyDictionary<string, string> values, string key)
        => values.FirstOrDefault(value => string.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

    private sealed record WebDavConfiguration(Uri? FileUrl, string Username, string Password, string? ErrorMessage)
    {
        public bool IsConfigured => FileUrl is not null && string.IsNullOrEmpty(ErrorMessage);

        public static WebDavConfiguration NotConfigured(string? errorMessage) =>
            new(null, string.Empty, string.Empty, string.IsNullOrWhiteSpace(errorMessage) ? "WebDAV configuration is incomplete." : errorMessage);
    }
}
