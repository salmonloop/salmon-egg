using System;
using System.IO;

namespace SalmonEgg.Infrastructure.Storage;

public static class SalmonEggPaths
{
    public static string GetAppDataRootPath()
    {
        var overrideRoot = Environment.GetEnvironmentVariable("SALMONEGG_APPDATA_ROOT");
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return overrideRoot;
        }

#if __ANDROID__
        return Path.Combine(
            Android.App.Application.Context.FilesDir?.AbsolutePath
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SalmonEgg");
#elif __IOS__
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "..",
            "Library",
            "Application Support",
            "SalmonEgg");
#elif __MACOS__
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SalmonEgg");
#elif WINDOWS || WINDOWS_UWP
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SalmonEgg");
#elif __WASM__
        return "/local/SalmonEgg";
#else
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SalmonEgg");
#endif
    }

    public static string GetConfigRootPath() => Path.Combine(GetAppDataRootPath(), "config");

    public static string GetServersDirectoryPath() => Path.Combine(GetConfigRootPath(), "servers");

    public static string GetAppYamlPath() => Path.Combine(GetConfigRootPath(), "app.yaml");

    public static string GetConfigMigrationsDirectoryPath() =>
        Path.Combine(GetAppDataRootPath(), "config-migrations");

    public static string GetMigrationsLogPath() =>
        Path.Combine(GetConfigMigrationsDirectoryPath(), "migrations.log");
}
