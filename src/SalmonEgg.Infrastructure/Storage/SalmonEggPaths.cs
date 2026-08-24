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

        if (OperatingSystem.IsBrowser())
        {
            return "/local/SalmonEgg";
        }

        if (OperatingSystem.IsIOS())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "..",
                "Library",
                "Application Support",
                "SalmonEgg");
        }

        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SalmonEgg");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SalmonEgg");
    }

    public static string GetConfigRootPath() => Path.Combine(GetAppDataRootPath(), "config");

    public static string GetServersDirectoryPath() => Path.Combine(GetConfigRootPath(), "servers");

    public static string GetAppYamlPath() => Path.Combine(GetConfigRootPath(), "app.yaml");
}
