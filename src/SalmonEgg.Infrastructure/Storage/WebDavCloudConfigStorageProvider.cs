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

public sealed class WebDavCloudConfigStorageProvider : ICloudConfigStorageProvider
{
    public const string ProviderId = "webdav";
    public const string FileUrlOptionKey = "file_url";
    public const string UsernameOptionKey = "username";
    public const string PasswordSecretKey = "password";

    private const string SecureStoragePasswordKey = CloudConfigSecureStorageKeys.WebDavPassword;
    private static readonly HttpMethod MkColMethod = new("MKCOL");
    private static readonly ProductInfoHeaderValue UserAgent = new("SalmonEgg", "1.0");

    private readonly ISecureStorage _secureStorage;
    private readonly HttpClient _httpClient;

    public WebDavCloudConfigStorageProvider(ISecureStorage secureStorage)
        : this(secureStorage, new HttpClient())
    {
    }

    internal WebDavCloudConfigStorageProvider(
        ISecureStorage secureStorage,
        HttpClient httpClient)
    {
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public CloudConfigProviderDescriptor Descriptor => new(ProviderId, "WebDAV", true);

    public CloudProviderValidationResult Validate(IReadOnlyDictionary<string, string> options)
    {
        if (!TryGetNormalizedFileUrl(options, out _, out _, out var validationError))
        {
            return CloudProviderValidationResult.Failed(validationError);
        }

        return CloudProviderValidationResult.Success();
    }

    public async Task<CloudCredentialInspection> InspectCredentialAsync(
        IReadOnlyDictionary<string, string> options,
        CancellationToken cancellationToken = default)
    {
        if (!Validate(options).Succeeded)
        {
            return new CloudCredentialInspection(CloudCredentialState.Unknown);
        }

        if (string.IsNullOrWhiteSpace(GetValue(options, UsernameOptionKey)))
        {
            return new CloudCredentialInspection(CloudCredentialState.NotRequired);
        }

        try
        {
            var password = await _secureStorage.LoadAsync(SecureStoragePasswordKey).ConfigureAwait(false);
            return new CloudCredentialInspection(
                string.IsNullOrEmpty(password) ? CloudCredentialState.Missing : CloudCredentialState.Available);
        }
        catch (SecureStorageUnavailableException ex)
        {
            return new CloudCredentialInspection(
                CloudCredentialState.StoreUnavailable,
                new CloudSyncFailure(CloudSyncFailureKind.CredentialStoreUnavailable, ex.Message));
        }
    }

    public async Task<CloudProviderSessionResult> CreateSessionAsync(
        IReadOnlyDictionary<string, string> options,
        IReadOnlyDictionary<string, CloudSecretUpdate> secrets,
        bool interactive,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetNormalizedFileUrl(options, out var fileUrl, out var directoryUrl, out var validationError))
        {
            return CloudProviderSessionResult.Failed(
                CloudCredentialState.Unknown,
                new CloudSyncFailure(CloudSyncFailureKind.Validation, validationError));
        }

        var username = GetValue(options, UsernameOptionKey).Trim();
        var update = secrets.TryGetValue(PasswordSecretKey, out var requested)
            ? requested
            : CloudSecretUpdate.KeepExisting();
        var password = update.Kind switch
        {
            CloudSecretUpdateKind.Replace => update.Value ?? string.Empty,
            CloudSecretUpdateKind.Clear => string.Empty,
            _ => await _secureStorage.LoadAsync(SecureStoragePasswordKey).ConfigureAwait(false) ?? string.Empty
        };
        if (!string.IsNullOrWhiteSpace(username) && string.IsNullOrEmpty(password))
        {
            return CloudProviderSessionResult.Failed(
                CloudCredentialState.Missing,
                new CloudSyncFailure(CloudSyncFailureKind.CredentialMissing, "WebDAV password is required when a username is set."));
        }

        var configuration = new WebDavConfiguration(fileUrl, directoryUrl, username, password, null);
        var credential = string.IsNullOrWhiteSpace(username)
            ? CloudCredentialState.NotRequired
            : CloudCredentialState.Available;
        return CloudProviderSessionResult.Success(new Session(this, configuration), credential);
    }

    public async Task<IReadOnlyDictionary<string, CloudSecretUpdate>> ResolveSecretUpdatesAsync(
        IReadOnlyDictionary<string, CloudSecretUpdate> secrets,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var update = secrets.TryGetValue(PasswordSecretKey, out var requested)
            ? requested
            : CloudSecretUpdate.KeepExisting();
        if (update.Kind != CloudSecretUpdateKind.KeepExisting)
        {
            return new Dictionary<string, CloudSecretUpdate>(StringComparer.OrdinalIgnoreCase)
            {
                [PasswordSecretKey] = update
            };
        }

        var password = await _secureStorage.LoadAsync(SecureStoragePasswordKey).ConfigureAwait(false);
        return new Dictionary<string, CloudSecretUpdate>(StringComparer.OrdinalIgnoreCase)
        {
            [PasswordSecretKey] = password is null
                ? CloudSecretUpdate.Clear()
                : CloudSecretUpdate.Replace(password)
        };
    }

    public Task<ICloudSecretUpdateTransaction> BeginSecretUpdateAsync(
        IReadOnlyDictionary<string, CloudSecretUpdate> secrets,
        CancellationToken cancellationToken = default)
    {
        if (!secrets.TryGetValue(PasswordSecretKey, out var update) ||
            update.Kind == CloudSecretUpdateKind.KeepExisting)
        {
            return Task.FromResult(CloudSecretUpdateTransaction.None());
        }

        return CloudSecretUpdateTransaction.BeginAsync(
            _secureStorage,
            new Dictionary<string, CloudSecretUpdate>(StringComparer.Ordinal)
            {
                [SecureStoragePasswordKey] = update
            },
            cancellationToken);
    }

    public Task ForgetCredentialsAsync(CancellationToken cancellationToken = default) =>
        _secureStorage.DeleteAsync(SecureStoragePasswordKey);

    private async Task<CloudConfigRemoteFile?> TryDownloadAsync(
        WebDavConfiguration configuration,
        CancellationToken cancellationToken)
    {
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

    private async Task<CloudConfigUploadResult> UploadAsync(
        WebDavConfiguration configuration,
        byte[] content,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));

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
                    return CloudConfigUploadResult.Failed(
                        new CloudSyncFailure(CloudSyncFailureKind.Network, creation.UserMessage));
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
                        CreateHttpFailure(
                            retryResponse.StatusCode,
                            string.Format(
                            CultureInfo.InvariantCulture,
                            "WebDAV upload failed with status {0}.",
                            (int)retryResponse.StatusCode)));
                }

                return CloudConfigUploadResult.Uploaded(retryResponse.Headers.ETag?.Tag);
            }

            if (!response.IsSuccessStatusCode)
            {
                return CloudConfigUploadResult.Failed(
                    CreateHttpFailure(
                        response.StatusCode,
                        string.Format(
                        CultureInfo.InvariantCulture,
                        "WebDAV upload failed with status {0}.",
                        (int)response.StatusCode)));
            }

            return CloudConfigUploadResult.Uploaded(response.Headers.ETag?.Tag);
        }
        catch (HttpRequestException ex)
        {
            return CloudConfigUploadResult.Failed(
                new CloudSyncFailure(CloudSyncFailureKind.Network, ex.Message));
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
        request.Headers.UserAgent.Add(UserAgent);
        if (!string.IsNullOrWhiteSpace(configuration.Username))
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(configuration.Username + ":" + configuration.Password));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        return request;
    }

    private static CloudSyncFailure CreateHttpFailure(HttpStatusCode statusCode, string message) =>
        new(
            statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                ? CloudSyncFailureKind.Authentication
                : CloudSyncFailureKind.Network,
            message);

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

    private sealed class Session : ICloudConfigStorageSession
    {
        private readonly WebDavCloudConfigStorageProvider _provider;
        private readonly WebDavConfiguration _configuration;

        public Session(WebDavCloudConfigStorageProvider provider, WebDavConfiguration configuration)
        {
            _provider = provider;
            _configuration = configuration;
        }

        public Task<CloudConfigRemoteFile?> TryDownloadAsync(CancellationToken cancellationToken = default) =>
            _provider.TryDownloadAsync(_configuration, cancellationToken);

        public Task<CloudConfigUploadResult> UploadAsync(
            byte[] content,
            string? expectedETag,
            CancellationToken cancellationToken = default) =>
            _provider.UploadAsync(_configuration, content, expectedETag, cancellationToken);
    }

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
