#if __WASM__
using System;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Extensions.Logging;
using SalmonEgg.Presentation.Core.Services.Input;

namespace SalmonEgg.Platforms.WebAssembly;

[SupportedOSPlatform("browser")]
public sealed partial class WasmGamepadNativeInputBridge : IGamepadNativeInputBridge
{
    private const string GamepadModuleName = "salmon-egg-wasm-gamepad.js";

    private static readonly string GamepadModuleUrl = ResolveGamepadModuleUrl();
    private static readonly object ModuleSync = new();
    private static JSObject? _gamepadModule;
    private static bool _moduleImportStarted;
    private static bool _moduleImportFailed;

    private readonly ILogger<WasmGamepadNativeInputBridge> _logger;

    public WasmGamepadNativeInputBridge(ILogger<WasmGamepadNativeInputBridge> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        EnsureGamepadModuleImportStarted(_logger);
    }

    public bool TryDispatch(GamepadNavigationIntent intent)
    {
        if (!TryMapKey(intent, out var key, out var code))
        {
            return false;
        }

        if (!IsGamepadModuleReady)
        {
            EnsureGamepadModuleImportStarted(_logger);
            return false;
        }

        try
        {
            return DispatchKeyboardNavigationInterop(key, code);
        }
        catch (JSException ex)
        {
            _logger.LogWarning(
                ex,
                "WASM gamepad native input bridge failed to dispatch keyboard navigation. Intent={Intent}",
                intent);
            return false;
        }
    }

    private static bool IsGamepadModuleReady
    {
        get
        {
            lock (ModuleSync)
            {
                return _gamepadModule is not null;
            }
        }
    }

    private static void EnsureGamepadModuleImportStarted(ILogger logger)
    {
        lock (ModuleSync)
        {
            if (_gamepadModule is not null || _moduleImportStarted || _moduleImportFailed)
            {
                return;
            }

            _moduleImportStarted = true;
        }

        _ = ImportGamepadModuleAsync(logger);
    }

    private static async System.Threading.Tasks.Task ImportGamepadModuleAsync(ILogger logger)
    {
        try
        {
            var module = await JSHost.ImportAsync(GamepadModuleName, GamepadModuleUrl, CancellationToken.None)
                .ConfigureAwait(false);

            lock (ModuleSync)
            {
                _gamepadModule = module;
            }
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            lock (ModuleSync)
            {
                _moduleImportFailed = true;
            }

            logger.LogWarning(
                ex,
                "WASM gamepad native input bridge module import failed. Module={ModuleName}",
                GamepadModuleName);
        }
    }

    private static bool TryMapKey(GamepadNavigationIntent intent, out string key, out string code)
    {
        (key, code) = intent switch
        {
            GamepadNavigationIntent.MoveUp => ("ArrowUp", "ArrowUp"),
            GamepadNavigationIntent.MoveDown => ("ArrowDown", "ArrowDown"),
            GamepadNavigationIntent.MoveLeft => ("ArrowLeft", "ArrowLeft"),
            GamepadNavigationIntent.MoveRight => ("ArrowRight", "ArrowRight"),
            GamepadNavigationIntent.Activate => ("Enter", "Enter"),
            _ => (string.Empty, string.Empty)
        };

        return key.Length > 0;
    }

    [JSImport("dispatchKeyboardNavigation", "salmon-egg-wasm-gamepad.js")]
    private static partial bool DispatchKeyboardNavigationInterop(string key, string code);

    private static string ResolveGamepadModuleUrl()
    {
        var appBase = NormalizePathSegment(Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_APP_BASE"));
        if (string.IsNullOrWhiteSpace(appBase))
        {
            return "./" + GamepadModuleName;
        }

        var webAppBasePath = NormalizePathSegment(Environment.GetEnvironmentVariable("UNO_BOOTSTRAP_WEBAPP_BASE_PATH"));
        return string.IsNullOrWhiteSpace(webAppBasePath)
            ? $"/{appBase}/_framework/{GamepadModuleName}"
            : $"/{webAppBasePath}/{appBase}/_framework/{GamepadModuleName}";
    }

    private static string NormalizePathSegment(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Trim('/');
}
#endif
