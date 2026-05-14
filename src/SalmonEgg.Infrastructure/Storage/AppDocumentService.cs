using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class AppDocumentService : IAppDocumentService
{
    private readonly IAppDataService _paths;

    public AppDocumentService(IAppDataService paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public string DocsRootPath
    {
        get => Path.Combine(_paths.AppDataRootPath, "docs");
    }

    public string GetPrivacyPolicyPath()
    {
        return Path.Combine(DocsRootPath, "privacy-policy.md");
    }

    public string GetReleaseNotesPath()
    {
        return Path.Combine(DocsRootPath, "release-notes.md");
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(File.Exists(path));
    }
}
