using System;
using System.Collections.Generic;
using SalmonEgg.Application.Services.AcpSetup;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Application.Tests.Services.AcpSetup;

public class AcpLaunchPlanBuilderTests
{
    [Fact]
    public void Build_WithArgumentParameter_AppendsFlagAndValueAfterFixedArguments()
    {
        // Arrange
        var template = CreateTemplate(
            fixedArguments: new[] { "-y", "@scope/adapter" },
            parameters: new[] { CreateParameter("--model", AcpSetupParameterTarget.Argument) });

        // Act
        var plan = AcpLaunchPlanBuilder.Build(
            template,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["--model"] = "fast" });

        // Assert
        Assert.Equal(new[] { "-y", "@scope/adapter", "--model", "fast" }, plan.Arguments);
    }

    [Fact]
    public void Build_WithEnvironmentParameter_PlacesValueInEnvironmentNotArguments()
    {
        // Arrange
        var template = CreateTemplate(
            parameters: new[] { CreateParameter("AGENT_MODEL", AcpSetupParameterTarget.EnvironmentVariable) });

        // Act
        var plan = AcpLaunchPlanBuilder.Build(
            template,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["AGENT_MODEL"] = "pro" });

        // Assert
        Assert.Empty(plan.Arguments);
        Assert.Equal("pro", plan.Environment["AGENT_MODEL"]);
    }

    [Fact]
    public void Build_WithBlankValue_OmitsParameterEntirely()
    {
        // Arrange
        var template = CreateTemplate(
            parameters: new[]
            {
                CreateParameter("--model", AcpSetupParameterTarget.Argument),
                CreateParameter("AGENT_MODEL", AcpSetupParameterTarget.EnvironmentVariable)
            });

        // Act
        var plan = AcpLaunchPlanBuilder.Build(
            template,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["--model"] = "   ",
                ["AGENT_MODEL"] = string.Empty
            });

        // Assert
        Assert.Empty(plan.Arguments);
        Assert.Empty(plan.Environment);
    }

    [Fact]
    public void Build_WithSecretParameter_ThrowsSoCredentialIsNeverPersisted()
    {
        // Arrange
        var template = CreateTemplate(
            parameters: new[]
            {
                new AcpSetupParameterDefinition
                {
                    Key = "AGENT_TOKEN",
                    DisplayName = "AGENT_TOKEN",
                    Target = AcpSetupParameterTarget.EnvironmentVariable,
                    IsSecret = true
                }
            });
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["AGENT_TOKEN"] = "sk-live" };

        // Act
        var exception = Record.Exception(() => AcpLaunchPlanBuilder.Build(template, values));

        // Assert
        var invalidOperation = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("AGENT_TOKEN", invalidOperation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_WithParameterMatchingFixedEnvironmentName_LetsUserValueWin()
    {
        // Arrange
        var template = CreateTemplate(
            fixedEnvironment: new Dictionary<string, string>(StringComparer.Ordinal) { ["AGENT_MODEL"] = "default" },
            parameters: new[] { CreateParameter("AGENT_MODEL", AcpSetupParameterTarget.EnvironmentVariable) });

        // Act
        var plan = AcpLaunchPlanBuilder.Build(
            template,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["AGENT_MODEL"] = "chosen" });

        // Assert
        Assert.Equal("chosen", plan.Environment["AGENT_MODEL"]);
    }

    [Fact]
    public void CreatePrefilledValues_SeedsEveryParameterWithItsDefault()
    {
        // Arrange
        var template = CreateTemplate(
            parameters: new[]
            {
                new AcpSetupParameterDefinition
                {
                    Key = "--model",
                    DisplayName = "--model",
                    DefaultValue = "fast"
                },
                CreateParameter("AGENT_MODEL", AcpSetupParameterTarget.EnvironmentVariable)
            });

        // Act
        var values = AcpLaunchPlanBuilder.CreatePrefilledValues(template);

        // Assert
        Assert.Equal("fast", values["--model"]);
        Assert.Equal(string.Empty, values["AGENT_MODEL"]);
    }

    [Fact]
    public void CommandLineDisplay_WithoutArguments_RendersCommandAlone()
    {
        // Arrange
        var template = CreateTemplate();

        // Act
        var plan = AcpLaunchPlanBuilder.Build(template, parameterValues: null);

        // Assert
        Assert.Equal("npx", plan.CommandLineDisplay);
    }

    private static AcpLaunchTemplate CreateTemplate(
        IReadOnlyList<string>? fixedArguments = null,
        IReadOnlyDictionary<string, string>? fixedEnvironment = null,
        IReadOnlyList<AcpSetupParameterDefinition>? parameters = null)
        => new()
        {
            Command = "npx",
            FixedArguments = fixedArguments ?? Array.Empty<string>(),
            FixedEnvironment = fixedEnvironment ?? new Dictionary<string, string>(StringComparer.Ordinal),
            Parameters = parameters ?? Array.Empty<AcpSetupParameterDefinition>()
        };

    private static AcpSetupParameterDefinition CreateParameter(string key, AcpSetupParameterTarget target)
        => new()
        {
            Key = key,
            DisplayName = key,
            Target = target
        };
}
