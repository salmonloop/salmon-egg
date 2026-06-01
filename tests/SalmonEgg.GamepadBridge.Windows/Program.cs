using System.Reflection;

const string HidMaestroCorePathEnvVar = "SALMONEGG_HIDMAESTRO_CORE_PATH";

if (args.Length != 1 || !string.Equals(args[0], "serve", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: SalmonEgg.GamepadBridge.Windows serve");
    return 1;
}

var hidMaestroCorePath = ResolveHidMaestroCorePath();
using var bridge = new HidMaestroBridge(hidMaestroCorePath);

while (true)
{
    var line = Console.ReadLine();
    if (line is null)
    {
        break;
    }

    var command = line.Trim();
    if (command.Length == 0)
    {
        continue;
    }

    try
    {
        if (string.Equals(command, "create", StringComparison.OrdinalIgnoreCase))
        {
            bridge.CreateController();
            Console.WriteLine("ok");
            continue;
        }

        if (string.Equals(command, "dispose", StringComparison.OrdinalIgnoreCase))
        {
            bridge.DisposeController();
            Console.WriteLine("ok");
            continue;
        }

        if (command.StartsWith("press ", StringComparison.OrdinalIgnoreCase))
        {
            bridge.Press(command["press ".Length..].Trim());
            Console.WriteLine("ok");
            continue;
        }

        Console.WriteLine($"error unsupported-command {command}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"error {Sanitize(Describe(ex))}");
    }
}

return 0;

static string ResolveHidMaestroCorePath()
{
    var configured = Environment.GetEnvironmentVariable(HidMaestroCorePathEnvVar);
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured;
    }

    var sibling = Path.Combine(AppContext.BaseDirectory, "HIDMaestro.Core.dll");
    if (File.Exists(sibling))
    {
        return sibling;
    }

    throw new InvalidOperationException(
        $"Unable to locate HIDMaestro.Core.dll. Set {HidMaestroCorePathEnvVar} or place the DLL beside the bridge executable.");
}

static string Sanitize(string message)
    => message.Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal);

static string Describe(Exception exception)
{
    if (exception is TargetInvocationException targetInvocationException
        && targetInvocationException.InnerException is not null)
    {
        return $"{targetInvocationException.InnerException.GetType().Name}: {targetInvocationException.InnerException.Message}";
    }

    return $"{exception.GetType().Name}: {exception.Message}";
}

internal sealed class HidMaestroBridge : IDisposable
{
    private const string DefaultProfileId = "xbox-360-wired";

    private readonly Assembly _assembly;
    private readonly object _context;
    private readonly Type _hmButtonType;
    private readonly Type _hmHatType;
    private readonly Type _hmGamepadStateType;
    private readonly MethodInfo _createControllerMethod;
    private readonly MethodInfo _removeAllVirtualControllersMethod;
    private readonly MethodInfo _loadDefaultProfilesMethod;
    private readonly MethodInfo _getProfileMethod;
    private readonly MethodInfo _submitStateMethod;
    private readonly PropertyInfo _isDriverInstalledProperty;

    private object? _controller;

    public HidMaestroBridge(string hidMaestroCorePath)
    {
        if (!File.Exists(hidMaestroCorePath))
        {
            throw new FileNotFoundException("HIDMaestro.Core.dll was not found.", hidMaestroCorePath);
        }

        _assembly = Assembly.LoadFrom(hidMaestroCorePath);
        var hmContextType = _assembly.GetType("HIDMaestro.HMContext", throwOnError: true)!;
        _context = Activator.CreateInstance(hmContextType)
            ?? throw new InvalidOperationException("Failed to create HIDMaestro.HMContext.");

        _hmButtonType = _assembly.GetType("HIDMaestro.HMButton", throwOnError: true)!;
        _hmHatType = _assembly.GetType("HIDMaestro.HMHat", throwOnError: true)!;
        _hmGamepadStateType = _assembly.GetType("HIDMaestro.HMGamepadState", throwOnError: true)!;

        _createControllerMethod = hmContextType.GetMethod("CreateController", [ResolveType("HIDMaestro.HMProfile")])
            ?? throw new MissingMethodException(hmContextType.FullName, "CreateController");
        _removeAllVirtualControllersMethod = hmContextType.GetMethod("RemoveAllVirtualControllers", Type.EmptyTypes)
            ?? throw new MissingMethodException(hmContextType.FullName, "RemoveAllVirtualControllers");
        _loadDefaultProfilesMethod = hmContextType.GetMethod("LoadDefaultProfiles", Type.EmptyTypes)
            ?? throw new MissingMethodException(hmContextType.FullName, "LoadDefaultProfiles");
        _getProfileMethod = hmContextType.GetMethod("GetProfile", [typeof(string)])
            ?? throw new MissingMethodException(hmContextType.FullName, "GetProfile");
        _isDriverInstalledProperty = hmContextType.GetProperty("IsDriverInstalled")
            ?? throw new MissingMemberException(hmContextType.FullName, "IsDriverInstalled");

        var hmControllerType = ResolveType("HIDMaestro.HMController");
        _submitStateMethod = hmControllerType.GetMethod("SubmitState", [ResolveType("HIDMaestro.HMGamepadState").MakeByRefType()])
            ?? throw new MissingMethodException(hmControllerType.FullName, "SubmitState");
    }

    public void CreateController()
    {
        if (_controller is not null)
        {
            return;
        }

        _ = _loadDefaultProfilesMethod.Invoke(_context, null);

        if (!IsDriverInstalled())
        {
            throw new InvalidOperationException(
                "HIDMaestro driver is not installed. Install it once with administrator privileges before using the native-device gamepad backend.");
        }

        var profile = _getProfileMethod.Invoke(_context, [DefaultProfileId]);
        if (profile is null)
        {
            throw new InvalidOperationException($"Unable to resolve HIDMaestro profile '{DefaultProfileId}'.");
        }

        _controller = _createControllerMethod.Invoke(_context, [profile])
            ?? throw new InvalidOperationException("HIDMaestro failed to create a virtual controller.");
    }

    public void DisposeController()
    {
        _controller = null;
        _ = _removeAllVirtualControllersMethod.Invoke(_context, null);
    }

    public void Press(string input)
    {
        EnsureControllerCreated();

        switch (input.ToLowerInvariant())
        {
            case "dpad-up":
                SubmitTap(hatName: "North");
                break;
            case "dpad-down":
                SubmitTap(hatName: "South");
                break;
            case "dpad-left":
                SubmitTap(hatName: "West");
                break;
            case "dpad-right":
                SubmitTap(hatName: "East");
                break;
            case "a":
                SubmitTap(buttonName: "A");
                break;
            case "b":
                SubmitTap(buttonName: "B");
                break;
            case "y":
                SubmitTap(buttonName: "Y");
                break;
            default:
                throw new InvalidOperationException($"Unsupported gamepad input '{input}'.");
        }
    }

    public void Dispose()
    {
        try
        {
            DisposeController();
        }
        catch
        {
        }

        if (_context is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void EnsureControllerCreated()
    {
        if (_controller is null)
        {
            throw new InvalidOperationException("Virtual controller has not been created. Call 'create' first.");
        }
    }

    private bool IsDriverInstalled()
        => _isDriverInstalledProperty.GetValue(_context) as bool? == true;

    private void SubmitTap(string? buttonName = null, string? hatName = null)
    {
        SubmitState(buttonName, hatName);
        Thread.Sleep(60);
        SubmitState(buttonName: null, hatName: null);
        Thread.Sleep(30);
    }

    private void SubmitState(string? buttonName, string? hatName)
    {
        var state = Activator.CreateInstance(_hmGamepadStateType)
            ?? throw new InvalidOperationException("Failed to create HMGamepadState.");

        var buttonsField = _hmGamepadStateType.GetField("Buttons")
            ?? throw new MissingFieldException(_hmGamepadStateType.FullName, "Buttons");
        var hatField = _hmGamepadStateType.GetField("Hat")
            ?? throw new MissingFieldException(_hmGamepadStateType.FullName, "Hat");

        var buttonValue = Enum.Parse(_hmButtonType, buttonName ?? "None", ignoreCase: true);
        var hatValue = Enum.Parse(_hmHatType, hatName ?? "None", ignoreCase: true);

        buttonsField.SetValue(state, buttonValue);
        hatField.SetValue(state, hatValue);

        var args = new[] { state };
        _ = _submitStateMethod.Invoke(_controller, args);
    }

    private Type ResolveType(string fullName)
        => _assembly.GetType(fullName, throwOnError: true)
            ?? throw new InvalidOperationException($"Unable to resolve HIDMaestro type '{fullName}'.");
}
