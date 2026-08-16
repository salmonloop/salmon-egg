namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// Infrastructure ActivitySource declarations are intentionally empty until authoritative
/// operation boundaries start activities. Keeping undeployed sources registered would promise
/// operators trace data that the application does not produce.
/// </summary>
public static class SalmonEggActivitySources
{
}
