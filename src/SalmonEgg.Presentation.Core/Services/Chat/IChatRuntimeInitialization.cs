using System.Threading.Tasks;

namespace SalmonEgg.Presentation.Core.Services.Chat;

public interface IChatRuntimeInitialization
{
    Task<bool> InitializeAcpProfilesAsync();

    Task<bool> RestoreConversationsAsync();
}
