using System;
using System.Collections.Generic;
using SalmonEgg.Application.Services.AcpSetup;
using SalmonEgg.Domain.Models.AcpSetup;

namespace SalmonEgg.Application.Tests.Services.AcpSetup;

public class AcpSetupParameterValidatorTests
{
    [Fact]
    public void Validate_WhenRequiredValueMissing_ReportsViolationForThatParameter()
    {
        // Arrange
        var template = CreateTemplate(new AcpSetupParameterDefinition
        {
            Key = "--project",
            DisplayName = "--project",
            IsRequired = true
        });

        // Act
        var violations = AcpSetupParameterValidator.Validate(template, parameterValues: null);

        // Assert
        var violation = Assert.Single(violations);
        Assert.Equal("--project", violation.ParameterKey);
        Assert.Equal(AcpSetupParameterValidator.MissingRequiredValueKey, violation.MessageKey);
    }

    [Fact]
    public void Validate_WhenRequiredValueIsWhitespace_TreatsItAsMissing()
    {
        // Arrange
        var template = CreateTemplate(new AcpSetupParameterDefinition
        {
            Key = "--project",
            DisplayName = "--project",
            IsRequired = true
        });
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["--project"] = "  " };

        // Act
        var violations = AcpSetupParameterValidator.Validate(template, values);

        // Assert
        Assert.Equal(AcpSetupParameterValidator.MissingRequiredValueKey, Assert.Single(violations).MessageKey);
    }

    [Fact]
    public void Validate_WhenOptionalValueMissing_ReportsNothing()
    {
        // Arrange
        var template = CreateTemplate(new AcpSetupParameterDefinition
        {
            Key = "--model",
            DisplayName = "--model"
        });

        // Act
        var violations = AcpSetupParameterValidator.Validate(template, parameterValues: null);

        // Assert
        Assert.Empty(violations);
    }

    [Fact]
    public void Validate_WhenValueOutsideAllowedSet_ReportsViolation()
    {
        // Arrange
        var template = CreateTemplate(new AcpSetupParameterDefinition
        {
            Key = "--mode",
            DisplayName = "--mode",
            AllowedValues = new[] { "fast", "thorough" }
        });
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["--mode"] = "turbo" };

        // Act
        var violations = AcpSetupParameterValidator.Validate(template, values);

        // Assert
        Assert.Equal(AcpSetupParameterValidator.ValueNotAllowedKey, Assert.Single(violations).MessageKey);
    }

    [Fact]
    public void Validate_WhenValueInsideAllowedSet_ReportsNothing()
    {
        // Arrange
        var template = CreateTemplate(new AcpSetupParameterDefinition
        {
            Key = "--mode",
            DisplayName = "--mode",
            AllowedValues = new[] { "fast", "thorough" }
        });
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["--mode"] = "thorough" };

        // Act
        var violations = AcpSetupParameterValidator.Validate(template, values);

        // Assert
        Assert.Empty(violations);
    }

    private static AcpLaunchTemplate CreateTemplate(params AcpSetupParameterDefinition[] parameters)
        => new()
        {
            Command = "npx",
            Parameters = parameters
        };
}
