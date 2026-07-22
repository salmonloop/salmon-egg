using System.Reflection;

const string HidMaestroCorePathEnvVar = "SALMONEGG_HIDMAESTRO_CORE_PATH";
const string HidMaestroProfileIdEnvVar = "SALMONEGG_HIDMAESTRO_PROFILE_ID";
const string DefaultProfileId = "xbox-360-wired";

if (args.Length != 1 || !string.Equals(args[0], "serve", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: SalmonEgg.GamepadBridge.Windows serve");
    return 1;
}

var hidMaestroCorePath = ResolveHidMaestroCorePath();
var hidMaestroProfileId = ResolveHidMaestroProfileId();
using var bridge = new HidMaestroBridge(hidMaestroCorePath, hidMaestroProfileId);

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

static string ResolveHidMaestroProfileId()
{
    var configured = Environment.GetEnvironmentVariable(HidMaestroProfileIdEnvVar);
    return string.IsNullOrWhiteSpace(configured)
        ? DefaultProfileId
        : configured.Trim();
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
    private readonly Assembly _assembly;
    private readonly object _context;
    private readonly string _profileId;
    private readonly Type _hmButtonType;
    private readonly Type _hmHatType;
    private readonly Type _hmAxisType;
    private readonly Type _hmGamepadStateType;
    private readonly MethodInfo _createControllerMethod;
    private readonly MethodInfo _removeAllVirtualControllersMethod;
    private readonly MethodInfo _loadDefaultProfilesMethod;
    private readonly MethodInfo _getProfileMethod;
    private readonly MethodInfo _submitStateMethod;
    private readonly PropertyInfo _isDriverInstalledProperty;

    private object? _controller;
    private object? _profile;

    public HidMaestroBridge(string hidMaestroCorePath, string profileId)
    {
        if (!File.Exists(hidMaestroCorePath))
        {
            throw new FileNotFoundException("HIDMaestro.Core.dll was not found.", hidMaestroCorePath);
        }

        _profileId = string.IsNullOrWhiteSpace(profileId)
            ? throw new ArgumentException("HIDMaestro profile id is required.", nameof(profileId))
            : profileId.Trim();

        _assembly = Assembly.LoadFrom(hidMaestroCorePath);
        var hmContextType = _assembly.GetType("HIDMaestro.HMContext", throwOnError: true)!;
        _context = Activator.CreateInstance(hmContextType)
            ?? throw new InvalidOperationException("Failed to create HIDMaestro.HMContext.");

        _hmButtonType = _assembly.GetType("HIDMaestro.HMButton", throwOnError: true)!;
        _hmHatType = _assembly.GetType("HIDMaestro.HMHat", throwOnError: true)!;
        _hmAxisType = _assembly.GetType("HIDMaestro.HMAxis", throwOnError: true)!;
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

        var profile = _getProfileMethod.Invoke(_context, [_profileId]);
        if (profile is null)
        {
            throw new InvalidOperationException($"Unable to resolve HIDMaestro profile '{_profileId}'.");
        }

        _controller = _createControllerMethod.Invoke(_context, [profile])
            ?? throw new InvalidOperationException("HIDMaestro failed to create a virtual controller.");

        // Keep the live controller profile so trigger submission can use profile.Triggers
        // (DualSense Rx/Ry) or digital L2/R2 indexes when the profile has no analog triggers
        // (Switch Pro). Do not invent profile ids here — callers choose via env/config.
        var profileProperty = _controller.GetType().GetProperty("Profile")
            ?? throw new MissingMemberException(_controller.GetType().FullName, "Profile");
        _profile = profileProperty.GetValue(_controller) ?? profile;
    }

    public void DisposeController()
    {
        if (_controller is not null)
        {
            try
            {
                SubmitState(buttonName: null, hatName: null);
            }
            catch
            {
            }
        }

        _controller = null;
        _profile = null;
        _ = _removeAllVirtualControllersMethod.Invoke(_context, null);
    }

    public void Press(string input)
    {
        EnsureControllerCreated();

        // Sticky press: hold the requested control until the next press or dispose so
        // diagnostics/GUI smokes can observe the live Windows.Gaming.Input reading while
        // the virtual controller is still active (app poll is ~50ms).
        SubmitState(buttonName: null, hatName: null);

        switch (input.ToLowerInvariant())
        {
            case "dpad-up":
                SubmitState(hatName: "North");
                break;
            case "dpad-down":
                SubmitState(hatName: "South");
                break;
            case "dpad-left":
                SubmitState(hatName: "West");
                break;
            case "dpad-right":
                SubmitState(hatName: "East");
                break;
            case "a":
                SubmitState(buttonName: "A");
                break;
            case "b":
                SubmitState(buttonName: "B");
                break;
            case "x":
                SubmitState(buttonName: "X");
                break;
            case "y":
                SubmitState(buttonName: "Y");
                break;
            case "lt":
            case "left-trigger":
            case "lefttrigger":
                // Profile-aware: analog Axes when profile.Triggers is non-empty (Xbox/DualSense),
                // otherwise digital descriptor button 6 (Switch Pro L2 click).
                SubmitState(buttonName: null, hatName: null, leftTrigger: 1f);
                break;
            case "rt":
            case "right-trigger":
            case "righttrigger":
                // Profile-aware: analog Axes when profile.Triggers is non-empty (Xbox/DualSense),
                // otherwise digital descriptor button 7 (Switch Pro R2 click).
                SubmitState(buttonName: null, hatName: null, rightTrigger: 1f);
                break;
            case "release":
                // Already cleared above.
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

    private void SubmitState(
        string? buttonName = null,
        string? hatName = null,
        float? leftTrigger = null,
        float? rightTrigger = null)
    {
        var state = Activator.CreateInstance(_hmGamepadStateType)
            ?? throw new InvalidOperationException("Failed to create HMGamepadState.");

        var buttonsField = _hmGamepadStateType.GetField("Buttons")
            ?? throw new MissingFieldException(_hmGamepadStateType.FullName, "Buttons");
        var hatField = _hmGamepadStateType.GetField("Hat")
            ?? throw new MissingFieldException(_hmGamepadStateType.FullName, "Hat");
        var axesField = _hmGamepadStateType.GetField("Axes")
            ?? throw new MissingFieldException(_hmGamepadStateType.FullName, "Axes");

        var buttonValue = Enum.Parse(_hmButtonType, buttonName ?? "None", ignoreCase: true);
        var hatValue = Enum.Parse(_hmHatType, hatName ?? "None", ignoreCase: true);

        // When the active profile has no analog trigger axes (Switch Pro full HID),
        // L2/R2 are digital descriptor buttons at indexes 6/7. Without a ButtonMap,
        // HMButton bit N maps to descriptor button N, so bit 6/7 light L2/R2.
        // Only use this when profile.Triggers is empty so Xbox Back/Start (bits 6/7)
        // are not mis-fired as triggers on analog-trigger profiles.
        if ((leftTrigger is not null || rightTrigger is not null)
            && !ProfileHasAnalogTriggers())
        {
            var digitalMask = Convert.ToUInt32(buttonValue);
            if (leftTrigger is not null)
            {
                digitalMask |= 1u << 6;
            }

            if (rightTrigger is not null)
            {
                digitalMask |= 1u << 7;
            }

            buttonValue = Enum.ToObject(_hmButtonType, digitalMask);
        }

        buttonsField.SetValue(state, buttonValue);
        hatField.SetValue(state, hatValue);

        // Analog triggers: write profile.Triggers field keys and canonical Z/Rz + Rx/Ry
        // so Xbox (Z/Rz), DualSense (Rx/Ry / axisMap), and other full pads all feed
        // HIDMaestro ResolveTrigger and the XUSB companion GIP path.
        if ((leftTrigger is not null || rightTrigger is not null)
            && ProfileHasAnalogTriggers())
        {
            axesField.SetValue(state, BuildTriggerAxes(leftTrigger, rightTrigger));
        }

        var args = new[] { state };
        _ = _submitStateMethod.Invoke(_controller, args);
    }

    private bool ProfileHasAnalogTriggers()
    {
        var triggers = GetProfileTriggers();
        return triggers is not null && triggers.Count > 0;
    }

    private System.Collections.IList? GetProfileTriggers()
    {
        if (_profile is null)
        {
            return null;
        }

        var triggersProperty = _profile.GetType().GetProperty("Triggers");
        if (triggersProperty is null)
        {
            return null;
        }

        return triggersProperty.GetValue(_profile) as System.Collections.IList;
    }

    private object BuildTriggerAxes(float? leftTrigger, float? rightTrigger)
    {
        var axesType = typeof(Dictionary<,>).MakeGenericType(_hmAxisType, typeof(float));
        var axes = Activator.CreateInstance(axesType)
            ?? throw new InvalidOperationException("Failed to create HMGamepadState.Axes dictionary.");
        var indexer = axesType.GetProperty("Item")
            ?? throw new MissingMemberException(axesType.FullName, "Item");

        void WriteAxis(string axisName, float value)
        {
            indexer.SetValue(axes, value, [Enum.Parse(_hmAxisType, axisName, ignoreCase: true)]);
        }

        void WriteProfileTriggerSlot(int slot, float value)
        {
            var triggers = GetProfileTriggers();
            if (triggers is null || slot >= triggers.Count || triggers[slot] is null)
            {
                return;
            }

            var axisProperty = triggers[slot]!.GetType().GetProperty("Axis")
                ?? throw new MissingMemberException(triggers[slot]!.GetType().FullName, "Axis");
            var axisValue = axisProperty.GetValue(triggers[slot])
                ?? throw new InvalidOperationException("Profile trigger Axis was null.");
            indexer.SetValue(axes, value, [axisValue]);
        }

        if (leftTrigger is float left)
        {
            // Canonical first (PadForge / older consumers), then profile field key.
            WriteAxis("Z", left);
            WriteAxis("Rx", left);
            WriteProfileTriggerSlot(0, left);
        }

        if (rightTrigger is float right)
        {
            WriteAxis("Rz", right);
            WriteAxis("Ry", right);
            WriteProfileTriggerSlot(1, right);
        }

        return axes;
    }

    private Type ResolveType(string fullName)
        => _assembly.GetType(fullName, throwOnError: true)
            ?? throw new InvalidOperationException($"Unable to resolve HIDMaestro type '{fullName}'.");
}
