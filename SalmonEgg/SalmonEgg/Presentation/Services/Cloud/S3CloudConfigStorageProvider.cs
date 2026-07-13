using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;

namespace SalmonEgg.Presentation.Services.Cloud;

public sealed class S3CloudConfigStorageProvider : ICloudConfigStorageProvider
{
    public const string ProviderId = "s3";
    public const string EndpointOptionKey = "endpoint";
    public const string BucketOptionKey = "bucket";
    public const string RegionOptionKey = "region";
    public const string ObjectKeyOptionKey = "object_key";
    public const string ForcePathStyleOptionKey = "force_path_style";
    public const string AccessKeyIdSecretKey = "access_key_id";
    public const string SecretAccessKeySecretKey = "secret_access_key";

    private const string SecureStorageAccessKeyIdKey = "salmonegg/cloud-sync/s3/access-key-id";
    private const string SecureStorageSecretAccessKeyKey = "salmonegg/cloud-sync/s3/secret-access-key";
    private const string DefaultRegion = "us-east-1";
    private const string DefaultObjectKey = CloudConfigSyncDefaults.RemotePackagePath;
    private const string ServiceName = "s3";
    private const string EmptyPayloadHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private readonly ISecureStorage _secureStorage;
    private readonly HttpClient _httpClient;

    public S3CloudConfigStorageProvider(ISecureStorage secureStorage)
        : this(secureStorage, new HttpClient())
    {
    }

    internal S3CloudConfigStorageProvider(
        ISecureStorage secureStorage,
        HttpClient httpClient)
    {
        _secureStorage = secureStorage ?? throw new ArgumentNullException(nameof(secureStorage));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public CloudConfigProviderDescriptor Descriptor => new(ProviderId, "S3 compatible", true);

    public CloudProviderValidationResult Validate(IReadOnlyDictionary<string, string> options)
    {
        if (!TryCreateConfiguration(options, accessKeyId: string.Empty, secretAccessKey: string.Empty, out _, out var validationError))
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

        try
        {
            var accessKeyId = await _secureStorage.LoadAsync(SecureStorageAccessKeyIdKey).ConfigureAwait(false);
            var secretAccessKey = await _secureStorage.LoadAsync(SecureStorageSecretAccessKeyKey).ConfigureAwait(false);
            return new CloudCredentialInspection(
                string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrEmpty(secretAccessKey)
                    ? CloudCredentialState.Missing
                    : CloudCredentialState.Available);
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
        var accessKeyId = await ResolveSecretAsync(secrets, AccessKeyIdSecretKey, SecureStorageAccessKeyIdKey).ConfigureAwait(false);
        var secretAccessKey = await ResolveSecretAsync(secrets, SecretAccessKeySecretKey, SecureStorageSecretAccessKeyKey).ConfigureAwait(false);
        if (!TryCreateConfiguration(options, accessKeyId, secretAccessKey, out var configuration, out var validationError))
        {
            return CloudProviderSessionResult.Failed(
                CloudCredentialState.Unknown,
                new CloudSyncFailure(CloudSyncFailureKind.Validation, validationError));
        }

        if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrEmpty(secretAccessKey))
        {
            return CloudProviderSessionResult.Failed(
                CloudCredentialState.Missing,
                new CloudSyncFailure(CloudSyncFailureKind.CredentialMissing, "S3 access key ID and secret access key are required."));
        }

        return CloudProviderSessionResult.Success(new Session(this, configuration!), CloudCredentialState.Available);
    }

    public async Task CommitSecretsAsync(
        IReadOnlyDictionary<string, CloudSecretUpdate> secrets,
        CancellationToken cancellationToken = default)
    {
        await CommitSecretAsync(secrets, AccessKeyIdSecretKey, SecureStorageAccessKeyIdKey).ConfigureAwait(false);
        await CommitSecretAsync(secrets, SecretAccessKeySecretKey, SecureStorageSecretAccessKeyKey).ConfigureAwait(false);
    }

    public async Task ForgetCredentialsAsync(CancellationToken cancellationToken = default)
    {
        await _secureStorage.DeleteAsync(SecureStorageAccessKeyIdKey).ConfigureAwait(false);
        await _secureStorage.DeleteAsync(SecureStorageSecretAccessKeyKey).ConfigureAwait(false);
    }

    private async Task<CloudConfigRemoteFile?> TryDownloadAsync(
        S3Configuration configuration,
        CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, configuration, payloadHash: EmptyPayloadHash);
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
                    "S3 download failed with status {0}.",
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

    private async Task<CloudConfigUploadResult> UploadAsync(
        S3Configuration configuration,
        byte[] content,
        string? expectedETag,
        CancellationToken cancellationToken = default)
    {
        if (content is null) throw new ArgumentNullException(nameof(content));

        try
        {
            var payloadHash = ComputeSha256Hex(content);
            using var request = CreateRequest(HttpMethod.Put, configuration, payloadHash);
            request.Content = new ByteArrayContent(content);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            if (!string.IsNullOrWhiteSpace(expectedETag))
            {
                request.Headers.TryAddWithoutValidation("If-Match", expectedETag);
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.PreconditionFailed)
            {
                return CloudConfigUploadResult.PreconditionFailed("S3 config package changed remotely.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return CloudConfigUploadResult.Failed(
                    new CloudSyncFailure(
                        response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                            ? CloudSyncFailureKind.Authentication
                            : CloudSyncFailureKind.Network,
                        string.Format(
                        CultureInfo.InvariantCulture,
                        "S3 upload failed with status {0}.",
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

    private HttpRequestMessage CreateRequest(HttpMethod method, S3Configuration configuration, string payloadHash)
    {
        var now = DateTimeOffset.UtcNow;
        var request = new HttpRequestMessage(method, configuration.ObjectUri);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("x-amz-date", now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture));
        request.Headers.Host = configuration.ObjectUri.Authority;
        request.Headers.Authorization = CreateAuthorizationHeader(request, configuration, now, payloadHash);
        return request;
    }

    private async Task<string> ResolveSecretAsync(
        IReadOnlyDictionary<string, CloudSecretUpdate> secrets,
        string secretName,
        string storageKey)
    {
        if (!secrets.TryGetValue(secretName, out var update) || update.Kind == CloudSecretUpdateKind.KeepExisting)
        {
            return await _secureStorage.LoadAsync(storageKey).ConfigureAwait(false) ?? string.Empty;
        }

        return update.Kind == CloudSecretUpdateKind.Clear ? string.Empty : update.Value ?? string.Empty;
    }

    private async Task CommitSecretAsync(
        IReadOnlyDictionary<string, CloudSecretUpdate> secrets,
        string secretName,
        string storageKey)
    {
        if (!secrets.TryGetValue(secretName, out var update) || update.Kind == CloudSecretUpdateKind.KeepExisting)
        {
            return;
        }

        if (update.Kind == CloudSecretUpdateKind.Clear)
        {
            await _secureStorage.DeleteAsync(storageKey).ConfigureAwait(false);
            return;
        }

        await _secureStorage.SaveAsync(storageKey, update.Value ?? string.Empty).ConfigureAwait(false);
    }

    private static bool TryCreateConfiguration(
        IReadOnlyDictionary<string, string> options,
        string accessKeyId,
        string secretAccessKey,
        out S3Configuration? configuration,
        out string errorMessage)
    {
        configuration = null;
        var endpoint = GetValue(options, EndpointOptionKey).Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(endpoint) ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri) ||
            (endpointUri.Scheme != Uri.UriSchemeHttps && endpointUri.Scheme != Uri.UriSchemeHttp))
        {
            errorMessage = "S3 endpoint must be an absolute HTTP or HTTPS URL.";
            return false;
        }

        var bucket = GetValue(options, BucketOptionKey).Trim();
        if (string.IsNullOrWhiteSpace(bucket))
        {
            errorMessage = "S3 bucket is required.";
            return false;
        }

        var objectKey = NormalizeObjectKey(GetValue(options, ObjectKeyOptionKey));
        if (string.IsNullOrWhiteSpace(objectKey))
        {
            errorMessage = "S3 object key is required.";
            return false;
        }

        var region = GetValue(options, RegionOptionKey).Trim();
        if (string.IsNullOrWhiteSpace(region))
        {
            region = DefaultRegion;
        }

        var forcePathStyle = ParseBoolean(GetValue(options, ForcePathStyleOptionKey), defaultValue: true);
        configuration = new S3Configuration(
            endpointUri,
            bucket,
            region,
            objectKey,
            forcePathStyle,
            accessKeyId,
            secretAccessKey,
            CreateObjectUri(endpointUri, bucket, objectKey, forcePathStyle),
            null);
        errorMessage = string.Empty;
        return true;
    }

    private static Uri CreateObjectUri(Uri endpoint, string bucket, string objectKey, bool forcePathStyle)
    {
        var escapedObjectKey = string.Join("/", objectKey.Split('/').Select(Uri.EscapeDataString));
        if (forcePathStyle)
        {
            return new Uri(endpoint, CombinePath(endpoint.AbsolutePath, bucket, escapedObjectKey));
        }

        var builder = new UriBuilder(endpoint)
        {
            Host = bucket + "." + endpoint.Host,
            Path = CombinePath(endpoint.AbsolutePath, escapedObjectKey)
        };
        return builder.Uri;
    }

    private static string CombinePath(params string[] segments)
    {
        var values = segments
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .Select(segment => segment.Trim('/'))
            .Where(segment => !string.IsNullOrWhiteSpace(segment))
            .ToArray();
        return "/" + string.Join("/", values);
    }

    private static string NormalizeObjectKey(string value)
    {
        var objectKey = string.IsNullOrWhiteSpace(value) ? DefaultObjectKey : value.Trim();
        return objectKey.TrimStart('/');
    }

    private static AuthenticationHeaderValue CreateAuthorizationHeader(
        HttpRequestMessage request,
        S3Configuration configuration,
        DateTimeOffset now,
        string payloadHash)
    {
        var amzDate = now.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var credentialScope = string.Join('/', dateStamp, configuration.Region, ServiceName, "aws4_request");
        var canonicalHeaders =
            "host:" + request.RequestUri!.Authority + "\n" +
            "x-amz-content-sha256:" + payloadHash + "\n" +
            "x-amz-date:" + amzDate + "\n";
        const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";
        var canonicalRequest = string.Join(
            "\n",
            request.Method.Method,
            request.RequestUri.AbsolutePath,
            request.RequestUri.Query.TrimStart('?'),
            canonicalHeaders,
            signedHeaders,
            payloadHash);
        var stringToSign = string.Join(
            "\n",
            "AWS4-HMAC-SHA256",
            amzDate,
            credentialScope,
            ComputeSha256Hex(Encoding.UTF8.GetBytes(canonicalRequest)));
        var signingKey = GetSignatureKey(configuration.SecretAccessKey, dateStamp, configuration.Region, ServiceName);
        var signature = ToHex(HmacSha256(signingKey, stringToSign));
        var value =
            "Credential=" + configuration.AccessKeyId + "/" + credentialScope +
            ", SignedHeaders=" + signedHeaders +
            ", Signature=" + signature;
        return new AuthenticationHeaderValue("AWS4-HMAC-SHA256", value);
    }

    private static byte[] GetSignatureKey(string key, string dateStamp, string regionName, string serviceName)
    {
        var dateKey = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + key), dateStamp);
        var dateRegionKey = HmacSha256(dateKey, regionName);
        var dateRegionServiceKey = HmacSha256(dateRegionKey, serviceName);
        return HmacSha256(dateRegionServiceKey, "aws4_request");
    }

    private static byte[] HmacSha256(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string ComputeSha256Hex(byte[] content)
    {
        using var sha256 = SHA256.Create();
        return ToHex(sha256.ComputeHash(content));
    }

    private static string ToHex(byte[] bytes)
    {
        var builder = new StringBuilder(bytes.Length * 2);
        foreach (var value in bytes)
        {
            builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static bool ParseBoolean(string value, bool defaultValue)
        => string.IsNullOrWhiteSpace(value) ? defaultValue : bool.TryParse(value, out var parsed) ? parsed : defaultValue;

    private static string GetValue(IReadOnlyDictionary<string, string> values, string key)
        => values.FirstOrDefault(value => string.Equals(value.Key, key, StringComparison.OrdinalIgnoreCase)).Value ?? string.Empty;

    private sealed class Session : ICloudConfigStorageSession
    {
        private readonly S3CloudConfigStorageProvider _provider;
        private readonly S3Configuration _configuration;

        public Session(S3CloudConfigStorageProvider provider, S3Configuration configuration)
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

    private sealed record S3Configuration(
        Uri? Endpoint,
        string Bucket,
        string Region,
        string ObjectKey,
        bool ForcePathStyle,
        string AccessKeyId,
        string SecretAccessKey,
        Uri ObjectUri,
        string? ErrorMessage)
    {
        public bool IsConfigured => Endpoint is not null && string.IsNullOrEmpty(ErrorMessage);

        public static S3Configuration NotConfigured(string? errorMessage) =>
            new(
                null,
                string.Empty,
                string.Empty,
                string.Empty,
                true,
                string.Empty,
                string.Empty,
                new Uri("https://example.invalid/"),
                string.IsNullOrWhiteSpace(errorMessage) ? "S3 configuration is incomplete." : errorMessage);
    }
}
