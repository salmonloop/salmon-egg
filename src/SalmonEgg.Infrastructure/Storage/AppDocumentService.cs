using System;
using System.IO;
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
        get
        {
            var path = Path.Combine(_paths.AppDataRootPath, "docs");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    public string GetPrivacyPolicyPath()
    {
        return Path.Combine(DocsRootPath, "privacy-policy.md");
    }

    public string GetReleaseNotesPath()
    {
        return Path.Combine(DocsRootPath, "release-notes.md");
    }
}
