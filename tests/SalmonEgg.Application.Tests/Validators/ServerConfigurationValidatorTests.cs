using SalmonEgg.Application.Validators;
using SalmonEgg.Domain.Models;
using Xunit;

namespace SalmonEgg.Application.Tests.Validators;

public sealed class ServerConfigurationValidatorTests
{
    [Fact]
    public void Validate_WhenStdioCommandMissing_ShouldMentionLauncherSupport()
    {
        var validator = new ServerConfigurationValidator();
        var configuration = new ServerConfiguration
        {
            Id = "ssh-bridge",
            Name = "SSH Bridge",
            Transport = TransportType.Stdio
        };

        var result = validator.Validate(configuration);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.ErrorMessage == "Stdio transport requires a command or launcher");
    }
}
