namespace SalmonEgg.Acp.JsonRpc
{
    /// <summary>
    /// JSON-RPC 2.0 message validator implementation.
    /// Validates message shape and the presence of required fields.
    /// </summary>
    internal sealed class MessageValidator
    {
        /// <summary>
        /// Validates the shape and required fields of a request message.
        /// A request message must contain: jsonrpc, method, id
        /// </summary>
        public ValidationResult ValidateRequest(JsonRpcRequest request)
        {
            if (request == null)
            {
                return ValidationResult.Failure(
                    JsonRpcErrorCode.InvalidRequest,
                    "Request message cannot be null");
            }

            var errors = new System.Collections.Generic.List<string>();

            // Validate the jsonrpc field
            if (string.IsNullOrWhiteSpace(request.JsonRpc) || request.JsonRpc != "2.0")
            {
                errors.Add("Invalid or missing 'jsonrpc' field. Must be '2.0'");
            }

            // Validate the method field
            if (string.IsNullOrWhiteSpace(request.Method))
            {
                errors.Add("Missing or empty 'method' field");
            }

            // Validate the id field
            if (request.Id == null || request.Id.Equals(false))
            {
                // id may be null, but it must not be the boolean value false
                if (request.Id is bool)
                {
                    errors.Add("Invalid 'id' field. Cannot be a boolean value");
                }
                else if (request.Id == null)
                {
                    errors.Add("Missing 'id' field. Request messages must have an 'id'");
                }
            }

            if (errors.Count > 0)
            {
                return ValidationResult.Failure(
                    JsonRpcErrorCode.InvalidRequest,
                    errors);
            }

            return ValidationResult.Success();
        }

        /// <summary>
        /// Validates the shape and required fields of a notification message.
        /// A notification message must contain: jsonrpc, method
        /// A notification message must not contain: id
        /// </summary>
        public ValidationResult ValidateNotification(JsonRpcNotification notification)
        {
            if (notification == null)
            {
                return ValidationResult.Failure(
                    JsonRpcErrorCode.InvalidRequest,
                    "Notification message cannot be null");
            }

            var errors = new System.Collections.Generic.List<string>();

            // Validate the jsonrpc field
            if (string.IsNullOrWhiteSpace(notification.JsonRpc) || notification.JsonRpc != "2.0")
            {
                errors.Add("Invalid or missing 'jsonrpc' field. Must be '2.0'");
            }

            // Validate the method field
            if (string.IsNullOrWhiteSpace(notification.Method))
            {
                errors.Add("Missing or empty 'method' field");
            }

            // Validate the absence of an id field (a notification should not carry an id)
            // Note: in a C# object we cannot directly check whether a given field is present in the JSON,
            // so here we assume that an Id left at its default value means no id was set.
            // Real validation has to happen at the JSON level.

            if (errors.Count > 0)
            {
                return ValidationResult.Failure(
                    JsonRpcErrorCode.InvalidRequest,
                    errors);
            }

            return ValidationResult.Success();
        }

        /// <summary>
        /// Validates the shape and required fields of a response message.
        /// A response message must contain: jsonrpc, id
        /// A response message must contain exactly one of result or error
        /// </summary>
        public ValidationResult ValidateResponse(JsonRpcResponse response)
        {
            if (response == null)
            {
                return ValidationResult.Failure(
                    JsonRpcErrorCode.InvalidRequest,
                    "Response message cannot be null");
            }

            var errors = new System.Collections.Generic.List<string>();

            // Validate the jsonrpc field
            if (string.IsNullOrWhiteSpace(response.JsonRpc) || response.JsonRpc != "2.0")
            {
                errors.Add("Invalid or missing 'jsonrpc' field. Must be '2.0'");
            }

            // Validate the id field
            if (response.Id == null || response.Id.Equals(false))
            {
                if (response.Id is bool)
                {
                    errors.Add("Invalid 'id' field. Cannot be a boolean value");
                }
                else if (response.Id == null)
                {
                    errors.Add("Missing 'id' field. Response messages must have an 'id'");
                }
            }

            // Validate that exactly one of result or error is present
            var hasResult = response.Result.HasValue;
            var hasError = response.Error != null;

            if (hasResult && hasError)
            {
                errors.Add("Response must have either 'result' or 'error', not both");
            }
            else if (!hasResult && !hasError)
            {
                errors.Add("Response must have either 'result' or 'error'");
            }

            // When an error is present, validate the shape of the error object
            if (hasError && response.Error != null)
            {
                var error = response.Error;

                // Validate that error.code is a number
                // In C#, Code is an int property, so it is always a number

                // Validate that error.message is a non-empty string
                if (string.IsNullOrWhiteSpace(error.Message))
                {
                    errors.Add("Error 'message' field cannot be empty");
                }

                // Error code values are not validated: JSON-RPC 2.0 merely reserves -32768..-32000 for
                // predefined semantics, and applications may use any integer code outside that range; unknown
                // codes inside the reserved range belong to future spec revisions and are the Agent's call, so
                // having the client reject a response based on its code would tighten the protocol from outside.
            }

            if (errors.Count > 0)
            {
                return ValidationResult.Failure(
                    JsonRpcErrorCode.InvalidRequest,
                    errors);
            }

            return ValidationResult.Success();
        }
    }

    /// <summary>
    /// JSON-RPC message validation result. For SDK-internal message validation only.
    /// </summary>
    internal sealed class ValidationResult
    {
        /// <summary>
        /// Whether validation succeeded.
        /// </summary>
        public bool IsValid { get; init; }

        /// <summary>
        /// The list of error messages (populated when <see cref="IsValid"/> is false).
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<string> Errors { get; init; }
            = System.Array.Empty<string>();

        /// <summary>
        /// The error code (when validation failed).
        /// </summary>
        public int? ErrorCode { get; init; }

        /// <summary>
        /// Creates a successful validation result.
        /// </summary>
        public static ValidationResult Success() => new() { IsValid = true };

        /// <summary>
        /// Creates a failed validation result.
        /// </summary>
        public static ValidationResult Failure(int errorCode, System.Collections.Generic.IReadOnlyList<string> errors)
            => new() { IsValid = false, ErrorCode = errorCode, Errors = errors };

        /// <summary>
        /// Creates a failed validation result (single error).
        /// </summary>
        public static ValidationResult Failure(int errorCode, string error)
            => new() { IsValid = false, ErrorCode = errorCode, Errors = new[] { error } };
    }
}
