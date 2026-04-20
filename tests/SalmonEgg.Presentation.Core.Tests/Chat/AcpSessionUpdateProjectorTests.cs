using System.Collections.Generic;
using System.Collections.ObjectModel;
using SalmonEgg.Domain.Models.Plan;
using SalmonEgg.Domain.Models.Protocol;
using SalmonEgg.Domain.Services;
using SalmonEgg.Presentation.Core.Services.Chat;
using Xunit;

namespace SalmonEgg.Presentation.Core.Tests.Chat;

public class AcpSessionUpdateProjectorTests
{
    [Fact]
    public void ProjectSessionLoad_MapsModesAndConfigOptions()
    {
        var projector = new AcpSessionUpdateProjector();
        var response = new SessionLoadResponse(
            modes: new SessionModesState
            {
                CurrentModeId = "agent",
                AvailableModes = new List<SessionMode>
                {
                    new() { Id = "agent", Name = "Agent", Description = "Agent mode" },
                    new() { Id = "plan", Name = "Plan", Description = "Plan mode" }
                }
            },
            configOptions: new List<ConfigOption>
            {
                new()
                {
                    Id = "mode",
                    Category = "mode",
                    CurrentValue = "agent",
                    Options = new List<ConfigOptionValue>
                    {
                        new() { Value = "agent", Name = "Agent" },
                        new() { Value = "plan", Name = "Plan" }
                    }
                }
            });

        var delta = projector.ProjectSessionLoad(response);

        Assert.Equal("agent", delta.SelectedModeId);
        Assert.Equal(2, delta.AvailableModes?.Count);
        Assert.True(delta.ShowConfigOptionsPanel);
        Assert.Single(delta.ConfigOptions!);
    }

    [Fact]
    public void ProjectSessionLoad_ConfigOptionsTakePrecedence()
    {
        var projector = new AcpSessionUpdateProjector();
        var response = new SessionLoadResponse(
            modes: new SessionModesState
            {
                CurrentModeId = "legacy-mode",
                AvailableModes = new List<SessionMode>
                {
                    new() { Id = "legacy-mode", Name = "Legacy" }
                }
            },
            configOptions: new List<ConfigOption>
            {
                new()
                {
                    Id = "mode",
                    Category = "mode",
                    CurrentValue = "config-mode",
                    Options = new List<ConfigOptionValue>
                    {
                        new() { Value = "config-mode", Name = "Config Mode" },
                        new() { Value = "legacy-mode", Name = "Legacy Mode" }
                    }
                }
            });

        var delta = projector.ProjectSessionLoad(response);

        Assert.Equal("config-mode", delta.SelectedModeId);
        Assert.Equal(2, delta.AvailableModes?.Count);
        Assert.True(delta.ShowConfigOptionsPanel);
    }

    [Fact]
    public void ProjectSessionLoad_FallsBackToLegacyModes_WhenConfigOptionsDoNotExposeModeSelector()
    {
        var projector = new AcpSessionUpdateProjector();
        var response = new SessionLoadResponse(
            modes: new SessionModesState
            {
                CurrentModeId = "yolo",
                AvailableModes = new List<SessionMode>
                {
                    new() { Id = "interactive", Name = "Interactive" },
                    new() { Id = "yolo", Name = "YOLO" }
                }
            },
            configOptions: new List<ConfigOption>
            {
                new()
                {
                    Id = "_salmonloop_permission_policy",
                    Name = "Permission policy",
                    CurrentValue = "ask",
                    Options = new List<ConfigOptionValue>
                    {
                        new() { Value = "ask", Name = "Ask user" },
                        new() { Value = "deny_all", Name = "Deny all" }
                    }
                }
            });

        var delta = projector.ProjectSessionLoad(response);

        Assert.Equal("yolo", delta.SelectedModeId);
        Assert.Equal(2, delta.AvailableModes?.Count);
        Assert.True(delta.ShowConfigOptionsPanel);
        Assert.Single(delta.ConfigOptions!);
    }

    [Fact]
    public void ProjectSessionLoad_WhenResponseIsEmpty_ReturnsEmptyDelta()
    {
        var projector = new AcpSessionUpdateProjector();

        var delta = projector.ProjectSessionLoad(SessionLoadResponse.Completed);

        Assert.Null(delta.AvailableModes);
        Assert.Null(delta.SelectedModeId);
        Assert.Null(delta.ConfigOptions);
        Assert.Null(delta.ShowConfigOptionsPanel);
    }

    [Fact]
    public void ProjectSessionNew_MapsModesAndConfigOptions()
    {
        var projector = new AcpSessionUpdateProjector();
        var response = new SessionNewResponse(
            sessionId: "remote-1",
            modes: new SessionModesState
            {
                CurrentModeId = "agent",
                AvailableModes = new List<SessionMode>
                {
                    new() { Id = "agent", Name = "Agent", Description = "Agent mode" },
                    new() { Id = "plan", Name = "Plan", Description = "Plan mode" }
                }
            },
            configOptions: new List<ConfigOption>
            {
                new()
                {
                    Id = "mode",
                    Category = "mode",
                    CurrentValue = "agent",
                    Options = new List<ConfigOptionValue>
                    {
                        new() { Value = "agent", Name = "Agent" },
                        new() { Value = "plan", Name = "Plan" }
                    }
                }
            });

        var delta = projector.ProjectSessionNew(response);

        Assert.Equal("agent", delta.SelectedModeId);
        Assert.Equal(2, delta.AvailableModes?.Count);
        Assert.True(delta.ShowConfigOptionsPanel);
        Assert.Single(delta.ConfigOptions!);
    }

    [Fact]
    public void ProjectSessionNew_ConfigOptionsTakePrecedence()
    {
        var projector = new AcpSessionUpdateProjector();
        var response = new SessionNewResponse(
            sessionId: "remote-1",
            modes: new SessionModesState
            {
                CurrentModeId = "legacy-mode",
                AvailableModes = new List<SessionMode>
                {
                    new() { Id = "legacy-mode", Name = "Legacy" },
                }
            },
            configOptions: new List<ConfigOption>
            {
                new()
                {
                    Id = "mode",
                    Category = "mode",
                    CurrentValue = "config-mode",
                    Options = new List<ConfigOptionValue>
                    {
                        new() { Value = "config-mode", Name = "Config Mode" },
                        new() { Value = "legacy-mode", Name = "Legacy Mode" }
                    }
                }
            });

        var delta = projector.ProjectSessionNew(response);

        Assert.Equal("config-mode", delta.SelectedModeId);
        Assert.Equal(2, delta.AvailableModes?.Count);
        Assert.True(delta.ShowConfigOptionsPanel);
        Assert.NotNull(delta.ConfigOptions);
        Assert.All(
            delta.AvailableModes!,
            mode => Assert.Contains(mode.ModeId, new[] { "config-mode", "legacy-mode" }));
    }

    [Fact]
    public void ProjectSessionNew_FallsBackToLegacyModes_WhenConfigOptionsDoNotExposeModeSelector()
    {
        var projector = new AcpSessionUpdateProjector();
        var response = new SessionNewResponse(
            sessionId: "remote-1",
            modes: new SessionModesState
            {
                CurrentModeId = "yolo",
                AvailableModes = new List<SessionMode>
                {
                    new() { Id = "interactive", Name = "Interactive" },
                    new() { Id = "yolo", Name = "YOLO" }
                }
            },
            configOptions: new List<ConfigOption>
            {
                new()
                {
                    Id = "_salmonloop_permission_policy",
                    Name = "Permission policy",
                    CurrentValue = "ask",
                    Options = new List<ConfigOptionValue>
                    {
                        new() { Value = "ask", Name = "Ask user" },
                        new() { Value = "deny_all", Name = "Deny all" }
                    }
                },
                new()
                {
                    Id = "_salmonloop_mode",
                    Name = "Session Mode",
                    CurrentValue = "yolo",
                    Options = new List<ConfigOptionValue>
                    {
                        new() { Value = "interactive", Name = "Interactive" },
                        new() { Value = "yolo", Name = "YOLO" }
                    }
                }
            });

        var delta = projector.ProjectSessionNew(response);

        Assert.Equal("yolo", delta.SelectedModeId);
        Assert.Equal(2, delta.AvailableModes?.Count);
        Assert.Contains(delta.AvailableModes!, mode => mode.ModeId == "interactive");
        Assert.Contains(delta.AvailableModes!, mode => mode.ModeId == "yolo");
        Assert.True(delta.ShowConfigOptionsPanel);
        Assert.Equal(2, delta.ConfigOptions?.Count);
    }

    [Fact]
    public void Project_ConfigOptionUpdate_MapsFullState()
    {
        var projector = new AcpSessionUpdateProjector();
        var update = new ConfigOptionUpdate
        {
            ConfigOptions = new List<ConfigOption>
            {
                new()
                {
                    Id = "mode",
                    Category = "mode",
                    CurrentValue = "config-mode",
                    Options = new List<ConfigOptionValue>
                    {
                        new() { Value = "config-mode", Name = "Config Mode" }
                    }
                }
            }
        };

        var delta = projector.Project(new SessionUpdateEventArgs("remote-1", update));

        Assert.NotNull(delta.ConfigOptions);
        Assert.True(delta.ShowConfigOptionsPanel);
        Assert.Equal("config-mode", delta.SelectedModeId);
    }

    [Fact]
    public void Project_MapsPlanUpdateToPlanPanelProjection()
    {
        var projector = new AcpSessionUpdateProjector();
        var args = new SessionUpdateEventArgs(
            "remote-1",
            new PlanUpdate(
                entries: new List<PlanEntry>
                {
                    new() { Content = "Step 1", Status = PlanEntryStatus.Pending, Priority = PlanEntryPriority.High }
                },
                title: "My plan"));

        var delta = projector.Project(args);

        Assert.True(delta.ShowPlanPanel);
        Assert.Equal("My plan", delta.PlanTitle);
        Assert.Single(delta.PlanEntries!);
    }

    [Fact]
    public void Project_AvailableCommandsUpdate_MapsSessionScopedCommandSnapshot()
    {
        // Arrange
        var projector = new AcpSessionUpdateProjector();
        var args = new SessionUpdateEventArgs(
            "remote-1",
            new AvailableCommandsUpdate
            {
                AvailableCommands = new List<AvailableCommand>
                {
                    new()
                    {
                        Name = "plan",
                        Description = "Show the current plan",
                        Input = new AvailableCommandInput
                        {
                            Hint = "scope"
                        }
                    }
                }
            });

        // Act
        var delta = projector.Project(args);

        // Assert
        var command = Assert.Single(delta.AvailableCommands!);
        Assert.Equal("plan", command.Name);
        Assert.Equal("Show the current plan", command.Description);
        Assert.Equal("scope", command.InputHint);
    }

    [Fact]
    public void Project_SessionInfoUpdate_MapsSessionMetadataSnapshot()
    {
        // Arrange
        var projector = new AcpSessionUpdateProjector();
        var updatedAt = "2026-04-20T03:04:05Z";
        var args = new SessionUpdateEventArgs(
            "remote-1",
            new SessionInfoUpdate
            {
                Title = "Remote session",
                Description = "ACP metadata",
                Cwd = @"C:\repo\remote",
                UpdatedAt = updatedAt,
                Meta = new Dictionary<string, object?>
                {
                    ["profileId"] = "profile-1"
                }
            });

        // Act
        var delta = projector.Project(args);

        // Assert
        Assert.NotNull(delta.SessionInfo);
        Assert.Equal("Remote session", delta.SessionInfo.Title);
        Assert.Equal("ACP metadata", delta.SessionInfo.Description);
        Assert.Equal(@"C:\repo\remote", delta.SessionInfo.Cwd);
        Assert.Equal(updatedAt, delta.SessionInfo.UpdatedAt);
        Assert.Equal("profile-1", delta.SessionInfo.Meta!["profileId"]);
    }

    [Fact]
    public void Project_SessionInfoUpdate_MetaSnapshot_IsDetachedAndReadOnly()
    {
        // Arrange
        var projector = new AcpSessionUpdateProjector();
        var meta = new Dictionary<string, object?>
        {
            ["profileId"] = "profile-1"
        };
        var args = new SessionUpdateEventArgs(
            "remote-1",
            new SessionInfoUpdate
            {
                Meta = meta
            });

        // Act
        var delta = projector.Project(args);
        meta["profileId"] = "profile-2";

        // Assert
        Assert.Equal("profile-1", delta.SessionInfo!.Meta!["profileId"]);
        var mutableMeta = Assert.IsAssignableFrom<IDictionary<string, object?>>(delta.SessionInfo.Meta);
        Assert.Throws<NotSupportedException>(() => mutableMeta["profileId"] = "profile-3");
        Assert.IsType<ReadOnlyDictionary<string, object?>>(delta.SessionInfo.Meta);
    }

    [Fact]
    public void Project_SessionInfoUpdate_WhenMetaIsNull_MapsNullMetaSnapshot()
    {
        // Arrange
        var projector = new AcpSessionUpdateProjector();
        var args = new SessionUpdateEventArgs(
            "remote-1",
            new SessionInfoUpdate
            {
                Title = "Remote session",
                Meta = null
            });

        // Act
        var delta = projector.Project(args);

        // Assert
        Assert.NotNull(delta.SessionInfo);
        Assert.Equal("Remote session", delta.SessionInfo.Title);
        Assert.Null(delta.SessionInfo.Meta);
    }

    [Fact]
    public void Project_UsageUpdate_MapsTypedUsageSnapshot()
    {
        // Arrange
        var projector = new AcpSessionUpdateProjector();
        var args = new SessionUpdateEventArgs(
            "remote-1",
            new UsageUpdate
            {
                Used = 64,
                Size = 128,
                Cost = new UsageCost
                {
                    Amount = 1.25m,
                    Currency = "USD"
                }
            });

        // Act
        var delta = projector.Project(args);

        // Assert
        Assert.NotNull(delta.Usage);
        Assert.Equal(64, delta.Usage.Used);
        Assert.Equal(128, delta.Usage.Size);
        Assert.NotNull(delta.Usage.Cost);
        Assert.Equal(1.25m, delta.Usage.Cost.Amount);
        Assert.Equal("USD", delta.Usage.Cost.Currency);
    }

    [Fact]
    public void Project_UsageUpdate_WhenCostIsNull_MapsUsageWithoutCostSnapshot()
    {
        // Arrange
        var projector = new AcpSessionUpdateProjector();
        var args = new SessionUpdateEventArgs(
            "remote-1",
            new UsageUpdate
            {
                Used = 64,
                Size = 128,
                Cost = null
            });

        // Act
        var delta = projector.Project(args);

        // Assert
        Assert.NotNull(delta.Usage);
        Assert.Equal(64, delta.Usage.Used);
        Assert.Equal(128, delta.Usage.Size);
        Assert.Null(delta.Usage.Cost);
    }

    [Fact]
    public void Project_AvailableCommandsUpdate_SnapshotIsDetachedFromCallerOwnedInput()
    {
        // Arrange
        var projector = new AcpSessionUpdateProjector();
        var availableCommand = new AvailableCommand
        {
            Name = "plan",
            Description = "Show the current plan",
            Input = new AvailableCommandInput
            {
                Hint = "scope"
            }
        };
        var update = new AvailableCommandsUpdate
        {
            AvailableCommands = new List<AvailableCommand>
            {
                availableCommand
            }
        };

        // Act
        var delta = projector.Project(new SessionUpdateEventArgs("remote-1", update));
        availableCommand.Name = "apply";
        availableCommand.Description = "Apply the plan";
        availableCommand.Input!.Hint = "target";
        update.AvailableCommands.Clear();

        // Assert
        var command = Assert.Single(delta.AvailableCommands!);
        Assert.Equal("plan", command.Name);
        Assert.Equal("Show the current plan", command.Description);
        Assert.Equal("scope", command.InputHint);
    }

    [Fact]
    public void Project_CurrentModeUpdate_MapsOfficialModeIdPayload()
    {
        var projector = new AcpSessionUpdateProjector();
        var delta = projector.Project(new SessionUpdateEventArgs(
            "remote-1",
            new CurrentModeUpdate
            {
                LegacyModeId = "code"
            }));

        Assert.Equal("code", delta.SelectedModeId);
    }

    [Fact]
    public void Project_ConfigOptionUpdate_MapsOfficialSessionModeExample()
    {
        var projector = new AcpSessionUpdateProjector();
        var delta = projector.Project(new SessionUpdateEventArgs(
            "remote-1",
            new ConfigOptionUpdate
            {
                ConfigOptions = new List<ConfigOption>
                {
                    new()
                    {
                        Id = "mode",
                        Name = "Session Mode",
                        Type = "select",
                        CurrentValue = "code",
                        Options = new List<ConfigOptionValue>
                        {
                            new() { Value = "code", Name = "Code" },
                            new() { Value = "plan", Name = "Plan" }
                        }
                    }
                }
            }));

        Assert.True(delta.ShowConfigOptionsPanel);
        Assert.Equal("code", delta.SelectedModeId);
        Assert.Equal(2, delta.AvailableModes?.Count);
        Assert.Equal("code", delta.AvailableModes![0].ModeId);
        Assert.Single(delta.ConfigOptions!);
    }

    [Fact]
    public void Project_AvailableCommandsUpdate_MapsOfficialCommandsExample()
    {
        var projector = new AcpSessionUpdateProjector();
        var delta = projector.Project(new SessionUpdateEventArgs(
            "remote-1",
            new AvailableCommandsUpdate
            {
                AvailableCommands = new List<AvailableCommand>
                {
                    new()
                    {
                        Name = "web",
                        Description = "Search the web for information",
                        Input = new AvailableCommandInput
                        {
                            Hint = "query to search for"
                        }
                    },
                    new()
                    {
                        Name = "test",
                        Description = "Run the project's tests",
                        Input = new AvailableCommandInput
                        {
                            Hint = "test command"
                        }
                    }
                }
            }));

        Assert.NotNull(delta.AvailableCommands);
        Assert.Equal(2, delta.AvailableCommands!.Count);
        Assert.Equal("web", delta.AvailableCommands[0].Name);
        Assert.Equal("query to search for", delta.AvailableCommands[0].InputHint);
    }

    [Fact]
    public void Project_SessionInfoUpdate_MapsOfficialPartialPayload()
    {
        var projector = new AcpSessionUpdateProjector();
        var delta = projector.Project(new SessionUpdateEventArgs(
            "remote-1",
            new SessionInfoUpdate
            {
                Title = "Debug authentication timeout",
                Meta = new Dictionary<string, object?>
                {
                    ["projectName"] = "api-server",
                    ["branch"] = "main"
                }
            }));

        Assert.NotNull(delta.SessionInfo);
        Assert.Equal("Debug authentication timeout", delta.SessionInfo!.Title);
        Assert.Null(delta.SessionInfo.Description);
        Assert.Null(delta.SessionInfo.Cwd);
        Assert.Null(delta.SessionInfo.UpdatedAt);
        Assert.Equal("api-server", delta.SessionInfo.Meta!["projectName"]);
        Assert.Equal("main", delta.SessionInfo.Meta["branch"]);
    }

    [Fact]
    public void Project_UsageUpdate_MapsOfficialMinimalPayload()
    {
        var projector = new AcpSessionUpdateProjector();
        var delta = projector.Project(new SessionUpdateEventArgs(
            "remote-1",
            new UsageUpdate
            {
                Used = 53000,
                Size = 200000
            }));

        Assert.NotNull(delta.Usage);
        Assert.Equal(53000, delta.Usage!.Used);
        Assert.Equal(200000, delta.Usage.Size);
        Assert.Null(delta.Usage.Cost);
    }
}
