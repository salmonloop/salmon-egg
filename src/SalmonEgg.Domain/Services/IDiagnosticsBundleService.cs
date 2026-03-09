using System.Threading.Tasks;
using SalmonEgg.Domain.Models.Diagnostics;

namespace SalmonEgg.Domain.Services;

public interface IDiagnosticsBundleService
{
    /// <summary>
    /// Creates a diagnostic zip bundle and returns its absolute path.
    /// </summary>
    Task<string> CreateBundleAsync(DiagnosticsSnapshot snapshot);
}

