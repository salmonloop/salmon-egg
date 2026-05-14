using System.Threading;
using System.Threading.Tasks;

namespace SalmonEgg.Domain.Services;

public interface IAppDocumentService
{
    string DocsRootPath { get; }

    string GetPrivacyPolicyPath();

    string GetReleaseNotesPath();

    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);
}
