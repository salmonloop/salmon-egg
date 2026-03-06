using System.Text.Json;
using UnoAcpClient.Domain.Exceptions;
using UnoAcpClient.Domain.Models;
using UnoAcpClient.Infrastructure.Serialization;

namespace UnoAcpClient.Infrastructure.Tests.Serialization
{
    public class AcpMessageParserTests
    {
        private readonly AcpMessageParser _parser;

        public AcpMessageParserTests()
        {
            _parser = new AcpMessageParser();
        }

        /// <summary>
        /// 测试 ACP 消息序列化的 Round-Trip 特性
        /// 验证序列化后再解析产生等价对象
        /// </summary>
        [Fact]
        public void SerializeThenParseShouldReturnEquivalentMessage()
        {
            // 创建测试消息
            var originalMessage = new AcpMessage
            {
                Id = "test-123",
                Type = "request",
                Method = "test.method",
                Params = JsonDocument.Parse("{\"key\":\"value\",\"number\":42}").RootElement,
                ProtocolVersion = "1.0"
            };

            // 序列化消息
            var json = _parser.SerializeMessage(originalMessage);
            
            // 解析序列化后的 JSON
            var parsedMessage = _parser.ParseMessage(json);
            
            // 验证解析后的消息与原始消息等价
            AssertEquivalentMessages(originalMessage, parsedMessage);
        }

        /// <summary>
        /// 测试无效消息的错误处理
        /// 验证解析无效 JSON 时返回描述性错误
        /// </summary>
        [Fact]
        public void ParseInvalidJsonShouldThrowAcpProtocolException()
        {
            // 无效的 JSON 字符串
            string[] invalidJsonStrings = {
                "",
                "{}",
                "{invalid json}",
                "{\"id\": \"1\"}" // 缺少 type
            };

            foreach (var invalidJson in invalidJsonStrings)
            {
                Assert.Throws<AcpProtocolException>(() =>
                {
                    _parser.ParseMessage(invalidJson);
                });
            }
        }

        /// <summary>
        /// 测试各种消息类型的解析
        /// </summary>
        [Fact]
        public void ParseRequestMessageShouldSucceed()
        {
            var json = "{\"id\":\"1\",\"type\":\"request\",\"method\":\"test\",\"params\":{\"key\":\"value\"}}";
            var message = _parser.ParseMessage(json);
            
            Assert.Equal("1", message.Id);
            Assert.Equal("request", message.Type);
            Assert.Equal("test", message.Method);
        }

        [Fact]
        public void ParseResponseMessageShouldSucceed()
        {
            var json = "{\"id\":\"1\",\"type\":\"response\",\"result\":\"success\"}";
            var message = _parser.ParseMessage(json);
            
            Assert.Equal("1", message.Id);
            Assert.Equal("response", message.Type);
        }

        [Fact]
        public void ParseNotificationMessageShouldSucceed()
        {
            var json = "{\"id\":\"1\",\"type\":\"notification\",\"method\":\"test\",\"params\":{\"key\":\"value\"}}";
            var message = _parser.ParseMessage(json);
            
            Assert.Equal("1", message.Id);
            Assert.Equal("notification", message.Type);
            Assert.Equal("test", message.Method);
        }

        [Fact]
        public void ParseInitializeMessageShouldSucceed()
        {
            var json = "{\"id\":\"1\",\"type\":\"initialize\",\"params\":{\"version\":\"1.0\"}}";
            var message = _parser.ParseMessage(json);
            
            Assert.Equal("1", message.Id);
            Assert.Equal("initialize", message.Type);
        }

        [Fact]
        public void ParseMessageWithMissingRequiredFieldsShouldThrowException()
        {
            // 缺少 ID
            var jsonWithoutId = "{\"type\":\"request\",\"method\":\"test\"}";
            Assert.Throws<AcpProtocolException>(() =>
            {
                _parser.ParseMessage(jsonWithoutId);
            });

            // 缺少 Type
            var jsonWithoutType = "{\"id\":\"1\",\"method\":\"test\"}";
            Assert.Throws<AcpProtocolException>(() =>
            {
                _parser.ParseMessage(jsonWithoutType);
            });

            // 缺少 Method（request 类型）
            var jsonWithoutMethod = "{\"id\":\"1\",\"type\":\"request\"}";
            Assert.Throws<AcpProtocolException>(() =>
            {
                _parser.ParseMessage(jsonWithoutMethod);
            });
        }

        /// <summary>
        /// 辅助方法：验证两个消息是否等价
        /// </summary>
        private void AssertEquivalentMessages(AcpMessage expected, AcpMessage actual)
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Type, actual.Type);
            Assert.Equal(expected.Method, actual.Method);
            
            // 比较 Params
            if (expected.Params.HasValue && actual.Params.HasValue)
            {
                var expectedParams = JsonSerializer.Serialize(expected.Params.Value);
                var actualParams = JsonSerializer.Serialize(actual.Params.Value);
                Assert.Equal(expectedParams, actualParams);
            }
            else
            {
                Assert.Equal(expected.Params.HasValue, actual.Params.HasValue);
            }
            
            // 比较 Result
            if (expected.Result.HasValue && actual.Result.HasValue)
            {
                var expectedResult = JsonSerializer.Serialize(expected.Result.Value);
                var actualResult = JsonSerializer.Serialize(actual.Result.Value);
                Assert.Equal(expectedResult, actualResult);
            }
            else
            {
                Assert.Equal(expected.Result.HasValue, actual.Result.HasValue);
            }
            
            // 比较 Error
            if (expected.Error != null && actual.Error != null)
            {
                Assert.Equal(expected.Error.Code, actual.Error.Code);
                Assert.Equal(expected.Error.Message, actual.Error.Message);
            }
            else
            {
                Assert.Equal(expected.Error == null, actual.Error == null);
            }
            
            Assert.Equal(expected.ProtocolVersion, actual.ProtocolVersion);
        }
    }
}