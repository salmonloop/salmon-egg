using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SalmonEgg.Cli.Commands;
using SalmonEgg.Cli.Output;

namespace SalmonEgg.Cli.Tests;

public sealed class CliFailureBoundaryTests
{
    [Fact]
    public async Task RunAsync_WhenOutputFactoryThrows_MapsToFailureWithoutWritingExceptionMessage()
    {
        await using var stdout = new StringWriter();
        await using var stderr = new StringWriter();
        var output = new RecordingCliOutput();
        const string sensitiveMessage = "token=should-not-be-visible";

        var exitCode = await CliApplication.RunAsyncForTesting(
            ["--help"],
            stdout,
            stderr,
            _ => throw new InvalidOperationException(sensitiveMessage),
            output,
            TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Failure, exitCode);
        Assert.Empty(stderr.ToString());
        Assert.Equal($"CLI failed: {nameof(InvalidOperationException)}.", Assert.Single(output.Errors));
        Assert.DoesNotContain(sensitiveMessage, output.Errors[0], StringComparison.Ordinal);
    }

    private sealed class RecordingCliOutput : ICliOutput
    {
        public List<string> Errors { get; } = new();

        public Task WriteAsync(string message) => Task.CompletedTask;

        public Task WriteErrorAsync(string message)
        {
            Errors.Add(message);
            return Task.CompletedTask;
        }
    }
}
