using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services;

public interface IApplicationStartupWorkflow
{
    Task ActivateShellAsync();

    Task InitializeRuntimeAsync();
}
