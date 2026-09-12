using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 1 || !OperatingSystem.IsWindows()) return 2;
        Directory.CreateDirectory(args[0]);
        var path = Path.Combine(args[0], "desktop-preflight.json");
        using var process = Process.GetCurrentProcess();
        var inputDesktop = NativeDesktopAcceptance.OpenInputDesktop(0, false, 0x0100);
        var evidence = new DesktopEvidence(process.Id, process.SessionId, Environment.UserInteractive,
            NativeDesktopAcceptance.Name(NativeDesktopAcceptance.GetProcessWindowStation()),
            inputDesktop == IntPtr.Zero ? "<unavailable>" : NativeDesktopAcceptance.Name(inputDesktop), false, false);
        if (inputDesktop != IntPtr.Zero) NativeDesktopAcceptance.CloseDesktop(inputDesktop);
        Save(path, evidence);
        try
        {
            var result = NativeDesktopAcceptance.RunKeyboardWindow();
            evidence = evidence with { NativeInputSent = result[0], NativeTextReceived = result[1] };
            Save(path, evidence);
            return evidence.Passed ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error.GetType().Name + ": " + error.Message);
            return 1;
        }
    }

    private static void Save(string path, DesktopEvidence evidence)
    {
        var json = JsonSerializer.Serialize(evidence, ProbeJsonContext.Default.DesktopEvidence);
        File.WriteAllText(path, json);
        Console.WriteLine(json);
    }
}

internal sealed record DesktopEvidence(int ProcessId, int SessionId, bool UserInteractive,
    string WindowStation, string InputDesktop, bool NativeInputSent, bool NativeTextReceived)
{
    public bool Passed => NativeInputSent && NativeTextReceived;
}

[JsonSerializable(typeof(DesktopEvidence))]
internal sealed partial class ProbeJsonContext : JsonSerializerContext;
