using System;
using System.Diagnostics;

namespace SalmonEgg.Acp.Observability
{
    /// <summary>
    /// ACP SDK-owned tracing source and its low-cardinality protocol attributes.
    /// </summary>
    public static class AcpActivitySources
    {
        public const string ClientName = "SalmonEgg.Acp.Client";

        private const string CancelledAttribute = "acp.request.cancelled";
        private const string ErrorTypeAttribute = "error.type";
        private const string ExceptionEventName = "exception";
        private const string ExceptionMessageAttribute = "exception.message";
        private const string ExceptionStacktraceAttribute = "exception.stacktrace";
        private const string ExceptionTypeAttribute = "exception.type";
        private const string JsonRpcErrorCodeAttribute = "rpc.jsonrpc.error_code";
        private const string RpcMethodAttribute = "rpc.method";
        private const string RpcSystemAttribute = "rpc.system";

        internal static readonly ActivitySource Client = new(ClientName, "1.0.0");

        internal static Activity? StartClientRequest(string method)
        {
            var activity = Client.StartActivity($"acp.request {method}", ActivityKind.Client);
            activity?.SetTag(RpcSystemAttribute, "jsonrpc");
            activity?.SetTag(RpcMethodAttribute, method);
            return activity;
        }

        internal static void MarkSuccess(Activity? activity)
            => activity?.SetStatus(ActivityStatusCode.Ok);

        internal static void MarkProtocolError(Activity? activity, int errorCode)
        {
            activity?.SetTag(ErrorTypeAttribute, "jsonrpc.error");
            activity?.SetTag(JsonRpcErrorCodeAttribute, errorCode);
            activity?.SetStatus(ActivityStatusCode.Error, "JSON-RPC error");
        }

        internal static void MarkInvalidResponse(Activity? activity)
        {
            activity?.SetTag(ErrorTypeAttribute, "jsonrpc.invalid_response");
            activity?.SetStatus(ActivityStatusCode.Error, "Invalid JSON-RPC response");
        }

        internal static void MarkCancelled(Activity? activity)
            => activity?.SetTag(CancelledAttribute, true);

        internal static void RecordException(Activity? activity, Exception exception)
        {
            if (activity is null)
            {
                return;
            }

            activity.SetTag(ErrorTypeAttribute, exception.GetType().FullName);
            activity.SetStatus(ActivityStatusCode.Error, "ACP request failed");

            var tags = new ActivityTagsCollection
            {
                { ExceptionTypeAttribute, exception.GetType().FullName },
                { ExceptionMessageAttribute, exception.Message }
            };

            if (!string.IsNullOrWhiteSpace(exception.StackTrace))
            {
                tags.Add(ExceptionStacktraceAttribute, exception.StackTrace);
            }

            activity.AddEvent(new ActivityEvent(ExceptionEventName, tags: tags));
        }
    }
}
