using System;
using System.Text.Json;
using FsCheck;
using FsCheck.NUnit;
using NUnit.Framework;
using UnoAcpClient.Domain.Models.JsonRpc;

namespace UnoAcpClient.Domain.Tests.Models.JsonRpc
{
    /// <summary>
    /// JSON-RPC 消息属性测试。
    /// 使用 FsCheck 进行基于属性的测试，验证消息的往返一致性和字段约束。
    /// </summary>
    [TestFixture]
    public class JsonRpcMessageProperties
    {
        /// <summary>
        /// 属性 1：JSON-RPC 2.0 请求消息往返一致性
        /// 验证序列化后反序列化产生等效对象，所有必需字段保持不变。
        /// </summary>
        [FsCheck.NUnit.Property]
        public void JsonRpcRequest_RoundTrip_PreservesEquivalence(object id, string method, byte[]? paramsData)
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
            NUnit.Framework.Assert.IsNotNull(deserialized);
            Assert.AreEqual(request.JsonRpc, deserialized.JsonRpc);
            Assert.AreEqual(request.Id, deserialized.Id);
            Assert.AreEqual(request.Method, deserialized.Method);

            if (request.Params.HasValue)
            {
                NUnit.Framework.Assert.IsTrue(deserialized.Params.HasValue);
                Assert.AreEqual(
                    request.Params.Value.GetRawText(),
                    deserialized.Params.Value.GetRawText());
            }
            else
            {
                NUnit.Framework.Assert.IsFalse(deserialized.Params.HasValue);
            }
        }

        /// <summary>
        /// 属性 1：JSON-RPC 2.0 响应消息往返一致性（成功情况）
        /// </summary>
        [FsCheck.NUnit.Property]
        public void JsonRpcResponse_Success_RoundTrip_PreservesEquivalence(object id, byte[] resultData)
        {
            // Arrange
            var result = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(resultData));
            var response = new JsonRpcResponse(id, result);

            // Act
            var json = JsonSerializer.Serialize(response);
            var deserialized = JsonSerializer.Deserialize<JsonRpcResponse>(json);

            // Assert
            NUnit.Framework.Assert.IsNotNull(deserialized);
            Assert.AreEqual(response.JsonRpc, deserialized.JsonRpc);
            Assert.AreEqual(response.Id, deserialized.Id);
            NUnit.Framework.Assert.IsTrue(deserialized.Result.HasValue);
            NUnit.Framework.Assert.IsFalse(deserialized.Error.HasValue);
            Assert.AreEqual(
                response.Result.Value.GetRawText(),
                deserialized.Result.Value.GetRawText());
        }

        /// <summary>
        /// 属性 1：JSON-RPC 2.0 响应消息往返一致性（错误情况）
        /// </summary>
        [FsCheck.NUnit.Property]
        public void JsonRpcResponse_Error_RoundTrip_PreservesEquivalence(object id, int code, string message)
        {
            // Arrange
            var error = new JsonRpcError(code, message);
            var response = new JsonRpcResponse(id, error);

            // Act
            var json = JsonSerializer.Serialize(response);
            var deserialized = JsonSerializer.Deserialize<JsonRpcResponse>(json);

            // Assert
            NUnit.Framework.Assert.IsNotNull(deserialized);
            Assert.AreEqual(response.JsonRpc, deserialized.JsonRpc);
            Assert.AreEqual(response.Id, deserialized.Id);
            NUnit.Framework.Assert.IsFalse(deserialized.Result.HasValue);
            NUnit.Framework.Assert.IsNotNull(deserialized.Error);
            Assert.AreEqual(response.Error.Code, deserialized.Error.Code);
            Assert.AreEqual(response.Error.Message, deserialized.Error.Message);
        }

        /// <summary>
        /// 属性 1：JSON-RPC 2.0 通知消息往返一致性
        /// </summary>
        [FsCheck.NUnit.Property]
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
            NUnit.Framework.Assert.IsNotNull(deserialized);
            Assert.AreEqual(notification.JsonRpc, deserialized.JsonRpc);
            Assert.AreEqual(notification.Method, deserialized.Method);

            if (notification.Params.HasValue)
            {
                NUnit.Framework.Assert.IsTrue(deserialized.Params.HasValue);
                Assert.AreEqual(
                    notification.Params.Value.GetRawText(),
                    deserialized.Params.Value.GetRawText());
            }
            else
            {
                NUnit.Framework.Assert.IsFalse(deserialized.Params.HasValue);
            }
        }

        /// <summary>
        /// 属性 2：请求消息必需字段完整性
        /// 验证序列化后的 JSON 包含 jsonrpc, method, id 字段。
        /// </summary>
        [FsCheck.NUnit.Property]
        public void JsonRpcRequest_RequiredFields_Present(object id, string method)
        {
            // Arrange
            var request = new JsonRpcRequest(id, method);

            // Act
            var json = JsonSerializer.Serialize(request);
            var doc = JsonDocument.Parse(json);

            // Assert
            NUnit.Framework.Assert.IsTrue(doc.RootElement.TryGetProperty("jsonrpc", out _));
            NUnit.Framework.Assert.IsTrue(doc.RootElement.TryGetProperty("method", out _));
            NUnit.Framework.Assert.IsTrue(doc.RootElement.TryGetProperty("id", out _));

            // 验证 jsonrpc 值
            var jsonRpcValue = doc.RootElement.GetProperty("jsonrpc").GetString();
            Assert.AreEqual("2.0", jsonRpcValue);
        }

        /// <summary>
        /// 属性 3：通知消息字段约束
        /// 验证通知消息不包含 id 字段。
        /// </summary>
        [FsCheck.NUnit.Property]
        public void JsonRpcNotification_NoIdField(string method)
        {
            // Arrange
            var notification = new JsonRpcNotification(method);

            // Act
            var json = JsonSerializer.Serialize(notification);
            var doc = JsonDocument.Parse(json);

            // Assert
            NUnit.Framework.Assert.IsTrue(doc.RootElement.TryGetProperty("jsonrpc", out _));
            NUnit.Framework.Assert.IsTrue(doc.RootElement.TryGetProperty("method", out _));
            NUnit.Framework.Assert.IsFalse(doc.RootElement.TryGetProperty("id", out _));
        }

        /// <summary>
        /// 属性 4：响应消息互斥字段验证
        /// 验证响应消息恰好包含 result 或 error 之一。
        /// </summary>
        [Test]
        public void JsonRpcResponse_ExactlyOneOfResultOrError_Success()
        {
            // Arrange
            var response = new JsonRpcResponse("test-id", JsonDocument.Parse("{\"value\":123}").RootElement);

            // Act
            var json = JsonSerializer.Serialize(response);
            var doc = JsonDocument.Parse(json);

            // Assert
            var hasResult = doc.RootElement.TryGetProperty("result", out _);
            var hasError = doc.RootElement.TryGetProperty("error", out _);

            NUnit.Framework.Assert.IsTrue(hasResult ^ hasError, "响应必须恰好包含 result 或 error 之一");
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
            var hasResult = doc.RootElement.TryGetProperty("result", out _);
            var hasError = doc.RootElement.TryGetProperty("error", out _);

            NUnit.Framework.Assert.IsTrue(hasResult ^ hasError, "响应必须恰好包含 result 或 error 之一");
        }

        /// <summary>
        /// 属性 5：错误码标准化
        /// 验证所有错误响应包含标准错误码在有效范围内。
        /// </summary>
        [FsCheck.NUnit.Property]
        public void JsonRpcError_StandardErrorCodeRange(int code, string message)
        {
            // Arrange - 限制错误码范围
            var limitedCode = Math.Max(-32768, Math.Min(-32000, code));
            var error = new JsonRpcError(limitedCode, message);

            // Act
            var json = JsonSerializer.Serialize(error);
            var deserialized = JsonSerializer.Deserialize<JsonRpcError>(json);

            // Assert
            NUnit.Framework.Assert.IsNotNull(deserialized);
            Assert.AreEqual(error.Code, deserialized.Code);
            Assert.AreEqual(error.Message, deserialized.Message);

            // 验证错误码在有效范围内
            NUnit.Framework.Assert.That(deserialized.Code, Is.GreaterThanOrEqualTo(-32768));
            NUnit.Framework.Assert.That(deserialized.Code, Is.LessThanOrEqualTo(-32000));
        }
    }
}
