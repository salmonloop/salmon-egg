using System;
using System.Text.Json;
using Xunit;
using SalmonEgg.Acp.JsonRpc;

namespace SalmonEgg.Acp.Tests.JsonRpc
{
    /// <summary>
    /// JSON-RPC 消息属性测试。
    /// 使用 FsCheck 进行基于属性的测试，验证消息的往返一致性和字段约束。
    /// </summary>
    public class JsonRpcMessageProperties
    {
        /// <summary>
        /// 比较两个 ID 是否相等。
        /// 策略：将两者都序列化为 JSON 字符串进行比较。
        /// 这样可以完美处理任何类型（string, int, long, bool, null, char 等）的 ID，
        /// 避免类型转换和空值问题。
        /// </summary>
        private static bool AreIdsEqual(object? expected, object? actual)
        {
            if (ReferenceEquals(expected, actual))
                return true;

            if (expected is null && actual is null)
                return true;

            if (expected is null || actual is null)
                return false;

            try
            {
                // 配置相同的序列化选项，确保结果一致
                var options = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = null,
                    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never,
                    WriteIndented = false
                };

                // 序列化期望值
                var expectedJson = JsonSerializer.Serialize(expected, options);

                // 序列化实际值（如果它是 JsonElement，直接获取原始文本）
                string actualJson;
                if (actual is JsonElement actualElem)
                {
                    actualJson = actualElem.GetRawText();
                }
                else
                {
                    actualJson = JsonSerializer.Serialize(actual, options);
                }

                // 比较 JSON 字符串
                return expectedJson == actualJson;
            }
            catch
            {
                // 如果序列化失败，回退到 ToString 比较
                return expected.ToString() == actual.ToString();
            }
        }

        /// <summary>
        /// 属性 1：JSON-RPC 2.0 请求消息往返一致性
        /// 验证序列化后反序列化产生等效对象，所有必需字段保持不变。
        /// </summary>
        [Fact]
        public void JsonRpcRequest_RoundTrip_PreservesEquivalence()
        {
            FsCheckPropertyRunner.Run(this, nameof(JsonRpcRequest_RoundTrip_PreservesEquivalenceProperty));
        }

        private void JsonRpcRequest_RoundTrip_PreservesEquivalenceProperty(string id, string method, byte[]? paramsData)
        {
            // Arrange
            var paramsElement = paramsData != null
                ? JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(paramsData))
                : (JsonElement?)null;

            var request = new JsonRpcRequest
            {
                Id = id,
                Method = method,
                Params = paramsElement
            };

            // Act
            var json = JsonSerializer.Serialize(request);
            var deserialized = JsonSerializer.Deserialize<JsonRpcRequest>(json);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal("2.0", deserialized!.JsonRpc);
            Assert.True(AreIdsEqual(deserialized.Id, request.Id));
            Assert.Equal(request.Method, deserialized.Method);

            if (request.Params is JsonElement requestParams)
            {
                Assert.True(deserialized!.Params.HasValue);
                var deserializedParams = deserialized!.Params!.Value;
                // 比较 JSON 值的原始文本，而不是创建时的原始文本
                var deserializedParamsJson = deserializedParams.GetRawText();
                var expectedParamsJson = JsonSerializer.Serialize(requestParams, new JsonSerializerOptions { WriteIndented = false });
                Assert.Equal(expectedParamsJson, deserializedParamsJson);
            }
            else
            {
                Assert.False(deserialized!.Params.HasValue);
            }
        }

        /// <summary>
        /// 属性 1：JSON-RPC 2.0 响应消息往返一致性（成功情况）
        /// </summary>
        [Fact]
        public void JsonRpcResponse_Success_RoundTrip_PreservesEquivalence()
        {
            FsCheckPropertyRunner.Run(this, nameof(JsonRpcResponse_Success_RoundTrip_PreservesEquivalenceProperty));
        }

        private void JsonRpcResponse_Success_RoundTrip_PreservesEquivalenceProperty(string id, byte[] resultData)
        {
            // Arrange
            var result = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(resultData));
            var response = new JsonRpcResponse(id, result);

            // Act
            var json = JsonSerializer.Serialize(response);
            var deserialized = JsonSerializer.Deserialize<JsonRpcResponse>(json);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal("2.0", deserialized!.JsonRpc);
            Assert.True(AreIdsEqual(deserialized.Id, response.Id));
            Assert.True(deserialized.Result.HasValue);
            Assert.Null(deserialized.Error);
            // 比较 JSON 值的原始文本
            Assert.True(response.Result.HasValue);
            var deserializedResult = deserialized.Result!.Value;
            var expectedResult = response.Result!.Value;
            var deserializedResultJson = deserializedResult.GetRawText();
            var expectedResultJson = JsonSerializer.Serialize(expectedResult, new JsonSerializerOptions { WriteIndented = false });
            Assert.Equal(expectedResultJson, deserializedResultJson);
        }

        /// <summary>
        /// 属性 1：JSON-RPC 2.0 响应消息往返一致性（错误情况）
        /// </summary>
        [Fact]
        public void JsonRpcResponse_Error_RoundTrip_PreservesEquivalence()
        {
            FsCheckPropertyRunner.Run(this, nameof(JsonRpcResponse_Error_RoundTrip_PreservesEquivalenceProperty));
        }

        private void JsonRpcResponse_Error_RoundTrip_PreservesEquivalenceProperty(string id, int code, string message)
        {
            // Arrange
            var limitedCode = Math.Max(-32768, Math.Min(-32000, code));
            var error = new JsonRpcError(limitedCode, message);
            var response = new JsonRpcResponse(id, error);

            // Act
            var json = JsonSerializer.Serialize(response);
            var deserialized = JsonSerializer.Deserialize<JsonRpcResponse>(json);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal("2.0", deserialized!.JsonRpc);
            Assert.True(AreIdsEqual(deserialized.Id, response.Id));
            Assert.False(deserialized.Result.HasValue);
            Assert.NotNull(deserialized.Error);
            Assert.NotNull(response.Error);
            Assert.Equal(response.Error!.Code, deserialized.Error!.Code);
            Assert.Equal(response.Error.Message, deserialized.Error.Message);
        }

        /// <summary>
        /// 属性 1：JSON-RPC 2.0 通知消息往返一致性
        /// </summary>
        [Fact]
        public void JsonRpcNotification_RoundTrip_PreservesEquivalence()
        {
            FsCheckPropertyRunner.Run(this, nameof(JsonRpcNotification_RoundTrip_PreservesEquivalenceProperty));
        }

        private void JsonRpcNotification_RoundTrip_PreservesEquivalenceProperty(string method, byte[]? paramsData)
        {
            // Arrange
            var paramsElement = paramsData != null
                ? JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(paramsData))
                : (JsonElement?)null;

            var notification = new JsonRpcNotification(method, paramsElement);

            // Act
            var json = JsonSerializer.Serialize(notification);
            var deserialized = JsonSerializer.Deserialize<JsonRpcNotification>(json);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal("2.0", deserialized!.JsonRpc);
            Assert.Equal(notification.Method, deserialized.Method);

            if (notification.Params is JsonElement notificationParams)
            {
                Assert.True(deserialized!.Params.HasValue);
                var deserializedParams = deserialized!.Params!.Value;
                // 比较 JSON 值的原始文本
                var deserializedParamsJson = deserializedParams.GetRawText();
                var expectedParamsJson = JsonSerializer.Serialize(notificationParams, new JsonSerializerOptions { WriteIndented = false });
                Assert.Equal(expectedParamsJson, deserializedParamsJson);
            }
            else
            {
                Assert.False(deserialized!.Params.HasValue);
            }
        }

        /// <summary>
        /// 属性 2：请求消息必需字段完整性
        /// 验证序列化后的 JSON 包含 jsonrpc, method, id 字段。
        /// </summary>
        [Fact]
        public void JsonRpcRequest_RequiredFields_Present()
        {
            FsCheckPropertyRunner.Run(this, nameof(JsonRpcRequest_RequiredFields_PresentProperty));
        }

        private void JsonRpcRequest_RequiredFields_PresentProperty(string id, string method)
        {
            // Arrange
            var request = new JsonRpcRequest(id, method);

            // Act
            var json = JsonSerializer.Serialize(request);
            var doc = JsonDocument.Parse(json);

            // Assert
            Assert.True(doc.RootElement.TryGetProperty("jsonrpc", out _));
            Assert.True(doc.RootElement.TryGetProperty("method", out _));
            Assert.True(doc.RootElement.TryGetProperty("id", out _));

            // 验证 jsonrpc 值
            var jsonRpcValue = doc.RootElement.GetProperty("jsonrpc").GetString();
            Assert.Equal("2.0", jsonRpcValue);
        }

        /// <summary>
        /// 属性 3：通知消息字段约束
        /// 验证通知消息不包含 id 字段。
        /// </summary>
        [Fact]
        public void JsonRpcNotification_NoIdField()
        {
            FsCheckPropertyRunner.Run(this, nameof(JsonRpcNotification_NoIdFieldProperty));
        }

        private void JsonRpcNotification_NoIdFieldProperty(string method)
        {
            // Arrange
            var notification = new JsonRpcNotification(method);

            // Act
            var json = JsonSerializer.Serialize(notification);
            var doc = JsonDocument.Parse(json);

            // Assert
            Assert.True(doc.RootElement.TryGetProperty("jsonrpc", out _));
            Assert.True(doc.RootElement.TryGetProperty("method", out _));
            Assert.False(doc.RootElement.TryGetProperty("id", out _), "Notification should NOT contain 'id' field");
        }

        /// <summary>
        /// 属性 4：响应消息互斥字段验证（成功情况）
        /// </summary>
        [Fact]
        public void JsonRpcResponse_ExactlyOneOfResultOrError_Success()
        {
            // Arrange
            var resultElement = JsonDocument.Parse("{\"value\":123}").RootElement;
            var response = new JsonRpcResponse("test-id", resultElement);

            // Act
            var json = JsonSerializer.Serialize(response);
            var doc = JsonDocument.Parse(json);

            // Assert
            var hasResult = doc.RootElement.TryGetProperty("result", out var resultProp);
            var hasError = doc.RootElement.TryGetProperty("error", out var errorProp);

            // Check if error property exists AND is not null
            var errorIsNull = hasError && errorProp.ValueKind == System.Text.Json.JsonValueKind.Null;

            Assert.True(hasResult, "Response should have 'result' property");
            Assert.True(errorIsNull, "Response should have 'error' property set to null");
        }

        /// <summary>
        /// 属性 4：响应消息互斥字段验证（错误情况）
        /// </summary>
        [Fact]
        public void JsonRpcResponse_ExactlyOneOfResultOrError_Error()
        {
            // Arrange
            var error = new JsonRpcError(-32600, "Invalid request");
            var response = new JsonRpcResponse("test-id", error);

            // Act
            var json = JsonSerializer.Serialize(response);
            var doc = JsonDocument.Parse(json);

            // Assert
            var hasResult = doc.RootElement.TryGetProperty("result", out var resultProp);
            var hasError = doc.RootElement.TryGetProperty("error", out var errorProp);

            // Check if result property exists AND is not null, and error exists AND is not null
            var resultIsNull = hasResult && resultProp.ValueKind == System.Text.Json.JsonValueKind.Null;
            var errorIsNotNull = hasError && errorProp.ValueKind != System.Text.Json.JsonValueKind.Null;

            Assert.True(resultIsNull, "Response should have 'result' property set to null");
            Assert.True(errorIsNotNull, "Response should have a non-null 'error' property");
        }

        /// <summary>
        /// 属性 5：错误码标准化
        /// 验证所有错误响应包含标准错误码在有效范围内。
        /// </summary>
        [Fact]
        public void JsonRpcError_StandardErrorCodeRange()
        {
            FsCheckPropertyRunner.Run(this, nameof(JsonRpcError_StandardErrorCodeRangeProperty));
        }

        private void JsonRpcError_StandardErrorCodeRangeProperty(int code, string message)
        {
            // Arrange
            var limitedCode = Math.Max(-32768, Math.Min(-32000, code));
            var error = new JsonRpcError(limitedCode, message);

            // Act
            var json = JsonSerializer.Serialize(error);
            var deserialized = JsonSerializer.Deserialize<JsonRpcError>(json);

            // Assert
            Assert.NotNull(deserialized);
            Assert.Equal(error.Code, deserialized!.Code);
            Assert.Equal(error.Message, deserialized.Message);

            // 验证错误码在有效范围内
            Assert.True(deserialized.Code >= -32768);
            Assert.True(deserialized.Code <= -32000);
        }
    }
}
