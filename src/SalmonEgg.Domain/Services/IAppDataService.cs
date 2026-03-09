namespace SalmonEgg.Domain.Services;

public interface IAppDataService
{
    string AppDataRootPath { get; }
    string ConfigRootPath { get; }
    string LogsDirectoryPath { get; }
    string CacheRootPath { get; }
    string ExportsDirectoryPath { get; }
}

