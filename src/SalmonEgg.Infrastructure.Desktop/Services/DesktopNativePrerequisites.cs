using System;
using System.Runtime.InteropServices;

namespace SalmonEgg.Infrastructure.Services;

public static class DesktopNativePrerequisites
{
    private const int RtldNow = 2;
    private const int RtldGlobal = 0x100;
    private static readonly object Gate = new();
    private static IntPtr _freetypeHandle;

    public static void Initialize()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        EnsureGlobalLibraryLoaded("libfreetype.so.6", ref _freetypeHandle);
    }

    internal static bool IsFreetypeLoaded => _freetypeHandle != IntPtr.Zero;

    private static void EnsureGlobalLibraryLoaded(string libraryName, ref IntPtr handle)
    {
        lock (Gate)
        {
            if (handle != IntPtr.Zero)
            {
                return;
            }

            var loaded = Dlopen(libraryName, RtldNow | RtldGlobal);
            if (loaded == IntPtr.Zero)
            {
                var message = PtrToString(Dlerror()) ?? $"Unable to load {libraryName}.";
                throw new InvalidOperationException(
                    $"Linux desktop runtime prerequisite '{libraryName}' could not be loaded: {message}");
            }

            handle = loaded;
        }
    }

    private static string? PtrToString(IntPtr value)
        => value == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(value);

    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    private static extern IntPtr Dlopen(string fileName, int flags);

    [DllImport("libdl.so.2", EntryPoint = "dlerror")]
    private static extern IntPtr Dlerror();
}
