namespace SalmonEgg.Domain.Services;

public interface IAppDocumentService
{
    string DocsRootPath { get; }

    string GetPrivacyPolicyPath();

    string GetReleaseNotesPath();
}
