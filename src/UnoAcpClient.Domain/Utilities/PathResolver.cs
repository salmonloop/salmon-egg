using System;
using System.IO;
using System.Runtime.InteropServices;

namespace UnoAcpClient.Domain.Utilities;

/// <summary>
/// 路径解析工具类，提供跨平台的命令解析和路径处理功能。
/// </summary>
public static class PathResolver
{
    public static string ResolveCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
            return command;

        // 如果命令包含路径分隔符，直接使用（可能是绝对或相对路径）
        if (command.Contains(Path.DirectorySeparatorChar) || command.Contains(Path.AltDirectorySeparatorChar))
            return command;

        // 如果已有扩展名，直接使用
        if (Path.HasExtension(command))
            return command;

        // 根据操作系统采用不同的策略
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ResolveWindowsCommand(command);

        return command;
    }

    private static string ResolveWindowsCommand(string command)
    {
        System.Diagnostics.Debug.WriteLine($"[PathResolver] 正在解析命令：{command}");
        System.Diagnostics.Debug.WriteLine($"[PathResolver] 当前目录：{Environment.CurrentDirectory}");

        // 1. 检查当前目录是否有该文件
        string currentPath = Path.Combine(Environment.CurrentDirectory, command);
        if (File.Exists(currentPath))
        {
            System.Diagnostics.Debug.WriteLine($"[PathResolver] 在当前目录找到：{currentPath}");
            return command;
        }

        // 2. 尝试添加常见的 Windows 可执行扩展名（包括 .cmd）
        foreach (var ext in new[] { ".exe", ".bat", ".cmd", ".ps1", "" })
        {
            string candidate = string.IsNullOrEmpty(ext) ? command : command + ext;

            // 检查当前目录
            string fullPath = Path.Combine(Environment.CurrentDirectory, candidate);
            if (File.Exists(fullPath))
            {
                System.Diagnostics.Debug.WriteLine($"[PathResolver] 在当前目录找到 (带扩展名): {fullPath}");
                return candidate;
            }

            // 3. 通过 PATH 环境变量查找
            string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            System.Diagnostics.Debug.WriteLine($"[PathResolver] 尝试扩展名 {ext}, PATH 长度：{pathEnv.Length}");

            foreach (var pathDir in pathEnv.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(pathDir)) continue;

                string pathFile = Path.Combine(pathDir.Trim(), candidate);
                if (File.Exists(pathFile))
                {
                    System.Diagnostics.Debug.WriteLine($"[PathResolver] 在 PATH 中找到：{pathFile}");
                    return pathFile;
                }
            }
        }

        // 4. 如果都找不到，返回原始命令让系统尝试查找
        System.Diagnostics.Debug.WriteLine($"[PathResolver] 未找到文件，返回原始命令：{command}");
        return command;
    }
}
