using System.IO;
using SalmonEgg.Domain.Services;

namespace SalmonEgg.Infrastructure.Storage;

public sealed class AppDataService : IAppDataService
{
    public string AppDataRootPath => SalmonEggPaths.GetAppDataRootPath();

    public string ConfigRootPath => SalmonEggPaths.GetConfigRootPath();

    public string LogsDirectoryPath => Path.Combine(AppDataRootPath, "logs");

    public string CacheRootPath => Path.Combine(AppDataRootPath, "cache");

    public string ExportsDirectoryPath => Path.Combine(AppDataRootPath, "exports");
}

