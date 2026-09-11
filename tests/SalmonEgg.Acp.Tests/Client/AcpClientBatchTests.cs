using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SalmonEgg.Acp.Client;
using SalmonEgg.Acp.JsonRpc;
using SalmonEgg.Acp.Protocol;
using SalmonEgg.Acp.Serialization;
using Xunit;

namespace SalmonEgg.Acp.Tests.Client;

public sealed class AcpClientBatchTests
{
    private static CancellationToken TestToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Batch_MixedCallsAndNotifications_AggregatesOnlyRequestResponses()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var forms = new List<ElicitationRequestEventArgs>();
        peer.Client.ElicitationRequestReceived += (_, form) => forms.Add(form);

        // Act
        peer.Deliver("[" + Form("1") + "," + Notification("_unknown") + "," + Form("\"1\"") + "]");

        // Assert
        Assert.Equal(2, forms.Count);
        Assert.Empty(peer.Responses);
        var second = forms[1].Decline();
        Assert.False(second.IsCompleted);
        Assert.Empty(peer.Responses);
        var first = forms[0].Cancel();
        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        var response = Assert.Single(peer.Responses);
        Assert.Equal(JsonValueKind.Array, response.ValueKind);
        Assert.Equal(2, response.GetArrayLength());
        Assert.Equal(JsonValueKind.Number, response[0].GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.String, response[1].GetProperty("id").ValueKind);
        Assert.Equal("cancel", response[0].GetProperty("result").GetProperty("action").GetString());
        Assert.Equal("decline", response[1].GetProperty("result").GetProperty("action").GetString());
        Assert.False(await forms[0].Accept(null));
        Assert.Empty(peer.Errors);
    }

    [Fact]
    public async Task Batch_OnlyNotifications_SendsNoResponse()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();

        // Act
        peer.Deliver("[" + Notification("_unknown") + "," + Cancel("99") + "]");

        // Assert
        Assert.Empty(peer.Responses);
        Assert.Empty(peer.Errors);
    }

    [Theory]
    [InlineData("[]", JsonRpcErrorCode.InvalidRequest)]
    [InlineData("[{\"jsonrpc\":", JsonRpcErrorCode.ParseError)]
    public async Task Batch_InvalidWholeFrame_RespondsWithSingleNullIdError(string frame, int code)
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();

        // Act
        peer.Deliver(frame);

        // Assert
        var response = Assert.Single(peer.Responses);
        Assert.Equal(JsonValueKind.Object, response.ValueKind);
        Assert.Equal(JsonValueKind.Null, response.GetProperty("id").ValueKind);
        Assert.Equal(code, response.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Batch_InvalidItems_DoNotDiscardValidCalls()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();

        // Act
        peer.Deliver("""
            [1, {"method":"_missing","id":1}, {"jsonrpc":"2.0","method":2},
             {"jsonrpc":"2.0","method":"_missing","id":true},
             {"jsonrpc":"2.0","method":"_missing","id":"kept"},
             {"jsonrpc":"2.0","method":"_ignored"}]
            """);

        // Assert
        var response = Assert.Single(peer.Responses);
        Assert.Equal(5, response.GetArrayLength());
        foreach (var invalid in response.EnumerateArray().Take(4))
        {
            Assert.Equal(JsonValueKind.Null, invalid.GetProperty("id").ValueKind);
            Assert.Equal(JsonRpcErrorCode.InvalidRequest, invalid.GetProperty("error").GetProperty("code").GetInt32());
        }
        Assert.Equal("kept", response[4].GetProperty("id").GetString());
        Assert.Equal(JsonRpcErrorCode.MethodNotFound, response[4].GetProperty("error").GetProperty("code").GetInt32());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"\"")]
    [InlineData("\" \"")]
    public async Task Batch_OpaqueRequestId_EchoesExactWireType(string id)
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        ElicitationRequestEventArgs? form = null;
        peer.Client.ElicitationRequestReceived += (_, value) => form = value;

        // Act
        peer.Deliver("[" + Form(id) + "]");

        // Assert
        Assert.NotNull(form);
        Assert.True(await form.Cancel().WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.Equal(id, Assert.Single(peer.Responses)[0].GetProperty("id").GetRawText());
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1)]
    [InlineData(0)]
    public async Task Batch_WithoutNegotiatedV2_IsRejectedWithoutDispatch(int version)
    {
        // Arrange
        using var peer = version == 0 ? new BatchPeer(AcpProtocolVersion.V2) : await BatchPeer.CreateAsync(version);
        var delivered = false;
        peer.Client.ElicitationRequestReceived += (_, _) => delivered = true;

        // Act
        peer.Deliver("[" + Form("11") + "]");

        // Assert
        Assert.False(delivered);
        var response = Assert.Single(peer.Responses);
        Assert.Equal(JsonValueKind.Object, response.ValueKind);
        Assert.Equal(JsonValueKind.Null, response.GetProperty("id").ValueKind);
        Assert.Equal(JsonRpcErrorCode.InvalidRequest, response.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Batch_ResponsesOutOfOrder_MatchesIdsWithoutStringCoercion()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var first = peer.Client.CreateSessionAsync(new SessionNewParams("/workspace", []), TestToken);
        var second = peer.Client.CreateSessionAsync(new SessionNewParams("/workspace", []), TestToken);
        var requests = peer.SessionRequests.ToArray();

        // Act
        peer.Deliver("[{\"jsonrpc\":\"2.0\",\"id\":\"" + requests[0].Id + "\",\"result\":{\"sessionId\":\"wrong\"}}]");
        Assert.False(first.IsCompleted);
        peer.Deliver("[" + SessionResponse(requests[1], "second") + "," + SessionResponse(requests[0], "first") + "]");

        // Assert
        Assert.Equal("first", (await first.WaitAsync(TimeSpan.FromSeconds(5), TestToken)).SessionId);
        Assert.Equal("second", (await second.WaitAsync(TimeSpan.FromSeconds(5), TestToken)).SessionId);
        Assert.Empty(peer.Responses);
    }

    [Fact]
    public async Task Batch_ResponseSmuggledIntoCalls_DoesNotSettleRequestButKeepsCalls()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var pending = peer.Client.CreateSessionAsync(new SessionNewParams("/workspace", []), TestToken);
        var request = Assert.Single(peer.SessionRequests);

        // Act
        peer.Deliver("[" + SessionResponse(request, "smuggled")
            + ",{\"jsonrpc\":\"2.0\",\"method\":\"_missing\",\"id\":\"call\"}]");

        // Assert
        Assert.False(pending.IsCompleted);
        var response = Assert.Single(peer.Responses);
        Assert.Equal(1, response.GetArrayLength());
        Assert.Equal("call", response[0].GetProperty("id").GetString());
        peer.Deliver("[" + SessionResponse(request, "real") + "]");
        Assert.Equal("real", (await pending.WaitAsync(TimeSpan.FromSeconds(5), TestToken)).SessionId);
    }

    [Fact]
    public async Task Batch_InvalidResponseItem_DoesNotDiscardValidResponsesOrReplyToResponses()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var pending = peer.Client.CreateSessionAsync(new SessionNewParams("/workspace", []), TestToken);
        var request = Assert.Single(peer.SessionRequests);

        // Act
        peer.Deliver("[17,{\"jsonrpc\":\"2.0\",\"id\":9,\"result\":{},\"error\":{\"code\":-1,\"message\":\"bad\"}},"
            + SessionResponse(request, "kept") + "]");

        // Assert
        Assert.Equal("kept", (await pending.WaitAsync(TimeSpan.FromSeconds(5), TestToken)).SessionId);
        Assert.Empty(peer.Responses);
    }

    [Fact]
    public async Task Batch_CancelNotification_CancelsOnlyMatchingTypedRequest()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var forms = new List<ElicitationRequestEventArgs>();
        peer.Client.ElicitationRequestReceived += (_, form) => forms.Add(form);

        // Act
        peer.Deliver("[" + Form("5") + "," + Form("\"5\"") + "," + Cancel("5") + "]");

        // Assert
        Assert.Equal(2, forms.Count);
        Assert.False(await forms[0].Accept(null));
        Assert.Empty(peer.Responses);
        Assert.True(await forms[1].Decline().WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        var response = Assert.Single(peer.Responses);
        Assert.Equal(JsonRpcErrorCode.Cancelled, response[0].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal("decline", response[1].GetProperty("result").GetProperty("action").GetString());
        peer.Deliver(Cancel("5"));
        Assert.Single(peer.Responses);
    }

    [Fact]
    public async Task Batch_CallerCancellationAndBatchedTerminalResponse_SettlesWithoutError()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        using var cancelled = new CancellationTokenSource();
        var first = peer.Client.CreateSessionAsync(new SessionNewParams("/workspace", []), cancelled.Token);
        var second = peer.Client.CreateSessionAsync(new SessionNewParams("/workspace", []), TestToken);
        var requests = peer.SessionRequests.ToArray();

        // Act
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        peer.Deliver("[{\"jsonrpc\":\"2.0\",\"id\":" + requests[0].Id
            + ",\"error\":{\"code\":-32800,\"message\":\"Cancelled\"}}," + SessionResponse(requests[1], "kept") + "]");

        // Assert
        Assert.Single(peer.Notifications, item => item.GetProperty("method").GetString() == CancelRequestParams.Method);
        Assert.Equal("kept", (await second.WaitAsync(TimeSpan.FromSeconds(5), TestToken)).SessionId);
        Assert.Empty(peer.Errors);
    }

    [Fact]
    public async Task Batch_DisconnectDuringWaiting_AbandonsBatchAndIgnoresStaleCallbacks()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var forms = new List<ElicitationRequestEventArgs>();
        peer.Client.ElicitationRequestReceived += (_, form) => forms.Add(form);
        var oldDelivery = peer.CaptureDelivery();
        peer.Deliver("[" + Form("1") + "," + Form("2") + "]");
        var oldAnswer = forms[0].Accept(null);

        // Act
        Assert.True(await peer.Client.DisconnectAsync());
        Assert.False(await oldAnswer.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        await peer.InitializeAsync();
        oldDelivery("[" + Form("9") + "]");
        peer.Deliver("[" + Form("1") + "]");

        // Assert
        Assert.Equal(3, forms.Count);
        Assert.False(await forms[1].Cancel());
        Assert.False(await forms[0].Accept(null));
        Assert.True(await forms[2].Decline().WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.Single(peer.Responses);
        Assert.Empty(peer.Errors);
    }

    [Fact]
    public async Task Batch_FailedSend_RetryKeepsAllAnswersAndCommitsAllSiblings()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var forms = new List<ElicitationRequestEventArgs>();
        peer.Client.ElicitationRequestReceived += (_, form) => forms.Add(form);
        peer.Deliver("[" + Form("1") + "," + Form("2") + "]");
        peer.FailNextResponse = true;

        // Act
        var first = forms[0].Decline();
        var second = forms[1].Cancel();
        Assert.False(await first.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.False(await second.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.Empty(peer.Responses);

        // Assert
        Assert.True(await forms[0].Decline().WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        var response = Assert.Single(peer.Responses);
        Assert.Equal(2, response.GetArrayLength());
        Assert.False(await forms[1].Accept(null));
        Assert.Empty(peer.Errors);
    }

    [Fact]
    public async Task Batch_V1OnlyMethods_AreRejectedInsideDraftBatch()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var delivered = false;
        peer.Client.FileSystemRequestReceived += (_, _) => delivered = true;
        peer.Client.TerminalRequestReceived += (_, _) => delivered = true;

        // Act
        peer.Deliver("""
            [{"jsonrpc":"2.0","id":1,"method":"fs/read_text_file","params":{"sessionId":"session","path":"/file"}},
             {"jsonrpc":"2.0","id":2,"method":"terminal/create","params":{"sessionId":"session","command":"echo"}}]
            """);

        // Assert
        Assert.False(delivered);
        var response = Assert.Single(peer.Responses);
        Assert.Equal(2, response.GetArrayLength());
        Assert.All(response.EnumerateArray(), item =>
            Assert.Equal(JsonRpcErrorCode.MethodNotFound, item.GetProperty("error").GetProperty("code").GetInt32()));
    }

    [Fact]
    public async Task Batch_SessionCancel_SubmitsAllSiblingsBeforeAwaiting()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var forms = new List<ElicitationRequestEventArgs>();
        peer.Client.ElicitationRequestReceived += (_, form) => forms.Add(form);
        peer.Deliver("[" + Form("1") + "," + Form("2") + "]");

        // Act
        await peer.Client.CancelSessionAsync(new SessionCancelParams("session"), TestToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestToken);

        // Assert
        var response = Assert.Single(peer.Responses);
        Assert.Equal(2, response.GetArrayLength());
        Assert.All(response.EnumerateArray(), item =>
            Assert.Equal("cancel", item.GetProperty("result").GetProperty("action").GetString()));
        Assert.False(await forms[0].Accept(null));
        Assert.False(await forms[1].Decline());
    }

    [Fact]
    public async Task Batch_SessionCancelWithPreparedAnswerAndOtherSession_DoesNotWaitForUnrelatedInput()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var forms = new List<ElicitationRequestEventArgs>();
        peer.Client.ElicitationRequestReceived += (_, form) => forms.Add(form);
        peer.Deliver("[" + Form("1") + "," + Form("2").Replace("\"session\"", "\"other\"", StringComparison.Ordinal) + "]");
        var prepared = forms[0].Accept(null);

        // Act
        await peer.Client.CancelSessionAsync(new SessionCancelParams("session"), TestToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestToken);

        // Assert
        Assert.Empty(peer.Responses);
        Assert.False(prepared.IsCompleted);
        Assert.True(await forms[1].Decline().WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.True(await prepared.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        var response = Assert.Single(peer.Responses);
        Assert.Equal("cancel", response[0].GetProperty("result").GetProperty("action").GetString());
        Assert.Equal("decline", response[1].GetProperty("result").GetProperty("action").GetString());
    }

    [Fact]
    public async Task Batch_DuplicatePendingId_DoesNotReplaceOriginalResponder()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var forms = new List<ElicitationRequestEventArgs>();
        peer.Client.ElicitationRequestReceived += (_, form) => forms.Add(form);

        // Act
        peer.Deliver("[" + Form("1") + "," + Form("1") + "]");

        // Assert
        var form = Assert.Single(forms);
        Assert.True(await form.Cancel().WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        var response = Assert.Single(peer.Responses);
        Assert.Equal(2, response.GetArrayLength());
        Assert.Equal("cancel", response[0].GetProperty("result").GetProperty("action").GetString());
        Assert.Equal(JsonRpcErrorCode.InvalidRequest, response[1].GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Batch_NotificationSubscriberThrows_KeepsLaterCalls()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        ElicitationRequestEventArgs? url = null;
        peer.Client.ElicitationRequestReceived += (_, value) => url = value;
        peer.Deliver("""
            {"jsonrpc":"2.0","id":"url","method":"elicitation/create","params":{
            "sessionId":"session","mode":"url","elicitationId":"connect","url":"https://agent.example/connect","message":"Connect"}}
            """);
        Assert.NotNull(url);
        Assert.True(await url.Accept(null));
        peer.Responses.Clear();
        peer.Client.ElicitationCompleted += (_, _) => throw new InvalidOperationException("Host UI failed.");

        // Act
        peer.Deliver("""
            [{"jsonrpc":"2.0","method":"elicitation/complete","params":{"elicitationId":"connect"}},
             {"jsonrpc":"2.0","id":"kept","method":"_missing"}]
            """);

        // Assert
        var response = Assert.Single(peer.Responses);
        Assert.Equal(1, response.GetArrayLength());
        Assert.Equal("kept", response[0].GetProperty("id").GetString());
        Assert.Equal(JsonRpcErrorCode.MethodNotFound, response[0].GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task Batch_HandlerAnswersThenThrows_KeepsPreparedAnswerAndSiblings()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        Task<bool>? prepared = null;
        peer.Client.ElicitationRequestReceived += (_, value) =>
        {
            prepared = value.Decline();
            throw new InvalidOperationException("Host failed after answering.");
        };

        // Act
        peer.Deliver("[" + Form("1") + ","
            + "{\"jsonrpc\":\"2.0\",\"id\":\"kept\",\"method\":\"_missing\"}]");

        // Assert
        Assert.NotNull(prepared);
        Assert.True(await prepared.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        var response = Assert.Single(peer.Responses);
        Assert.Equal("decline", response[0].GetProperty("result").GetProperty("action").GetString());
        Assert.Equal("kept", response[1].GetProperty("id").GetString());
        Assert.Single(peer.Errors);
    }

    [Fact]
    public async Task Batch_SendAndErrorSubscriberThrow_ReleasesEveryResponderForRetry()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var forms = new List<ElicitationRequestEventArgs>();
        peer.Client.ElicitationRequestReceived += (_, form) => forms.Add(form);
        peer.Client.ErrorOccurred += (_, _) => throw new InvalidOperationException("Host failed to display the send error.");
        peer.Deliver("[" + Form("1") + "," + Form("2") + "]");
        peer.ThrowNextResponse = true;

        // Act
        var first = forms[0].Decline();
        var second = forms[1].Cancel();

        // Assert
        Assert.False(await first.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.False(await second.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.Empty(peer.Responses);
        Assert.True(await forms[0].Decline().WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.Equal(2, Assert.Single(peer.Responses).GetArrayLength());
        Assert.False(await forms[1].Accept(null));
        Assert.Single(peer.Errors);
    }

    [Fact]
    public async Task Batch_PeerCancelsPreparedAnswer_ReplacesItBeforeTheSharedWrite()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var forms = new List<ElicitationRequestEventArgs>();
        peer.Client.ElicitationRequestReceived += (_, form) => forms.Add(form);
        peer.Deliver("[" + Form("1") + "," + Form("2") + "]");
        var prepared = forms[0].Accept(null);

        // Act
        peer.Deliver(Cancel("1"));

        // Assert
        Assert.False(await forms[0].Decline());
        Assert.Empty(peer.Responses);
        Assert.True(await forms[1].Cancel().WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.True(await prepared.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        var response = Assert.Single(peer.Responses);
        Assert.Equal(JsonRpcErrorCode.Cancelled, response[0].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal("cancel", response[1].GetProperty("result").GetProperty("action").GetString());
    }

    [Fact]
    public async Task Batch_SessionCancelWithExtensionAndForm_SettlesBothWithoutDeadlock()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var questionCount = 0;
        peer.Client.AskUserRequestReceived += (_, _) => questionCount++;
        peer.Client.ElicitationRequestReceived += (_, _) => { };
        var question = """
            {"jsonrpc":"2.0","id":7,"method":"_interaction.ask_user","params":{"sessionId":"session",
            "questions":[{"header":"Mode","question":"Choose","options":[{"label":"A","description":"First"},
            {"label":"B","description":"Second"}]}]}}
            """;
        peer.Deliver("[" + question + "," + Form("2") + "]");
        Assert.Equal(1, questionCount);

        // Act
        await peer.Client.CancelSessionAsync(new SessionCancelParams("session"), TestToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestToken);

        // Assert
        var response = Assert.Single(peer.Responses);
        Assert.Equal(JsonRpcErrorCode.MethodNotAllowed, response[0].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal("cancel", response[1].GetProperty("result").GetProperty("action").GetString());
    }

    [Fact]
    public async Task Batch_LoggerThrowsOnSendFailure_ReleasesEveryPublicCallbackForRetry()
    {
        // Arrange
        using var peer = new BatchPeer(AcpProtocolVersion.V2, new ThrowingLogger());
        await peer.InitializeAsync();
        var forms = new List<ElicitationRequestEventArgs>();
        peer.Client.ElicitationRequestReceived += (_, form) => forms.Add(form);
        peer.Deliver("[" + Form("1") + "," + Form("2") + "]");
        peer.ThrowNextResponse = true;

        // Act
        var first = forms[0].Decline();
        var second = forms[1].Cancel();

        // Assert
        Assert.False(await first.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.False(await second.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.Empty(peer.Responses);
        Assert.True(await forms[0].Decline().WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.Equal(2, Assert.Single(peer.Responses).GetArrayLength());
        Assert.False(await forms[1].Accept(null));
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1)]
    [InlineData(AcpProtocolVersion.V2)]
    public async Task AskUser_InvalidAnswer_CanCorrectTheSameRequest(int version)
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync(version);
        AskUserRequestEventArgs? question = null;
        peer.Client.AskUserRequestReceived += (_, value) => question = value;
        peer.Deliver(WrapForVersion(Question("7"), version));
        Assert.NotNull(question);

        // Act
        await Assert.ThrowsAsync<InvalidOperationException>(() => question.Respond(new Dictionary<string, string> { ["Choose"] = "C" }));

        // Assert
        Assert.Empty(peer.Responses);
        Assert.True(await question.Respond(Answer()).WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        var frame = Assert.Single(peer.Responses);
        var response = version == AcpProtocolVersion.V2 ? frame[0] : frame;
        Assert.Equal("A", response.GetProperty("result").GetProperty("answers").GetProperty("Choose").GetString());
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1)]
    [InlineData(AcpProtocolVersion.V2)]
    public async Task AskUser_OldCallbackAfterReconnect_CannotAnswerReusedId(int version)
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync(version);
        var questions = new List<AskUserRequestEventArgs>();
        peer.Client.AskUserRequestReceived += (_, value) => questions.Add(value);
        peer.Deliver(WrapForVersion(Question("7"), version));
        Assert.True(await peer.Client.DisconnectAsync());
        await peer.InitializeAsync();
        peer.Deliver(WrapForVersion(Question("7"), version));

        // Act
        Assert.False(await questions[0].Respond(Answer()));

        // Assert
        Assert.Empty(peer.Responses);
        Assert.True(await questions[1].Respond(Answer()).WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.Single(peer.Responses);
    }

    [Fact]
    public async Task Batch_AskUserFailedSend_RetryCommitsAllPreparedQuestions()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var questions = new List<AskUserRequestEventArgs>();
        peer.Client.AskUserRequestReceived += (_, value) => questions.Add(value);
        peer.Deliver("[" + Question("7") + "," + Question("8") + "]");
        peer.FailNextResponse = true;

        // Act
        var first = questions[0].Respond(Answer());
        var second = questions[1].Respond(Answer());

        // Assert
        Assert.False(await first.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.False(await second.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.True(await questions[0].Respond(Answer()).WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.Equal(2, Assert.Single(peer.Responses).GetArrayLength());
        Assert.False(await questions[1].Respond(Answer()));
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1)]
    [InlineData(AcpProtocolVersion.V2)]
    public async Task AskUser_HandlerAnswersThenThrows_DoesNotSendAnotherResponse(int version)
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync(version);
        Task<bool>? answer = null;
        peer.Client.AskUserRequestReceived += (_, question) =>
        {
            answer = question.Respond(Answer());
            throw new JsonException("Host failed after accepting the answer.");
        };

        // Act
        peer.Deliver(WrapForVersion(Question("7"), version));

        // Assert
        Assert.NotNull(answer);
        Assert.True(await answer.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        var frame = Assert.Single(peer.Responses);
        var response = version == AcpProtocolVersion.V2 ? frame[0] : frame;
        Assert.Equal("A", response.GetProperty("result").GetProperty("answers").GetProperty("Choose").GetString());
        Assert.Single(peer.Errors);
    }

    [Fact]
    public async Task Batch_AskUserPreparedAnswer_SessionCancelReplacesBeforeSharedWrite()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        AskUserRequestEventArgs? question = null;
        ElicitationRequestEventArgs? form = null;
        peer.Client.AskUserRequestReceived += (_, value) => question = value;
        peer.Client.ElicitationRequestReceived += (_, value) => form = value;
        peer.Deliver("[" + Question("7") + "," + Form("2").Replace("\"session\"", "\"other\"", StringComparison.Ordinal) + "]");
        Assert.NotNull(question);
        Assert.NotNull(form);
        var answer = question.Respond(Answer());

        // Act
        await peer.Client.CancelSessionAsync(new SessionCancelParams("session"), TestToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestToken);

        // Assert
        Assert.Empty(peer.Responses);
        Assert.True(await form.Cancel().WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.True(await answer.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        var response = Assert.Single(peer.Responses);
        Assert.Equal(JsonRpcErrorCode.MethodNotAllowed, response[0].GetProperty("error").GetProperty("code").GetInt32());
        Assert.False(await question.Respond(Answer()));
    }

    [Theory]
    [InlineData(AcpProtocolVersion.V1)]
    [InlineData(AcpProtocolVersion.V2)]
    public async Task AskUser_HostThrowsBeforeAnswer_UsesInternalError(int version)
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync(version);
        peer.Client.AskUserRequestReceived += (_, _) => throw new JsonException("Host view failed.");

        // Act
        peer.Deliver(WrapForVersion(Question("7"), version));

        // Assert
        var frame = Assert.Single(peer.Responses);
        var response = version == AcpProtocolVersion.V2 ? frame[0] : frame;
        Assert.Equal(JsonRpcErrorCode.InternalError, response.GetProperty("error").GetProperty("code").GetInt32());
        Assert.Single(peer.Errors);
    }

    [Fact]
    public async Task Batch_PermissionsWithTypedIds_ValidateWithoutConsumingTheRequest()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var permissions = new List<PermissionRequestEventArgs>();
        peer.Client.PermissionRequestReceived += (_, request) => permissions.Add(request);
        peer.Deliver("[" + Permission("1") + "," + Permission("\"1\"") + "]");
        Assert.Equal(2, permissions.Count);

        // Act
        await Assert.ThrowsAsync<AcpException>(() => peer.Client.RespondToPermissionRequestAsync(
            permissions[0].MessageId, "selected", "unoffered"));
        var first = peer.Client.RespondToPermissionRequestAsync(permissions[0].MessageId, "selected", "allow");
        Assert.False(first.IsCompleted);
        var second = peer.Client.RespondToPermissionRequestAsync(permissions[1].MessageId, "cancelled");

        // Assert
        Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.True(await second.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        var response = Assert.Single(peer.Responses);
        Assert.Equal(JsonValueKind.Number, response[0].GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.String, response[1].GetProperty("id").ValueKind);
        Assert.Equal("selected", response[0].GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
        Assert.Equal("cancelled", response[1].GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task Batch_PermissionRetry_CommitsEveryPreparedSibling()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var permissions = new List<PermissionRequestEventArgs>();
        peer.Client.PermissionRequestReceived += (_, request) => permissions.Add(request);
        peer.Deliver("[" + Permission("7") + "," + Permission("8") + "]");
        peer.FailNextResponse = true;
        var first = peer.Client.RespondToPermissionRequestAsync(permissions[0].MessageId, "selected", "allow");
        var second = peer.Client.RespondToPermissionRequestAsync(permissions[1].MessageId, "selected", "allow");
        Assert.False(await first.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.False(await second.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.Empty(peer.Responses);

        // Act
        Assert.True(await peer.Client.RespondToPermissionRequestAsync(permissions[0].MessageId, "selected", "allow")
            .WaitAsync(TimeSpan.FromSeconds(5), TestToken));

        // Assert
        Assert.Equal(2, Assert.Single(peer.Responses).GetArrayLength());
        Assert.False(await peer.Client.RespondToPermissionRequestAsync(permissions[1].MessageId, "cancelled"));
        Assert.Single(peer.Responses);
    }

    [Fact]
    public async Task Batch_SessionCancelAfterFailedWrite_CancelsEveryPreparedPermissionBeforeRetry()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var permissions = new List<PermissionRequestEventArgs>();
        peer.Client.PermissionRequestReceived += (_, request) => permissions.Add(request);
        peer.Deliver("[" + Permission("7") + "," + Permission("8") + "]");
        peer.FailNextResponse = true;
        var first = peer.Client.RespondToPermissionRequestAsync(permissions[0].MessageId, "selected", "allow");
        var second = peer.Client.RespondToPermissionRequestAsync(permissions[1].MessageId, "selected", "allow");
        Assert.False(await first.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.False(await second.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.Empty(peer.Responses);

        // Act
        await peer.Client.CancelSessionAsync(new SessionCancelParams("session"), TestToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestToken);

        // Assert
        var response = Assert.Single(peer.Responses);
        Assert.Equal(2, response.GetArrayLength());
        Assert.All(response.EnumerateArray(), item =>
            Assert.Equal("cancelled", item.GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString()));
        Assert.False(await peer.Client.RespondToPermissionRequestAsync(permissions[0].MessageId, "selected", "allow"));
        Assert.False(await peer.Client.RespondToPermissionRequestAsync(permissions[1].MessageId, "selected", "allow"));
    }

    [Fact]
    public async Task Batch_PeerCancelAfterFailedWrite_RetainsResponseUntilRetrySucceeds()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        PermissionRequestEventArgs? permission = null;
        peer.Client.PermissionRequestReceived += (_, request) => permission = request;
        peer.Deliver("[" + Permission("7") + "]");
        Assert.NotNull(permission);
        peer.FailNextResponse = true;
        Assert.False(await peer.Client.RespondToPermissionRequestAsync(permission.MessageId, "selected", "allow")
            .WaitAsync(TimeSpan.FromSeconds(5), TestToken));

        // Act
        peer.FailNextResponse = true;
        peer.Deliver(Cancel("7"));
        Assert.Empty(peer.Responses);
        Assert.False(await peer.Client.RespondToPermissionRequestAsync(permission.MessageId, "selected", "allow"));
        peer.Deliver(Cancel("7"));

        // Assert
        var response = Assert.Single(peer.Responses);
        Assert.Equal(JsonRpcErrorCode.Cancelled, response[0].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(7, response[0].GetProperty("id").GetInt32());
        Assert.False(await peer.Client.RespondToPermissionRequestAsync(permission.MessageId, "selected", "allow"));
        Assert.Single(peer.Responses);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batch_SessionCancelDuringWrite_RetriesAllSiblingsAndRemainsExplicitlyRetryable(bool retryFails)
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var permissions = new List<PermissionRequestEventArgs>();
        peer.Client.PermissionRequestReceived += (_, request) => permissions.Add(request);
        peer.Deliver("[" + Permission("7") + "," + Permission("8") + "]");
        var releaseWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.NextResponseWrite = releaseWrite.Task;
        var first = peer.Client.RespondToPermissionRequestAsync(permissions[0].MessageId, "selected", "allow");
        var second = peer.Client.RespondToPermissionRequestAsync(permissions[1].MessageId, "selected", "allow");
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        // Act
        await peer.Client.CancelSessionAsync(new SessionCancelParams("session"), TestToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestToken);
        peer.FailNextResponse = retryFails;
        releaseWrite.SetResult(false);
        Assert.Equal(!retryFails, await first.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.Equal(!retryFails, await second.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        if (retryFails)
        {
            Assert.Empty(peer.Responses);
            Assert.False(await peer.Client.RespondToPermissionRequestAsync(permissions[0].MessageId, "selected", "allow"));
            Assert.True(await peer.Client.RespondToPermissionRequestAsync(permissions[0].MessageId, "cancelled")
                .WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        }

        // Assert
        var response = Assert.Single(peer.Responses);
        Assert.Equal(2, response.GetArrayLength());
        Assert.All(response.EnumerateArray(), item =>
            Assert.Equal("cancelled", item.GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString()));
    }

    [Fact]
    public async Task Batch_SessionCancelAfterMixedFailedWrite_PreservesOtherSessionAndCancelsAllMatchingInputs()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var permissions = new List<PermissionRequestEventArgs>();
        ElicitationRequestEventArgs? form = null;
        AskUserRequestEventArgs? question = null;
        peer.Client.PermissionRequestReceived += (_, value) => permissions.Add(value);
        peer.Client.ElicitationRequestReceived += (_, value) => form = value;
        peer.Client.AskUserRequestReceived += (_, value) => question = value;
        peer.Deliver("[" + Permission("7") + "," + Form("8") + "," + Question("9") + ","
            + Permission("10").Replace("\"session\"", "\"other\"", StringComparison.Ordinal) + "]");
        Assert.NotNull(form);
        Assert.NotNull(question);
        peer.FailNextResponse = true;
        var answers = new[]
        {
            peer.Client.RespondToPermissionRequestAsync(permissions[0].MessageId, "selected", "allow"),
            form.Accept(null),
            question.Respond(Answer()),
            peer.Client.RespondToPermissionRequestAsync(permissions[1].MessageId, "selected", "allow")
        };
        foreach (var answer in answers) Assert.False(await answer.WaitAsync(TimeSpan.FromSeconds(5), TestToken));

        // Act
        await peer.Client.CancelSessionAsync(new SessionCancelParams("session"), TestToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestToken);

        // Assert
        var response = Assert.Single(peer.Responses);
        Assert.Equal(4, response.GetArrayLength());
        Assert.Equal("cancelled", response[0].GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
        Assert.Equal("cancel", response[1].GetProperty("result").GetProperty("action").GetString());
        Assert.Equal(JsonRpcErrorCode.MethodNotAllowed, response[2].GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal("selected", response[3].GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
        Assert.False(await form.Accept(null));
        Assert.False(await question.Respond(Answer()));
        Assert.False(await peer.Client.RespondToPermissionRequestAsync(permissions[1].MessageId, "cancelled"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batch_PeerCancelDuringWrite_RetainsCancellationAfterFailureAndNeverRepliesTwice(bool sent)
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        PermissionRequestEventArgs? permission = null;
        peer.Client.PermissionRequestReceived += (_, request) => permission = request;
        peer.Deliver("[" + Permission("7") + "]");
        Assert.NotNull(permission);
        var releaseWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.NextResponseWrite = releaseWrite.Task;
        var answer = peer.Client.RespondToPermissionRequestAsync(permission.MessageId, "selected", "allow");

        // Act
        peer.Deliver(Cancel("7"));
        releaseWrite.SetResult(sent);
        Assert.True(await answer.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        peer.Deliver(Cancel("7"));

        // Assert
        var response = Assert.Single(peer.Responses);
        if (sent)
        {
            Assert.Equal("selected", response[0].GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
        }
        else
        {
            Assert.Equal(JsonRpcErrorCode.Cancelled, response[0].GetProperty("error").GetProperty("code").GetInt32());
        }
        Assert.False(await peer.Client.RespondToPermissionRequestAsync(permission.MessageId, "selected", "allow"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Batch_UrlAcceptDuringSessionCancellation_CommitsTheResponseActuallyWritten(bool sent)
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        ElicitationRequestEventArgs? url = null;
        var completions = new List<string>();
        peer.Client.ElicitationRequestReceived += (_, value) => url = value;
        peer.Client.ElicitationCompleted += (_, value) => completions.Add(value.ElicitationId);
        peer.Deliver("""
            [{"jsonrpc":"2.0","id":7,"method":"elicitation/create","params":{
              "sessionId":"session","mode":"url","elicitationId":"connect","url":"https://agent.example/connect","message":"Connect"}}]
            """);
        Assert.NotNull(url);
        var releaseWrite = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        peer.NextResponseWrite = releaseWrite.Task;
        var answer = url.Accept(null);

        // Act
        await peer.Client.CancelSessionAsync(new SessionCancelParams("session"), TestToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestToken);
        releaseWrite.SetResult(sent);
        Assert.True(await answer.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        peer.Deliver("""
            {"jsonrpc":"2.0","method":"elicitation/complete","params":{"elicitationId":"connect"}}
            """);

        // Assert
        Assert.Equal(sent ? "accept" : "cancel", Assert.Single(peer.Responses)[0].GetProperty("result").GetProperty("action").GetString());
        Assert.Equal(sent ? 1 : 0, completions.Count);
        Assert.False(await url.Accept(null));
    }

    [Fact]
    public async Task Batch_PermissionCancellation_ReplacesPreparedAnswerWithoutWaitingForAnotherSession()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        PermissionRequestEventArgs? permission = null;
        ElicitationRequestEventArgs? form = null;
        peer.Client.PermissionRequestReceived += (_, request) => permission = request;
        peer.Client.ElicitationRequestReceived += (_, request) => form = request;
        peer.Deliver("[" + Permission("7") + "," + Form("8").Replace("\"session\"", "\"other\"", StringComparison.Ordinal) + "]");
        Assert.NotNull(permission);
        Assert.NotNull(form);
        var answer = peer.Client.RespondToPermissionRequestAsync(permission.MessageId, "selected", "allow");

        // Act
        await peer.Client.CancelSessionAsync(new SessionCancelParams("session"), TestToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestToken);

        // Assert
        Assert.Empty(peer.Responses);
        Assert.Equal("session/cancel", Assert.Single(peer.Notifications).GetProperty("method").GetString());
        Assert.True(await form.Cancel().WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.True(await answer.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.Equal("cancelled", Assert.Single(peer.Responses)[0].GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
        Assert.False(await peer.Client.RespondToPermissionRequestAsync(permission.MessageId, "selected", "allow"));
    }

    [Fact]
    public async Task Batch_PermissionSubscriberThrowsAfterAnswer_DoesNotReplacePreparedSuccess()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        Task? answer = null;
        ElicitationRequestEventArgs? form = null;
        peer.Client.PermissionRequestReceived += (_, request) =>
        {
            answer = request.Respond("selected", "allow");
            throw new InvalidOperationException("Host failed after answering.");
        };
        peer.Client.ElicitationRequestReceived += (_, request) => form = request;

        // Act
        peer.Deliver("[" + Permission("7") + "," + Form("8") + "]");
        Assert.NotNull(form);
        Assert.NotNull(answer);
        Assert.True(await form.Cancel().WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        await answer.WaitAsync(TimeSpan.FromSeconds(5), TestToken);

        // Assert
        Assert.Equal("selected", Assert.Single(peer.Responses)[0].GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
        Assert.Single(peer.Errors);
    }

    [Fact]
    public async Task Batch_PermissionReplyWaitingForSibling_DisconnectReleasesOriginalCallback()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        PermissionRequestEventArgs? permission = null;
        peer.Client.PermissionRequestReceived += (_, request) => permission = request;
        peer.Client.ElicitationRequestReceived += (_, _) => { };
        peer.Deliver("[" + Permission("7") + "," + Form("8") + "]");
        Assert.NotNull(permission);
        var answer = peer.Client.RespondToPermissionRequestAsync(permission.MessageId, "selected", "allow");

        // Act
        await peer.Client.DisconnectAsync();
        await peer.InitializeAsync();

        // Assert
        Assert.False(await answer.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
        Assert.Empty(peer.Responses);
        Assert.False(await peer.Client.RespondToPermissionRequestAsync(permission.MessageId, "cancelled"));
    }

    [Fact]
    public async Task Batch_FailedSubscriberError_RetryBySiblingCommitsItsOriginalRequest()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        ElicitationRequestEventArgs? form = null;
        PermissionRequestEventArgs? permission = null;
        peer.Client.PermissionRequestReceived += (_, request) => permission = request;
        peer.Client.ElicitationRequestReceived += (_, request) =>
        {
            form = request;
            throw new JsonException("Private host failure.");
        };
        peer.Deliver("[" + Form("1") + "," + Permission("2") + "]");
        Assert.NotNull(form);
        Assert.NotNull(permission);
        peer.FailNextResponse = true;
        Assert.False(await peer.Client.RespondToPermissionRequestAsync(permission.MessageId, "selected", "allow")
            .WaitAsync(TimeSpan.FromSeconds(5), TestToken));

        // Act
        Assert.True(await peer.Client.RespondToPermissionRequestAsync(permission.MessageId, "selected", "allow")
            .WaitAsync(TimeSpan.FromSeconds(5), TestToken));

        // Assert
        var frame = Assert.Single(peer.Responses);
        Assert.Equal(JsonRpcErrorCode.InternalError, frame[0].GetProperty("error").GetProperty("code").GetInt32());
        Assert.False(await form.Cancel());
        Assert.Single(peer.Responses);
    }

    [Fact]
    public async Task Batch_PermissionAvailabilityChanged_PreparationFailureAndRetryFollowSharedWrite()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var permissions = new List<PermissionRequestEventArgs>();
        peer.Client.PermissionRequestReceived += (_, request) => permissions.Add(request);
        peer.Deliver("[" + Permission("7") + "," + Permission("8") + "]");
        var prepared = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryable = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var siblingPreparedOnRetry = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stage = 0;
        void OnChanged(object? sender, EventArgs args)
        {
            var currentStage = Volatile.Read(ref stage);
            if (currentStage == 0 && permissions[0].IsResponsePrepared && !permissions[1].IsResponsePrepared)
                prepared.TrySetResult(true);
            if (currentStage == 1 && permissions.All(request => request.CanRespond && !request.IsResponsePrepared))
                retryable.TrySetResult(true);
            if (currentStage == 2 && permissions.All(request => request.IsResponsePrepared))
                siblingPreparedOnRetry.TrySetResult(true);
            if (permissions.All(request => !request.CanRespond && !request.IsResponsePrepared))
                completed.TrySetResult(true);
        }
        foreach (var permission in permissions) permission.Changed += OnChanged;
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool>? first = null;
        Task<bool>? second = null;
        Task<bool>? retry = null;
        try
        {
            // Act: one prepared answer yields the interaction, but cannot claim delivery yet.
            first = permissions[0].TryRespondAsync("selected", "allow");
            await prepared.Task.WaitAsync(TimeSpan.FromSeconds(5), TestToken);
            Assert.False(first.IsCompleted);
            Assert.Empty(peer.Responses);
            Volatile.Write(ref stage, 1);
            peer.FailNextResponse = true;
            second = permissions[1].TryRespondAsync("selected", "allow");
            Assert.False(await first.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
            Assert.False(await second.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
            await retryable.Task.WaitAsync(TimeSpan.FromSeconds(5), TestToken);

            Volatile.Write(ref stage, 2);
            peer.NextResponseWrite = release.Task;
            retry = permissions[0].TryRespondAsync("selected", "allow");
            await siblingPreparedOnRetry.Task.WaitAsync(TimeSpan.FromSeconds(5), TestToken);
            Assert.False(retry.IsCompleted);
            release.TrySetResult(true);
            Assert.True(await retry.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestToken);

            // Assert: the sibling did not retry, yet shares the same authoritative completion.
            Assert.Equal(2, Assert.Single(peer.Responses).GetArrayLength());
            Assert.All(permissions, request => Assert.False(request.CanRespond));
            Assert.All(permissions, request => Assert.False(request.IsResponsePrepared));
            Assert.Empty(peer.Errors);
        }
        finally
        {
            release.TrySetResult(false);
            await peer.Client.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5), TestToken);
            foreach (var permission in permissions) permission.Changed -= OnChanged;
            if (first is not null) await first.WaitAsync(TimeSpan.FromSeconds(5), TestToken);
            if (second is not null) await second.WaitAsync(TimeSpan.FromSeconds(5), TestToken);
            if (retry is not null) await retry.WaitAsync(TimeSpan.FromSeconds(5), TestToken);
        }
    }

    [Fact]
    public async Task Batch_ExplicitCancellationOfPreparedPermission_ReplacesSlotWithoutWaitingForSiblings()
    {
        // Arrange
        using var peer = await BatchPeer.CreateAsync();
        var permissions = new List<PermissionRequestEventArgs>();
        peer.Client.PermissionRequestReceived += (_, request) => permissions.Add(request);
        peer.Deliver("[" + Permission("7") + "," + Permission("8") + "]");
        var selected = permissions[0].TryRespondAsync("selected", "allow");
        Task<bool>? cancelled = null;
        Task<bool>? sibling = null;
        try
        {
            Assert.True(permissions[0].IsResponsePrepared);
            Assert.False(selected.IsCompleted);

            // Act: cancellation takes its existing slot even though the original await is pending.
            cancelled = permissions[0].TryRespondAsync("cancelled");
            Assert.True(permissions[0].IsCancellationRequested);
            Assert.False(cancelled.IsCompleted);
            Assert.Empty(peer.Responses);
            sibling = permissions[1].TryRespondAsync("selected", "allow");
            Assert.True(await cancelled.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
            Assert.True(await sibling.WaitAsync(TimeSpan.FromSeconds(5), TestToken));
            Assert.True(await selected.WaitAsync(TimeSpan.FromSeconds(5), TestToken));

            // Assert: every callback confirms the one physical array, whose cancelled slot wins.
            var response = Assert.Single(peer.Responses);
            Assert.Equal("cancelled", response[0].GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
            Assert.Equal("selected", response[1].GetProperty("result").GetProperty("outcome").GetProperty("outcome").GetString());
            Assert.All(permissions, request => Assert.False(request.CanRespond));
        }
        finally
        {
            await peer.Client.DisconnectAsync().WaitAsync(TimeSpan.FromSeconds(5), TestToken);
            await selected.WaitAsync(TimeSpan.FromSeconds(5), TestToken);
            if (cancelled is not null) await cancelled.WaitAsync(TimeSpan.FromSeconds(5), TestToken);
            if (sibling is not null) await sibling.WaitAsync(TimeSpan.FromSeconds(5), TestToken);
        }
    }

    private static string Permission(string id) => "{\"jsonrpc\":\"2.0\",\"id\":" + id + """
        ,"method":"session/request_permission","params":{"sessionId":"session","title":"Run tool",
        "options":[{"optionId":"allow","name":"Allow","kind":"allow_once"}]}}
        """;

    private static string WrapForVersion(string json, int version)
        => version == AcpProtocolVersion.V2 ? "[" + json + "]" : json;

    private static Dictionary<string, string> Answer() => new() { ["Choose"] = "A" };

    private static string Question(string id) => "{\"jsonrpc\":\"2.0\",\"id\":" + id + """
        ,"method":"_interaction.ask_user","params":{"sessionId":"session",
        "questions":[{"header":"Mode","question":"Choose","options":[{"label":"A","description":"First"},
        {"label":"B","description":"Second"}]}]}}
        """;

    private static string Form(string id) => "{\"jsonrpc\":\"2.0\",\"id\":" + id + """
        ,"method":"elicitation/create","params":{"sessionId":"session","mode":"form","message":"Choose",
        "requestedSchema":{"type":"object","properties":{}}}}
        """;

    private static string Cancel(string id)
        => "{\"jsonrpc\":\"2.0\",\"method\":\"$/cancel_request\",\"params\":{\"requestId\":" + id + "}}";

    private static string Notification(string method)
        => "{\"jsonrpc\":\"2.0\",\"method\":\"" + method + "\"}";

    private static string SessionResponse(JsonRpcRequest request, string sessionId)
        => "{\"jsonrpc\":\"2.0\",\"id\":" + request.Id + ",\"result\":{\"sessionId\":\"" + sessionId + "\"}}";

    private sealed class BatchPeer : IAcpTransport
    {
        private readonly int _version;
        private readonly MessageParser _parser = new();

        internal BatchPeer(int version, IAcpClientLogger? logger = null)
        {
            _version = version;
            Client = new AcpClient(this, logger);
            Client.ErrorOccurred += (_, error) => Errors.Enqueue(error);
        }

        public bool IsConnected { get; private set; } = true;
        internal AcpClient Client { get; }
        internal bool FailNextResponse { get; set; }
        internal bool ThrowNextResponse { get; set; }
        internal Task<bool>? NextResponseWrite { get; set; }
        internal ConcurrentQueue<JsonElement> Responses { get; } = new();
        internal ConcurrentQueue<JsonElement> Notifications { get; } = new();
        internal ConcurrentQueue<JsonRpcRequest> SessionRequests { get; } = new();
        internal ConcurrentQueue<string> Errors { get; } = new();

        public event EventHandler<AcpTransportMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<AcpTransportErrorEventArgs>? ErrorOccurred { add { } remove { } }

        internal static async Task<BatchPeer> CreateAsync(int version = AcpProtocolVersion.V2)
        {
            var peer = new BatchPeer(version);
            await peer.InitializeAsync();
            return peer;
        }

        internal Task<InitializeResponse> InitializeAsync()
        {
            var parameters = new InitializeParams(new ClientInfo("batch-peer", "1.0"), new ClientCapabilities
            {
                Elicitation = new ElicitationCapabilities
                {
                    Form = new ElicitationFormCapabilities(),
                    Url = new ElicitationUrlCapabilities()
                },
                Meta = ClientCapabilityMetadata.CreateDefault()
            })
            { ProtocolVersion = _version };
            return _version == AcpProtocolVersion.V2
                ? Client.InitializeDraftAsync(parameters, TestToken)
                : Client.InitializeAsync(parameters, TestToken);
        }

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.FromResult(true);
        }

        public Task<bool> DisconnectAsync()
        {
            IsConnected = false;
            return Task.FromResult(true);
        }

        public Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement.Clone();
            if (root.ValueKind == JsonValueKind.Array || root.TryGetProperty("result", out _) || root.TryGetProperty("error", out _))
            {
                if (NextResponseWrite is { } pendingWrite)
                {
                    NextResponseWrite = null;
                    return CompleteResponseWriteAsync(root, pendingWrite, cancellationToken);
                }
                if (ThrowNextResponse)
                {
                    ThrowNextResponse = false;
                    throw new InvalidOperationException("Transport write failed.");
                }
                if (FailNextResponse)
                {
                    FailNextResponse = false;
                    return Task.FromResult(false);
                }
                Responses.Enqueue(root);
                return Task.FromResult(true);
            }

            var parsed = _parser.ParseMessage(message);
            if (parsed is JsonRpcNotification)
            {
                Notifications.Enqueue(root);
            }
            if (parsed is JsonRpcRequest request)
            {
                if (request.Method == "initialize")
                {
                    var response = new InitializeResponse(_version, new AgentInfo("batch-peer", "1.0"), new AgentCapabilities());
                    Deliver("{\"jsonrpc\":\"2.0\",\"id\":" + request.Id + ",\"result\":"
                        + JsonSerializer.Serialize(response, AcpWireFormat.For(_version).TypeInfo<InitializeResponse>()) + "}");
                }
                else if (request.Method == "session/new")
                {
                    SessionRequests.Enqueue(request);
                }
            }
            return Task.FromResult(true);
        }

        private async Task<bool> CompleteResponseWriteAsync(JsonElement response, Task<bool> pendingWrite,
            CancellationToken cancellationToken)
        {
            var sent = await pendingWrite.WaitAsync(cancellationToken);
            if (sent) Responses.Enqueue(response);
            return sent;
        }

        internal void Deliver(string json) => MessageReceived?.Invoke(this, new AcpTransportMessageReceivedEventArgs(json));

        internal Action<string> CaptureDelivery()
        {
            var handler = MessageReceived;
            return json => handler?.Invoke(this, new AcpTransportMessageReceivedEventArgs(json));
        }

        public void Dispose() => Client.Dispose();
    }

    private sealed class ThrowingLogger : IAcpClientLogger
    {
        public void Log(AcpClientLogLevel level, string code, string message, string? source = null, Exception? exception = null)
            => throw new InvalidOperationException("Host logging failed.");
    }
}
