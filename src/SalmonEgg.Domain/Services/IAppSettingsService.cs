using System.Threading.Tasks;
using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Services;

public interface IAppSettingsService
{
    Task<AppSettings> LoadAsync();

    Task SaveAsync(AppSettings settings);
}

