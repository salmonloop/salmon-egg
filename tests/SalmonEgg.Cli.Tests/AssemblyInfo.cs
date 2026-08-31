using Xunit;

// Handler tests point SALMONEGG_APPDATA_ROOT at a per-test temp directory via a process-wide
// environment variable, so two test classes running concurrently would read each other's config
// root. Mirrors the same guard used by SalmonEgg.Infrastructure.Tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
