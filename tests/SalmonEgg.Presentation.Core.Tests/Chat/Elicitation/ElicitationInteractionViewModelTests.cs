using System.Text.Json;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Presentation.Core.Tests.Localization;
using SalmonEgg.Presentation.ViewModels.Chat.Elicitation;

namespace SalmonEgg.Presentation.Core.Tests.Chat.Elicitation;

public sealed class ElicitationInteractionViewModelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SubmitAsync_WhenRequiredFieldCannotBeRendered_NeverAccepts(bool declaredProperty)
    {
        var schema = new ElicitationSchema { Required = ["future"] };
        if (declaredProperty)
        {
            schema.Properties.Add("future", new CustomPropertySchema { SchemaType = "_future" });
        }

        var accepted = 0;
        var cancelled = 0;
        var cleared = 0;
        var request = Create(schema,
            _ => { accepted++; return Task.FromResult(true); },
            () => { cancelled++; return Task.FromResult(true); },
            () => { cleared++; return Task.CompletedTask; });

        // Executing the command directly must obey the same contract as the disabled button.
        await request.SubmitCommand.ExecuteAsync(null);

        Assert.Equal(0, accepted);
        Assert.False(request.CanSubmit);
        Assert.True(request.HasError);
        Assert.Equal(0, cleared);
        await request.CancelCommand.ExecuteAsync(null);
        Assert.Equal(1, cancelled);
        Assert.Equal(1, cleared);
    }

    [Fact]
    public async Task SubmitAsync_WhenOnlyOptionalFieldIsUnknown_OmitsItAndPreservesKnownValues()
    {
        var schema = new ElicitationSchema
        {
            Properties = new()
            {
                ["future"] = new CustomPropertySchema { SchemaType = "_future" },
                ["name"] = new StringPropertySchema { Default = "Salmon" },
                ["count"] = new IntegerPropertySchema { Default = 3 },
                ["ratio"] = new NumberPropertySchema { Default = 0.5 },
                ["enabled"] = new BooleanPropertySchema { Default = true },
                ["targets"] = new MultiSelectPropertySchema
                {
                    Items = new StringMultiSelectItems { Enum = ["api", "ui"] },
                    Default = ["api"]
                }
            },
            Required = ["name", "enabled"]
        };
        ElicitationAcceptContent? accepted = null;
        var request = Create(schema, content => { accepted = content; return Task.FromResult(true); });

        Assert.True(request.CanSubmit);
        await request.SubmitCommand.ExecuteAsync(null);

        Assert.NotNull(accepted);
        Assert.Equal(5, request.Fields.Count);
        Assert.False(accepted.Values.ContainsKey("future"));
        Assert.Equal("Salmon", accepted.Values["name"].RawValue.GetString());
        Assert.Equal(JsonValueKind.Number, accepted.Values["count"].RawValue.ValueKind);
        Assert.Equal(3, accepted.Values["count"].RawValue.GetInt64());
        Assert.Equal(0.5, accepted.Values["ratio"].RawValue.GetDouble());
        Assert.True(accepted.Values["enabled"].RawValue.GetBoolean());
        Assert.Equal("[\"api\"]", accepted.Values["targets"].RawValue.GetRawText());
    }

    [Theory]
    [InlineData("string")]
    [InlineData("integer")]
    [InlineData("number")]
    [InlineData("array")]
    public void ReprojectLocalizedState_WhenValidationErrorIsVisible_RefreshesLanguage(string kind)
    {
        var localizer = new MutableTestCoreStringLocalizer();
        localizer.Set("zh-Hans", "Elicitation_InvalidValue", "请检查填写内容");
        localizer.Set("en-US", "Elicitation_InvalidValue", "Check this value");
        ElicitationPropertySchema property = kind switch
        {
            "integer" => new IntegerPropertySchema(),
            "number" => new NumberPropertySchema(),
            "array" => new MultiSelectPropertySchema(),
            _ => new StringPropertySchema()
        };
        var schema = new ElicitationSchema { Properties = new() { ["value"] = property }, Required = ["value"] };
        var args = CreateArgs(schema, _ => Task.FromResult(true));
        var request = ElicitationInteractionViewModelFactory.Create(args, _ => Task.CompletedTask, localizer);
        var field = Assert.Single(request.Fields);

        Assert.False(field.Validate());
        Assert.Equal("请检查填写内容", field.ErrorMessage);
        localizer.SetLanguageTag("en-US");
        request.ReprojectLocalizedState();

        Assert.Equal("Check this value", field.ErrorMessage);
    }

    [Fact]
    public async Task SubmitAsync_WhenResponseFails_KeepsFormForRetry()
    {
        var attempts = 0;
        var cleared = 0;
        var request = Create(new ElicitationSchema(),
            _ => Task.FromResult(++attempts > 1),
            clear: () => { cleared++; return Task.CompletedTask; });

        await request.SubmitCommand.ExecuteAsync(null);
        Assert.True(request.HasError);
        Assert.Equal(0, cleared);
        await request.SubmitCommand.ExecuteAsync(null);
        Assert.Equal(2, attempts);
        Assert.Equal(1, cleared);
        Assert.False(request.HasError);
    }

    [Fact]
    public async Task CanSubmit_AfterUnsupportedFormCancelFails_PreservesResponseFailure()
    {
        var request = Create(new ElicitationSchema { Required = ["future"] },
            _ => Task.FromResult(true), () => Task.FromResult(false));

        await request.CancelCommand.ExecuteAsync(null);
        var responseError = request.ErrorMessage;

        Assert.Contains("response could not be sent", responseError, StringComparison.Ordinal);
        Assert.False(request.CanSubmit);
        Assert.Equal(responseError, request.ErrorMessage);
    }

    private static ElicitationRequestViewModel Create(
        ElicitationSchema schema,
        Func<ElicitationAcceptContent?, Task<bool>> accept,
        Func<Task<bool>>? cancel = null,
        Func<Task>? clear = null)
        => ElicitationInteractionViewModelFactory.Create(
            CreateArgs(schema, accept, cancel), _ => clear?.Invoke() ?? Task.CompletedTask);

    private static ElicitationRequestEventArgs CreateArgs(
        ElicitationSchema schema,
        Func<ElicitationAcceptContent?, Task<bool>> accept,
        Func<Task<bool>>? cancel = null)
        => new("request-1", new FormElicitationRequest
        {
            Scope = ElicitationScope.ForSession("session-1"),
            Message = "Choose settings",
            RequestedSchema = schema
        }, accept, () => Task.FromResult(true), cancel ?? (() => Task.FromResult(true)));
}
