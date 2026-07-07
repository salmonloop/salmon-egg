namespace SalmonEgg.Domain.Models;

public sealed record CloudConfigProviderDescriptor(
    string ProviderId,
    string DisplayName,
    bool IsConfigured);
