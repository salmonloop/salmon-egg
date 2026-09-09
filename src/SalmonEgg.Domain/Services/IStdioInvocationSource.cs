using SalmonEgg.Domain.Models;

namespace SalmonEgg.Domain.Services;

/// <summary>Exposes only the invocation actually used by an existing local ACP process.</summary>
public interface IStdioInvocationSource
{
    StdioInvocationSnapshot? StdioInvocation { get; }
}
