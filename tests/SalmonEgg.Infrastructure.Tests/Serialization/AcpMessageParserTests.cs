using System;
using System.Text.Json;
using SalmonEgg.Domain.Exceptions;
using SalmonEgg.Domain.Models;
using SalmonEgg.Infrastructure.Serialization;

namespace SalmonEgg.Infrastructure.Tests.Serialization
{
    public class AcpMessageParserTests
    {
        private readonly AcpMessageParser _parser;

        public AcpMessageParserTests()
        {
            _parser = new AcpMessageParser();
        }

        /// <summary>
        /// Tests ACP message serialization Round-Trip
        /// Verifies that serialization followed by parsing produces equivalent objects
        /// </summary>
        [Fact]
        public void SerializeThenParseShouldReturnEquivalentMessage()
        {
            // Arrange
            var originalMessage = new AcpMessage
            {
                Id = "test-123",
                Type = "request",
                Method = "test.method",
                Params = JsonDocument.Parse("{\"key\":\"value\",\"number\":42}").RootElement,
                ProtocolVersion = "1.0"
            };

            // Act
            var json = _parser.SerializeMessage(originalMessage);
            var parsedMessage = _parser.ParseMessage(json);
            
            // Assert
            AssertEquivalentMessages(originalMessage, parsedMessage);
        }

        /// <summary>
        /// Tests ACP message serialization Round-Trip for different message types
        /// Verifies that serialization followed by parsing produces equivalent objects
        /// </summary>
        [Theory]
        [InlineData("request")]
        [InlineData("response")]
        [InlineData("notification")]
        [InlineData("initialize")]
        public void SerializeThenParseShouldReturnEquivalentMessageForDifferentTypes(string messageType)
        {
            // Arrange
            var originalMessage = CreateTestMessage(messageType);

            // Act
            var json = _parser.SerializeMessage(originalMessage);
            var parsedMessage = _parser.ParseMessage(json);
            
            // Assert
            AssertEquivalentMessages(originalMessage, parsedMessage);
        }

        /// <summary>
        /// Tests error handling for invalid messages
        /// Verifies that parsing invalid JSON returns descriptive errors
        /// </summary>
        [Fact]
        public void ParseInvalidJsonShouldThrowAcpProtocolException()
        {
            // Arrange
            string[] invalidJsonStrings = {
                "",
                "{}",
                "{invalid json}",
                "{\"id\": \"1\"}" // Missing type
            };

            // Act & Assert
            foreach (var invalidJson in invalidJsonStrings)
            {
                Assert.Throws<AcpProtocolException>(() =>
                {
                    _parser.ParseMessage(invalidJson);
                });
            }
        }

        /// <summary>
        /// Tests error handling for invalid JSON formats (extended test cases)
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("{}")]
        [InlineData("{invalid json}")]
        [InlineData("{\"id\": \"1\"}")]
        [InlineData("{\"type\": \"request\"}")]
        [InlineData("{\"id\": \"1\", \"type\": \"request\"}")]
        public void ParseInvalidJsonShouldThrowAcpProtocolExceptionWithDifferentCases(string invalidJson)
        {
            Assert.Throws<AcpProtocolException>(() =>
            {
                _parser.ParseMessage(invalidJson);
            });
        }

        /// <summary>
        /// Tests parsing of various message types
        /// </summary>
        [Fact]
        public void ParseRequestMessageShouldSucceed()
        {
            // Arrange
            var json = "{\"id\":\"1\",\"type\":\"request\",\"method\":\"test\",\"params\":{\"key\":\"value\"}}";
            
            // Act
            var message = _parser.ParseMessage(json);
            
            // Assert
            Assert.Equal("1", message.Id);
            Assert.Equal("request", message.Type);
            Assert.Equal("test", message.Method);
        }

        [Fact]
        public void ParseResponseMessageShouldSucceed()
        {
            // Arrange
            var json = "{\"id\":\"1\",\"type\":\"response\",\"result\":\"success\"}";
            
            // Act
            var message = _parser.ParseMessage(json);
            
            // Assert
            Assert.Equal("1", message.Id);
            Assert.Equal("response", message.Type);
        }

        [Fact]
        public void ParseNotificationMessageShouldSucceed()
        {
            // Arrange
            var json = "{\"id\":\"1\",\"type\":\"notification\",\"method\":\"test\",\"params\":{\"key\":\"value\"}}";
            
            // Act
            var message = _parser.ParseMessage(json);
            
            // Assert
            Assert.Equal("1", message.Id);
            Assert.Equal("notification", message.Type);
            Assert.Equal("test", message.Method);
        }

        [Fact]
        public void ParseInitializeMessageShouldSucceed()
        {
            // Arrange
            var json = "{\"id\":\"1\",\"type\":\"initialize\",\"params\":{\"version\":\"1.0\"}}";
            
            // Act
            var message = _parser.ParseMessage(json);
            
            // Assert
            Assert.Equal("1", message.Id);
            Assert.Equal("initialize", message.Type);
        }

        [Fact]
        public void ParseMessageWithMissingRequiredFieldsShouldThrowException()
        {
            // Arrange - Missing ID
            var jsonWithoutId = "{\"type\":\"request\",\"method\":\"test\"}";
            Assert.Throws<AcpProtocolException>(() =>
            {
                _parser.ParseMessage(jsonWithoutId);
            });

            // Arrange - Missing Type
            var jsonWithoutType = "{\"id\":\"1\",\"method\":\"test\"}";
            Assert.Throws<AcpProtocolException>(() =>
            {
                _parser.ParseMessage(jsonWithoutType);
            });

            // Arrange - Missing Method (request type)
            var jsonWithoutMethod = "{\"id\":\"1\",\"type\":\"request\"}";
            Assert.Throws<AcpProtocolException>(() =>
            {
                _parser.ParseMessage(jsonWithoutMethod);
            });
        }

        /// <summary>
        /// Creates a test message for different message types
        /// </summary>
        private AcpMessage CreateTestMessage(string messageType)
        {
            var message = new AcpMessage
            {
                Id = Guid.NewGuid().ToString(),
                Type = messageType,
                ProtocolVersion = "1.0"
            };

            switch (messageType)
            {
                case "request":
                case "notification":
                    message.Method = "test.method";
                    message.Params = JsonDocument.Parse("{\"key\":\"value\",\"number\":42}").RootElement;
                    break;
                case "response":
                    message.Result = JsonDocument.Parse("\"success\"").RootElement;
                    break;
                case "initialize":
                    message.Params = JsonDocument.Parse("{\"version\":\"1.0\"}").RootElement;
                    break;
            }

            return message;
        }

        /// <summary>
        /// Helper method: Verifies that two messages are equivalent
        /// </summary>
        private void AssertEquivalentMessages(AcpMessage expected, AcpMessage actual)
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Type, actual.Type);
            Assert.Equal(expected.Method, actual.Method);
            
            // Compare Params
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
            
            // Compare Result
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
            
            // Compare Error
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