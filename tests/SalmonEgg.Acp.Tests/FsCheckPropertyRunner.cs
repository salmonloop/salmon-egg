using System.Reflection;
using FsCheck;

namespace SalmonEgg.Acp.Tests;

internal static class FsCheckPropertyRunner
{
    private static readonly Config PropertyConfig = Config.QuickThrowOnFailure.WithQuietOnSuccess(true);

    public static void Run(object instance, string propertyMethodName)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (string.IsNullOrWhiteSpace(propertyMethodName))
        {
            throw new ArgumentException("Property method name is required.", nameof(propertyMethodName));
        }

        var propertyMethod = instance.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(method => string.Equals(method.Name, propertyMethodName, StringComparison.Ordinal));

        if (propertyMethod is null)
        {
            throw new InvalidOperationException($"Could not find FsCheck property method '{propertyMethodName}'.");
        }

        Check.Method(
            PropertyConfig,
            propertyMethod,
            Microsoft.FSharp.Core.FSharpOption<object>.Some(instance));
    }
}
