namespace SalmonEgg.Infrastructure.Observability;

/// <summary>
/// Infrastructure metric declarations are intentionally empty until authoritative operation
/// boundaries emit bounded measurements. Keeping undeployed instruments registered would promise
/// operators data that the application does not produce.
/// </summary>
public static class SalmonEggMeters
{
}
