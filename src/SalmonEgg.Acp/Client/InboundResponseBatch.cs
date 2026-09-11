using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SalmonEgg.Acp.JsonRpc;

namespace SalmonEgg.Acp.Client;

/// <summary>
/// Owns one batch response until all calls have answered and the complete array reaches the peer.
/// </summary>
internal sealed class InboundResponseBatch
{
    private readonly object _gate = new();
    private readonly JsonRpcResponse?[] _responses;
    private readonly bool[] _cancelledResponses;
    private readonly Func<IReadOnlyList<JsonRpcResponse>, Task<bool>> _send;
    private readonly Action<IReadOnlyList<JsonRpcResponse>> _onSent;
    private readonly Action _onChanged;
    private readonly Action _onSendFailed;
    private readonly IAcpClientLogger _logger;
    private TaskCompletionSource<bool> _completion = NewCompletion();
    private int _remaining;
    private int _deferredSends;
    private bool _sending;
    private bool _finished;
    private bool _retryable;
    private bool _sendRequested;

    internal InboundResponseBatch(int count, Func<IReadOnlyList<JsonRpcResponse>, Task<bool>> send,
        Action<IReadOnlyList<JsonRpcResponse>> onSent, IAcpClientLogger logger, Action onChanged, Action onSendFailed)
    {
        _responses = new JsonRpcResponse[count];
        _cancelledResponses = new bool[count];
        _remaining = count;
        _send = send;
        _onSent = onSent;
        _logger = logger;
        _onChanged = onChanged;
        _onSendFailed = onSendFailed;
    }

    internal bool IsSending
    {
        get
        {
            lock (_gate)
            {
                return !_finished && _sending;
            }
        }
    }

    internal Task<bool> SubmitAsync(int index, JsonRpcResponse response)
    {
        TaskCompletionSource<bool> completion;
        lock (_gate)
        {
            if (_finished || _sending || (_responses[index] is not null && !_retryable))
            {
                return Task.FromResult(false);
            }

            completion = _completion;
            if (_responses[index] is null)
            {
                _remaining--;
            }
            if (!_cancelledResponses[index])
            {
                _responses[index] = response;
            }
            _sendRequested = true;
        }

        TrySendPreparedResponses();
        _onChanged();
        return completion.Task;
    }

    internal void DeferSending()
    {
        lock (_gate)
        {
            _deferredSends++;
        }
    }

    internal void ResumeSending()
    {
        lock (_gate)
        {
            _deferredSends--;
        }
        TrySendPreparedResponses();
    }

    internal Task<bool> SubmitCancellation(int index, JsonRpcResponse response)
    {
        Task<bool> completion;
        lock (_gate)
        {
            if (_finished)
            {
                return Task.FromResult(false);
            }
            completion = _completion.Task;
            if (_responses[index] is null)
            {
                _remaining--;
            }
            _responses[index] = response;
            _cancelledResponses[index] = true;
            // The in-flight array is immutable. If its write fails, the new cancellation owns one
            // follow-up attempt; a failed cancellation then waits for a new explicit retry.
            _sendRequested = true;
        }
        TrySendPreparedResponses();
        _onChanged();
        return completion;
    }

    internal bool HasPreparedResponse(int index)
    {
        lock (_gate)
        {
            return _responses[index] is not null;
        }
    }

    internal bool IsResponsePrepared(int index)
    {
        lock (_gate)
        {
            return !_finished && !_retryable && _responses[index] is not null;
        }
    }

    internal void Abandon()
    {
        lock (_gate)
        {
            _finished = true;
            Array.Clear(_responses);
            _completion.TrySetResult(false);
        }
    }

    private void TrySendPreparedResponses()
    {
        JsonRpcResponse[] ready;
        TaskCompletionSource<bool> completion;
        lock (_gate)
        {
            if (_finished || _sending || _deferredSends != 0 || _remaining != 0 || !_sendRequested)
            {
                return;
            }
            _sending = true;
            _retryable = false;
            _sendRequested = false;
            completion = _completion;
            ready = new JsonRpcResponse[_responses.Length];
            for (var item = 0; item < ready.Length; item++)
            {
                ready[item] = _responses[item]!;
            }
        }
        _ = SendAsync(ready, completion);
        // ResumeSending and a follow-up cancellation can start a write without SubmitAsync.
        // Publish that transition from the batch owner on every physical send path.
        _onChanged();
    }

    private async Task SendAsync(IReadOnlyList<JsonRpcResponse> responses, TaskCompletionSource<bool> completion)
    {
        var sent = false;
        try
        {
            sent = await _send(responses).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Even a host error subscriber may throw while reporting a failed write. The batch
            // still owns every waiting responder and must release all of them for a later retry.
            try
            {
                _logger.Log(AcpClientLogLevel.Error, "BATCH_RESPONSE_FAILED",
                    "Failed to send the prepared response batch.", exception: exception);
            }
            catch (Exception)
            {
                // Logging is supplied by the host. A second failure has no reliable reporting
                // channel, but it must not prevent the batch from releasing its waiting callers.
            }
        }
        bool retryCancellation;
        lock (_gate)
        {
            _sending = false;
            if (_finished)
            {
                return;
            }
            _finished = sent;
            retryCancellation = !sent && _sendRequested;
            if (!sent && !retryCancellation)
            {
                // A failed write leaves every answer retryable as the same batch. Already answered
                // siblings remain in the array, so retrying one UI action does not strand the rest.
                _completion = NewCompletion();
                _retryable = true;
            }
        }

        if (retryCancellation)
        {
            TrySendPreparedResponses();
            return;
        }

        try
        {
            if (sent)
            {
                _onSent(responses);
            }
            else
            {
                _onSendFailed();
            }
        }
        finally
        {
            completion.TrySetResult(sent);
            _onChanged();
        }
    }

    private static TaskCompletionSource<bool> NewCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
