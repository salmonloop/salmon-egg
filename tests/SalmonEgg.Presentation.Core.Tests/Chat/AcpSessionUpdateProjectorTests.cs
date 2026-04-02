using System.Collections.Generic;
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
}
