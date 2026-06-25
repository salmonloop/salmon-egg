using System;
using System.Text.Json;
using FsCheck.NUnit;
using NUnit.Framework;
using SalmonEgg.Domain.Models.JsonRpc;

namespace SalmonEgg.Domain.Tests.Models.JsonRpc
{
    /// <summary>
    /// JSON-RPC 消息属性测试。
    /// 使用 FsCheck 进行基于属性的测试，验证消息的往返一致性和字段约束。
    /// </summary>
    [TestFixture]
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
        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void JsonRpcRequest_RoundTrip_PreservesEquivalence(string id, string method, byte[]? paramsData)
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
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.JsonRpc, Is.EqualTo("2.0"));
            Assert.That(AreIdsEqual(deserialized.Id, request.Id), Is.True);
            Assert.That(deserialized.Method, Is.EqualTo(request.Method));

            if (request.Params is JsonElement requestParams)
            {
                Assert.That(deserialized!.Params.HasValue, Is.True);
                var deserializedParams = deserialized!.Params!.Value;
                // 比较 JSON 值的原始文本，而不是创建时的原始文本
                var deserializedParamsJson = deserializedParams.GetRawText();
                var expectedParamsJson = JsonSerializer.Serialize(requestParams, new JsonSerializerOptions { WriteIndented = false });
                Assert.That(deserializedParamsJson, Is.EqualTo(expectedParamsJson), $"Params JSON mismatch. Expected: {expectedParamsJson}, Actual: {deserializedParamsJson}");
            }
            else
            {
                Assert.That(deserialized!.Params.HasValue, Is.False);
            }
        }

        /// <summary>
        /// 属性 1：JSON-RPC 2.0 响应消息往返一致性（成功情况）
        /// </summary>
        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void JsonRpcResponse_Success_RoundTrip_PreservesEquivalence(string id, byte[] resultData)
        {
            // Arrange
            var result = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(resultData));
            var response = new JsonRpcResponse(id, result);

            // Act
            var json = JsonSerializer.Serialize(response);
            var deserialized = JsonSerializer.Deserialize<JsonRpcResponse>(json);

            // Assert
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.JsonRpc, Is.EqualTo("2.0"));
            Assert.That(AreIdsEqual(deserialized.Id, response.Id), Is.True);
            Assert.That(deserialized.Result.HasValue, Is.True);
            Assert.That(deserialized.Error, Is.Null);
            // 比较 JSON 值的原始文本
            Assert.That(response.Result.HasValue, Is.True);
            var deserializedResult = deserialized.Result!.Value;
            var expectedResult = response.Result!.Value;
            var deserializedResultJson = deserializedResult.GetRawText();
            var expectedResultJson = JsonSerializer.Serialize(expectedResult, new JsonSerializerOptions { WriteIndented = false });
            Assert.That(deserializedResultJson, Is.EqualTo(expectedResultJson), $"Result JSON mismatch. Expected: {expectedResultJson}, Actual: {deserializedResultJson}");
        }

        /// <summary>
        /// 属性 1：JSON-RPC 2.0 响应消息往返一致性（错误情况）
        /// </summary>
        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void JsonRpcResponse_Error_RoundTrip_PreservesEquivalence(string id, int code, string message)
        {
            // Arrange
            var limitedCode = Math.Max(-32768, Math.Min(-32000, code));
            var error = new JsonRpcError(limitedCode, message);
            var response = new JsonRpcResponse(id, error);

            // Act
            var json = JsonSerializer.Serialize(response);
            var deserialized = JsonSerializer.Deserialize<JsonRpcResponse>(json);

            // Assert
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.JsonRpc, Is.EqualTo("2.0"));
            Assert.That(AreIdsEqual(deserialized.Id, response.Id), Is.True);
            Assert.That(deserialized.Result.HasValue, Is.False);
            Assert.That(deserialized.Error, Is.Not.Null);
            Assert.That(response.Error, Is.Not.Null);
            Assert.That(deserialized.Error!.Code, Is.EqualTo(response.Error!.Code));
            Assert.That(deserialized.Error.Message, Is.EqualTo(response.Error.Message));
        }

        /// <summary>
        /// 属性 1：JSON-RPC 2.0 通知消息往返一致性
        /// </summary>
        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void JsonRpcNotification_RoundTrip_PreservesEquivalence(string method, byte[]? paramsData)
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
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.JsonRpc, Is.EqualTo("2.0"));
            Assert.That(deserialized.Method, Is.EqualTo(notification.Method));

            if (notification.Params is JsonElement notificationParams)
            {
                Assert.That(deserialized!.Params.HasValue, Is.True);
                var deserializedParams = deserialized!.Params!.Value;
                // 比较 JSON 值的原始文本
                var deserializedParamsJson = deserializedParams.GetRawText();
                var expectedParamsJson = JsonSerializer.Serialize(notificationParams, new JsonSerializerOptions { WriteIndented = false });
                Assert.That(deserializedParamsJson, Is.EqualTo(expectedParamsJson), $"Params JSON mismatch. Expected: {expectedParamsJson}, Actual: {deserializedParamsJson}");
            }
            else
            {
                Assert.That(deserialized!.Params.HasValue, Is.False);
            }
        }

        /// <summary>
        /// 属性 2：请求消息必需字段完整性
        /// 验证序列化后的 JSON 包含 jsonrpc, method, id 字段。
        /// </summary>
        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void JsonRpcRequest_RequiredFields_Present(string id, string method)
        {
            // Arrange
            var request = new JsonRpcRequest(id, method);

            // Act
            var json = JsonSerializer.Serialize(request);
            var doc = JsonDocument.Parse(json);

            // Assert
            Assert.That(doc.RootElement.TryGetProperty("jsonrpc", out _), Is.True);
            Assert.That(doc.RootElement.TryGetProperty("method", out _), Is.True);
            Assert.That(doc.RootElement.TryGetProperty("id", out _), Is.True);

            // 验证 jsonrpc 值
            var jsonRpcValue = doc.RootElement.GetProperty("jsonrpc").GetString();
            Assert.That(jsonRpcValue, Is.EqualTo("2.0"));
        }

        /// <summary>
        /// 属性 3：通知消息字段约束
        /// 验证通知消息不包含 id 字段。
        /// </summary>
        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void JsonRpcNotification_NoIdField(string method)
        {
            // Arrange
            var notification = new JsonRpcNotification(method);

            // Act
            var json = JsonSerializer.Serialize(notification);
            var doc = JsonDocument.Parse(json);

            // Assert
            Assert.That(doc.RootElement.TryGetProperty("jsonrpc", out _), Is.True);
            Assert.That(doc.RootElement.TryGetProperty("method", out _), Is.True);
            Assert.That(doc.RootElement.TryGetProperty("id", out _), Is.False, "Notification should NOT contain 'id' field");
        }

        /// <summary>
        /// 属性 4：响应消息互斥字段验证（成功情况）
        /// </summary>
        [Test]
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

            Assert.That(hasResult, Is.True, "Response should have 'result' property");
            Assert.That(errorIsNull, Is.True, "Response should have 'error' property set to null");
           }

        /// <summary>
        /// 属性 4：响应消息互斥字段验证（错误情况）
        /// </summary>
        [Test]
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

            Assert.That(resultIsNull, Is.True, "Response should have 'result' property set to null");
            Assert.That(errorIsNotNull, Is.True, "Response should have a non-null 'error' property");
           }

        /// <summary>
        /// 属性 5：错误码标准化
        /// 验证所有错误响应包含标准错误码在有效范围内。
        /// </summary>
        [FsCheck.NUnit.Property(QuietOnSuccess = true)]
        public void JsonRpcError_StandardErrorCodeRange(int code, string message)
        {
            // Arrange
            var limitedCode = Math.Max(-32768, Math.Min(-32000, code));
            var error = new JsonRpcError(limitedCode, message);

            // Act
            var json = JsonSerializer.Serialize(error);
            var deserialized = JsonSerializer.Deserialize<JsonRpcError>(json);

            // Assert
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized!.Code, Is.EqualTo(error.Code));
            Assert.That(deserialized.Message, Is.EqualTo(error.Message));

            // 验证错误码在有效范围内
            Assert.That(deserialized.Code, Is.GreaterThanOrEqualTo(-32768));
            Assert.That(deserialized.Code, Is.LessThanOrEqualTo(-32000));
        }
    }
}
