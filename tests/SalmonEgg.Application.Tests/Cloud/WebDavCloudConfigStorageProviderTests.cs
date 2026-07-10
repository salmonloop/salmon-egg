using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using SalmonEgg.Domain.Models;
using SalmonEgg.Domain.Services;
using SalmonEgg.Infrastructure.Storage;
using Xunit;

namespace SalmonEgg.Application.Tests.Cloud;

public sealed class WebDavCloudConfigStorageProviderTests
{
    [Fact]
    public async Task TryDownloadAsync_WhenDirectoryUrlConfigured_GetsDefaultPackageFileWithBasicAuth()
    {
        await using var server = await WebDavSmokeServer.StartAsync(
            "/dav/config/salmonegg-config.zip",
            "alice",
            "app-password");
        var provider = CreateProvider(server.CreateUrl("dav/config/"));

        await provider.ConfigureAsync(
            CreateOptions(server.CreateUrl("dav/config/"), "alice"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [WebDavCloudConfigStorageProvider.PasswordSecretKey] = "app-password"
            }, TestContext.Current.CancellationToken);
        var result = await provider.TryDownloadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal([10, 11, 12], result.Content);
        var request = Assert.Single(server.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/dav/config/salmonegg-config.zip", request.Path);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:app-password")), request.Authorization);
    }

    [Fact]
    public async Task TryDownloadAsync_WhenCredentialsAreRejected_ThrowsForbiddenAfterUsingResolvedPackagePath()
    {
        await using var server = await WebDavSmokeServer.StartAsync(
            "/dav/config/salmonegg-config.zip",
            "alice",
            "app-password");
        var provider = CreateProvider(server.CreateUrl("dav/config/"));

        await provider.ConfigureAsync(
            CreateOptions(server.CreateUrl("dav/config/"), "alice"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [WebDavCloudConfigStorageProvider.PasswordSecretKey] = "wrong-password"
            }, TestContext.Current.CancellationToken);
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => provider.TryDownloadAsync(TestContext.Current.CancellationToken));

        Assert.Contains("403", exception.Message, StringComparison.Ordinal);
        var request = Assert.Single(server.Requests);
        Assert.Equal("GET", request.Method);
        Assert.Equal("/dav/config/salmonegg-config.zip", request.Path);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:wrong-password")), request.Authorization);
    }

    [Theory]
    [InlineData("dav/config")]
    [InlineData("dav/config/")]
    public async Task UploadAsync_WhenDirectoryUrlConfigured_PutsDefaultPackageFileWithBasicAuth(string folderPath)
    {
        await using var server = await WebDavSmokeServer.StartAsync(
            "/dav/config/salmonegg-config.zip",
            "alice",
            "app-password");
        var provider = CreateProvider(server.CreateUrl(folderPath));

        var configuration = await provider.ConfigureAsync(
            CreateOptions(server.CreateUrl(folderPath), "alice"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [WebDavCloudConfigStorageProvider.PasswordSecretKey] = "app-password"
            }, TestContext.Current.CancellationToken);
        var result = await provider.UploadAsync([1, 2, 3], expectedETag: null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(configuration.Succeeded);
        Assert.Equal(CloudConfigUploadStatus.Uploaded, result.Status);
        var request = Assert.Single(server.Requests);
        Assert.Equal("PUT", request.Method);
        Assert.Equal("/dav/config/salmonegg-config.zip", request.Path);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:app-password")), request.Authorization);
        Assert.Equal([1, 2, 3], request.Body);
    }

    [Fact]
    public async Task UploadAsync_WhenDefaultFileUrlAlreadyConfigured_DoesNotAppendFileNameTwice()
    {
        await using var server = await WebDavSmokeServer.StartAsync(
            "/dav/config/salmonegg-config.zip",
            "alice",
            "app-password");
        var provider = CreateProvider(server.CreateUrl("dav/config/salmonegg-config.zip"));

        var configuration = await provider.ConfigureAsync(
            CreateOptions(server.CreateUrl("dav/config/salmonegg-config.zip"), "alice"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [WebDavCloudConfigStorageProvider.PasswordSecretKey] = "app-password"
            }, TestContext.Current.CancellationToken);
        var result = await provider.UploadAsync([4, 5, 6], expectedETag: null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(configuration.Succeeded);
        Assert.Equal(CloudConfigUploadStatus.Uploaded, result.Status);
        var request = Assert.Single(server.Requests);
        Assert.Equal("/dav/config/salmonegg-config.zip", request.Path);
    }

    [Fact]
    public async Task UploadAsync_WhenDirectoryDoesNotExist_CreatesCollectionAndRetriesPut()
    {
        await using var server = await WebDavSmokeServer.StartAsync(
            "/dav/config/salmonegg-config.zip",
            "alice",
            "app-password",
            existingCollections: ["/", "/dav/"]);
        var provider = CreateProvider(server.CreateUrl("dav/config/"));

        await provider.ConfigureAsync(
            CreateOptions(server.CreateUrl("dav/config/"), "alice"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [WebDavCloudConfigStorageProvider.PasswordSecretKey] = "app-password"
            }, TestContext.Current.CancellationToken);
        var result = await provider.UploadAsync([4, 5, 6], expectedETag: null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CloudConfigUploadStatus.Uploaded, result.Status);
        Assert.Equal(
            ["PUT /dav/config/salmonegg-config.zip", "MKCOL /dav/config/", "PUT /dav/config/salmonegg-config.zip"],
            server.Requests.Select(request => request.Method + " " + request.Path));
    }

    [Fact]
    public async Task UploadAsync_WhenNestedDirectoryDoesNotExist_CreatesMissingParentsBeforeRetryingPut()
    {
        await using var server = await WebDavSmokeServer.StartAsync(
            "/dav/config/nested/salmonegg-config.zip",
            "alice",
            "app-password",
            existingCollections: ["/", "/dav/"]);
        var provider = CreateProvider(server.CreateUrl("dav/config/nested/"));

        await provider.ConfigureAsync(
            CreateOptions(server.CreateUrl("dav/config/nested/"), "alice"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [WebDavCloudConfigStorageProvider.PasswordSecretKey] = "app-password"
            }, TestContext.Current.CancellationToken);
        var result = await provider.UploadAsync([7, 8, 9], expectedETag: null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(CloudConfigUploadStatus.Uploaded, result.Status);
        Assert.Equal(
            [
                "PUT /dav/config/nested/salmonegg-config.zip",
                "MKCOL /dav/config/nested/",
                "MKCOL /dav/config/",
                "MKCOL /dav/config/nested/",
                "PUT /dav/config/nested/salmonegg-config.zip"
            ],
            server.Requests.Select(request => request.Method + " " + request.Path));
    }

    [Fact]
    public async Task UploadAsync_WhenCredentialsAreRejected_ReturnsForbiddenAfterUsingResolvedPackagePath()
    {
        await using var server = await WebDavSmokeServer.StartAsync(
            "/dav/config/salmonegg-config.zip",
            "alice",
            "app-password");
        var provider = CreateProvider(server.CreateUrl("dav/config/"));

        var configuration = await provider.ConfigureAsync(
            CreateOptions(server.CreateUrl("dav/config/"), "alice"),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [WebDavCloudConfigStorageProvider.PasswordSecretKey] = "wrong-password"
            }, TestContext.Current.CancellationToken);
        var result = await provider.UploadAsync([7, 8, 9], expectedETag: null, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(configuration.Succeeded);
        Assert.Equal(CloudConfigUploadStatus.Failed, result.Status);
        Assert.Contains("403", result.UserMessage, StringComparison.Ordinal);
        var request = Assert.Single(server.Requests);
        Assert.Equal("/dav/config/salmonegg-config.zip", request.Path);
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("alice:wrong-password")), request.Authorization);
    }

    private static WebDavCloudConfigStorageProvider CreateProvider(string webDavUrl)
    {
        var settings = new AppSettings
        {
            CloudConfigSync = new CloudConfigSyncSettings
            {
                ProviderOptions =
                {
                    [WebDavCloudConfigStorageProvider.ProviderId] = CreateOptions(webDavUrl, "alice")
                }
            }
        };

        return new WebDavCloudConfigStorageProvider(
            new InMemoryAppSettingsService(settings),
            new InMemorySecureStorage());
    }

    private static Dictionary<string, string> CreateOptions(string webDavUrl, string username) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [WebDavCloudConfigStorageProvider.FileUrlOptionKey] = webDavUrl,
            [WebDavCloudConfigStorageProvider.UsernameOptionKey] = username
        };

    private sealed class InMemoryAppSettingsService(AppSettings settings) : IAppSettingsService
    {
        private AppSettings _settings = settings;

        public Task<AppSettings> LoadAsync() => Task.FromResult(_settings);

        public Task SaveAsync(AppSettings value)
        {
            _settings = value;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemorySecureStorage : ISecureStorage
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task SaveAsync(string key, string value)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task<string?> LoadAsync(string key)
        {
            _values.TryGetValue(key, out var value);
            return Task.FromResult<string?>(value);
        }

        public Task DeleteAsync(string key)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class WebDavSmokeServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly string _expectedPath;
        private readonly string _expectedAuthorization;
        private readonly HashSet<string> _existingCollections;
        private readonly Task _listenTask;
        private readonly ConcurrentQueue<WebDavRequest> _requests = new();

        private WebDavSmokeServer(
            HttpListener listener,
            Uri baseUri,
            string expectedPath,
            string username,
            string password,
            IEnumerable<string> existingCollections)
        {
            _listener = listener;
            BaseUri = baseUri;
            _expectedPath = expectedPath;
            _expectedAuthorization = "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes(username + ":" + password));
            _existingCollections = existingCollections
                .Append("/")
                .Select(NormalizeCollectionPath)
                .ToHashSet(StringComparer.Ordinal);
            _listenTask = Task.Run(ListenAsync);
        }

        public Uri BaseUri { get; }

        public IReadOnlyCollection<WebDavRequest> Requests => _requests.ToArray();

        public static Task<WebDavSmokeServer> StartAsync(
            string expectedPath,
            string username,
            string password,
            IEnumerable<string>? existingCollections = null)
        {
            var port = GetFreeTcpPort();
            var baseUri = new Uri("http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/");
            var listener = new HttpListener();
            listener.Prefixes.Add(baseUri.AbsoluteUri);
            listener.Start();
            existingCollections ??= new[] { GetParentCollectionPath(expectedPath) };
            return Task.FromResult(new WebDavSmokeServer(listener, baseUri, expectedPath, username, password, existingCollections));
        }

        public string CreateUrl(string relativePath) => new Uri(BaseUri, relativePath).AbsoluteUri;

        public async ValueTask DisposeAsync()
        {
            _listener.Close();
            try
            {
                await _listenTask.ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private async Task ListenAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                await HandleAsync(context).ConfigureAwait(false);
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            using var body = new MemoryStream();
            await context.Request.InputStream.CopyToAsync(body).ConfigureAwait(false);
            var request = new WebDavRequest(
                context.Request.HttpMethod,
                context.Request.Url?.AbsolutePath ?? string.Empty,
                context.Request.Headers["Authorization"] ?? string.Empty,
                body.ToArray());
            _requests.Enqueue(request);

            if (!string.Equals(request.Authorization, _expectedAuthorization, StringComparison.Ordinal))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                context.Response.Close();
                return;
            }

            if (!string.Equals(request.Path, _expectedPath, StringComparison.Ordinal))
            {
                if (!string.Equals(request.Method, "MKCOL", StringComparison.Ordinal))
                {
                    context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                    context.Response.Close();
                    return;
                }
            }

            if (string.Equals(request.Method, "GET", StringComparison.Ordinal))
            {
                var content = new byte[] { 10, 11, 12 };
                context.Response.StatusCode = (int)HttpStatusCode.OK;
                context.Response.ContentType = "application/zip";
                context.Response.ContentLength64 = content.Length;
                await context.Response.OutputStream.WriteAsync(content).ConfigureAwait(false);
                context.Response.Close();
                return;
            }

            if (string.Equals(request.Method, "MKCOL", StringComparison.Ordinal))
            {
                HandleMkCol(context, request.Path);
                return;
            }

            if (!string.Equals(request.Method, "PUT", StringComparison.Ordinal))
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                context.Response.Close();
                return;
            }

            if (!_existingCollections.Contains(GetParentCollectionPath(request.Path)))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                context.Response.Close();
                return;
            }

            context.Response.StatusCode = (int)HttpStatusCode.Created;
            context.Response.Headers["ETag"] = "\"smoke-etag\"";
            context.Response.Close();
        }

        private void HandleMkCol(HttpListenerContext context, string rawPath)
        {
            var collectionPath = NormalizeCollectionPath(rawPath);
            if (_existingCollections.Contains(collectionPath))
            {
                context.Response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
                context.Response.Close();
                return;
            }

            if (!_existingCollections.Contains(GetParentCollectionPath(collectionPath)))
            {
                context.Response.StatusCode = (int)HttpStatusCode.Conflict;
                context.Response.Close();
                return;
            }

            _existingCollections.Add(collectionPath);
            context.Response.StatusCode = (int)HttpStatusCode.Created;
            context.Response.Close();
        }

        private static string NormalizeCollectionPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || string.Equals(path, "/", StringComparison.Ordinal))
            {
                return "/";
            }

            return path.EndsWith("/", StringComparison.Ordinal) ? path : path + "/";
        }

        private static string GetParentCollectionPath(string path)
        {
            var value = path.TrimEnd('/');
            if (string.IsNullOrEmpty(value) || string.Equals(value, "/", StringComparison.Ordinal))
            {
                return "/";
            }

            var index = value.LastIndexOf('/');
            return index <= 0 ? "/" : value.Substring(0, index + 1);
        }

        private static int GetFreeTcpPort()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }

    private sealed record WebDavRequest(string Method, string Path, string Authorization, byte[] Body);
}
