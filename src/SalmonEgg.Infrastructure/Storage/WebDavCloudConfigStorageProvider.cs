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

namespace SalmonEgg.Infrastructure.Storage;

public sealed class WebDavCloudConfigStorageProvider : IConfigurableCloudConfigStorageProvider
{
    public const string ProviderId = "webdav";
    public const string FileUrlOptionKey = "file_url";
    public const string UsernameOptionKey = "username";
    public const string PasswordSecretKey = "password";

    private const string SecureStoragePasswordKey = "salmonegg/cloud-sync/webdav/password";
    private static readonly HttpMethod MkColMethod = new("MKCOL");

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
        if (!TryGetNormalizedFileUrl(options, out _, out _, out var validationError))
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
        if (!TryGetNormalizedFileUrl(options, out _, out _, out var validationError))
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

        await using var content = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var output = new MemoryStream();
        await content.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
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
            using var response = await SendUploadRequestAsync(configuration, content, expectedETag, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return CloudConfigUploadResult.PreconditionFailed("WebDAV config package changed remotely.");
            }

            if (IsMissingCollectionStatus(response.StatusCode))
            {
                var creation = await EnsureRemoteCollectionAsync(configuration, cancellationToken).ConfigureAwait(false);
                if (!creation.Succeeded)
                {
                    return CloudConfigUploadResult.Failed(creation.UserMessage);
                }

                using var retryResponse = await SendUploadRequestAsync(configuration, content, expectedETag, cancellationToken)
                    .ConfigureAwait(false);
                if (retryResponse.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    return CloudConfigUploadResult.PreconditionFailed("WebDAV config package changed remotely.");
                }

                if (!retryResponse.IsSuccessStatusCode)
                {
                    return CloudConfigUploadResult.Failed(
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "WebDAV upload failed with status {0}.",
                            (int)retryResponse.StatusCode));
                }

                return CloudConfigUploadResult.Uploaded(retryResponse.Headers.ETag?.Tag);
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

    private async Task<HttpResponseMessage> SendUploadRequestAsync(
        WebDavConfiguration configuration,
        byte[] content,
        string? expectedETag,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Put, configuration.FileUrl!, configuration);
        request.Content = new ByteArrayContent(content);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        if (!string.IsNullOrWhiteSpace(expectedETag))
        {
            request.Headers.TryAddWithoutValidation("If-Match", expectedETag);
        }

        return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<WebDavCollectionCreationResult> EnsureRemoteCollectionAsync(
        WebDavConfiguration configuration,
        CancellationToken cancellationToken)
    {
        return await EnsureRemoteCollectionAsync(configuration.DirectoryUrl!, configuration, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<WebDavCollectionCreationResult> EnsureRemoteCollectionAsync(
        Uri collectionUrl,
        WebDavConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var creation = await TryCreateCollectionAsync(collectionUrl, configuration, cancellationToken).ConfigureAwait(false);
        if (creation.Status is WebDavCollectionCreationStatus.Created or WebDavCollectionCreationStatus.AlreadyExists)
        {
            return WebDavCollectionCreationResult.Success();
        }

        if (creation.Status != WebDavCollectionCreationStatus.MissingParent)
        {
            return WebDavCollectionCreationResult.Failed(creation.UserMessage);
        }

        var parent = GetParentCollectionUrl(collectionUrl);
        if (parent is null)
        {
            return WebDavCollectionCreationResult.Failed(creation.UserMessage);
        }

        var parentCreation = await EnsureRemoteCollectionAsync(parent, configuration, cancellationToken).ConfigureAwait(false);
        if (!parentCreation.Succeeded)
        {
            return parentCreation;
        }

        creation = await TryCreateCollectionAsync(collectionUrl, configuration, cancellationToken).ConfigureAwait(false);
        return creation.Status is WebDavCollectionCreationStatus.Created or WebDavCollectionCreationStatus.AlreadyExists
            ? WebDavCollectionCreationResult.Success()
            : WebDavCollectionCreationResult.Failed(creation.UserMessage);
    }

    private async Task<WebDavCollectionCreationAttempt> TryCreateCollectionAsync(
        Uri collectionUrl,
        WebDavConfiguration configuration,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(MkColMethod, collectionUrl, configuration);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK or HttpStatusCode.NoContent)
        {
            return WebDavCollectionCreationAttempt.Created();
        }

        if (response.StatusCode == HttpStatusCode.MethodNotAllowed)
        {
            return WebDavCollectionCreationAttempt.AlreadyExists();
        }

        if (IsMissingCollectionStatus(response.StatusCode))
        {
            return WebDavCollectionCreationAttempt.MissingParent(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "WebDAV folder creation failed with status {0}.",
                    (int)response.StatusCode));
        }

        return WebDavCollectionCreationAttempt.Failed(
            string.Format(
                CultureInfo.InvariantCulture,
                "WebDAV folder creation failed with status {0}.",
                (int)response.StatusCode));
    }

    private static bool IsMissingCollectionStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.NotFound or HttpStatusCode.Conflict;

    private HttpRequestMessage CreateRequest(HttpMethod method, WebDavConfiguration configuration)
        => CreateRequest(method, configuration.FileUrl!, configuration);

    private HttpRequestMessage CreateRequest(HttpMethod method, Uri requestUri, WebDavConfiguration configuration)
    {
        var request = new HttpRequestMessage(method, requestUri);
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

        if (!TryGetNormalizedFileUrl(options, out var fileUrl, out var directoryUrl, out var validationError))
        {
            return WebDavConfiguration.NotConfigured(validationError);
        }

        var username = GetValue(options, UsernameOptionKey).Trim();
        var password = await _secureStorage.LoadAsync(SecureStoragePasswordKey).ConfigureAwait(false) ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(username) && string.IsNullOrEmpty(password))
        {
            return WebDavConfiguration.NotConfigured("WebDAV password is required when a username is set.");
        }

        return new WebDavConfiguration(fileUrl!, directoryUrl!, username, password, null);
    }

    private static bool TryGetNormalizedFileUrl(
        IReadOnlyDictionary<string, string> options,
        out Uri? fileUrl,
        out Uri? directoryUrl,
        out string errorMessage)
    {
        fileUrl = null;
        directoryUrl = null;
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

        directoryUrl = ResolveCollectionUrl(uri);
        fileUrl = ResolvePackageFileUrl(directoryUrl);
        errorMessage = string.Empty;
        return true;
    }

    private static Uri ResolveCollectionUrl(Uri uri)
    {
        var lastSegment = uri.Segments.Length == 0
            ? string.Empty
            : Uri.UnescapeDataString(uri.Segments[^1].Trim('/'));
        if (string.Equals(lastSegment, CloudConfigSyncDefaults.RemotePackageFileName, StringComparison.OrdinalIgnoreCase))
        {
            return GetParentCollectionUrl(uri) ?? EnsureTrailingSlash(uri);
        }

        return EnsureTrailingSlash(uri);
    }

    private static Uri ResolvePackageFileUrl(Uri directoryUrl)
    {
        var builder = new UriBuilder(directoryUrl);
        var path = string.IsNullOrEmpty(builder.Path) ? "/" : builder.Path;
        if (!path.EndsWith("/", StringComparison.Ordinal))
        {
            path += "/";
        }

        builder.Path = path + Uri.EscapeDataString(CloudConfigSyncDefaults.RemotePackageFileName);
        return builder.Uri;
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var builder = new UriBuilder(uri);
        var path = string.IsNullOrEmpty(builder.Path) ? "/" : builder.Path;
        if (!path.EndsWith("/", StringComparison.Ordinal))
        {
            builder.Path = path + "/";
        }

        return builder.Uri;
    }

    private static Uri? GetParentCollectionUrl(Uri collectionUrl)
    {
        var builder = new UriBuilder(collectionUrl);
        var path = builder.Path.TrimEnd('/');
        if (string.IsNullOrEmpty(path) || string.Equals(path, "/", StringComparison.Ordinal))
        {
            return null;
        }

        var index = path.LastIndexOf('/');
        builder.Path = index <= 0 ? "/" : path.Substring(0, index + 1);
        return builder.Uri;
    }

    private static string GetValue(IReadOnlyDictionary<string, string> values, string key)
        => values.FirstOrDefault(value => string.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

    private enum WebDavCollectionCreationStatus
    {
        Created,
        AlreadyExists,
        MissingParent,
        Failed
    }

    private sealed record WebDavCollectionCreationAttempt(
        WebDavCollectionCreationStatus Status,
        string UserMessage)
    {
        public static WebDavCollectionCreationAttempt Created() => new(WebDavCollectionCreationStatus.Created, string.Empty);

        public static WebDavCollectionCreationAttempt AlreadyExists() => new(WebDavCollectionCreationStatus.AlreadyExists, string.Empty);

        public static WebDavCollectionCreationAttempt MissingParent(string message) =>
            new(WebDavCollectionCreationStatus.MissingParent, message);

        public static WebDavCollectionCreationAttempt Failed(string message) => new(WebDavCollectionCreationStatus.Failed, message);
    }

    private sealed record WebDavCollectionCreationResult(bool Succeeded, string UserMessage)
    {
        public static WebDavCollectionCreationResult Success() => new(true, string.Empty);

        public static WebDavCollectionCreationResult Failed(string message) => new(false, message);
    }

    private sealed record WebDavConfiguration(Uri? FileUrl, Uri? DirectoryUrl, string Username, string Password, string? ErrorMessage)
    {
        public bool IsConfigured => FileUrl is not null && DirectoryUrl is not null && string.IsNullOrEmpty(ErrorMessage);

        public static WebDavConfiguration NotConfigured(string? errorMessage) =>
            new(
                null,
                null,
                string.Empty,
                string.Empty,
                string.IsNullOrWhiteSpace(errorMessage) ? "WebDAV configuration is incomplete." : errorMessage);
    }
}
