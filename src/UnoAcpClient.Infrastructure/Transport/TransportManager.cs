using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnoAcpClient.Domain.Interfaces.Transport;

namespace UnoAcpClient.Infrastructure.Transport
{
    /// <summary>
    /// 传输管理器实现。
    /// 用于管理多个传输连接的注册、检索和生命周期。
    /// </summary>
    public class TransportManager : ITransportManager
    {
        private readonly ConcurrentDictionary<string, ITransport> _transports = new();
        private readonly object _lock = new();

        /// <summary>
        /// 注册新的传输连接。
        /// </summary>
        public async Task<string> RegisterTransportAsync(ITransport transport, string? transportId = null)
        {
            if (transport == null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            // 如果没有指定 ID，生成一个唯一的 ID
            if (string.IsNullOrWhiteSpace(transportId))
            {
                transportId = GenerateTransportId();
            }

            // 如果已经存在相同 ID 的传输，先断开并移除旧的
            if (_transports.TryGetValue(transportId, out var existingTransport))
            {
                await DisconnectTransportAsync(transportId);
                _transports.TryRemove(transportId, out _);
            }

            // 注册传输事件处理
            transport.MessageReceived += OnTransportMessageReceived;
            transport.ErrorOccurred += OnTransportErrorOccurred;

            _transports[transportId] = transport;

            return transportId;
        }

        /// <summary>
        /// 根据 ID 获取传输连接。
        /// </summary>
        public ITransport? GetTransport(string transportId)
        {
            if (string.IsNullOrWhiteSpace(transportId))
            {
                return null;
            }

            return _transports.TryGetValue(transportId, out var transport) ? transport : null;
        }

        /// <summary>
        /// 断开指定的传输连接。
        /// </summary>
        public async Task<bool> DisconnectTransportAsync(string transportId)
        {
            if (string.IsNullOrWhiteSpace(transportId))
            {
                return false;
            }

            if (_transports.TryGetValue(transportId, out var transport))
            {
                try
                {
                    // 移除事件处理
                    transport.MessageReceived -= OnTransportMessageReceived;
                    transport.ErrorOccurred -= OnTransportErrorOccurred;

                    // 断开连接
                    var result = await transport.DisconnectAsync();
                    _transports.TryRemove(transportId, out _);
                    return result;
                }
                catch (Exception)
                {
                    // 即使出错也移除传输
                    _transports.TryRemove(transportId, out _);
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// 获取所有活跃的传输 ID 列表。
        /// </summary>
        public IEnumerable<string> GetActiveTransportIds()
        {
            return _transports.Keys.ToList();
        }

        /// <summary>
        /// 根据 ID 移除传输连接（不执行断开操作）。
        /// </summary>
        public bool RemoveTransport(string transportId)
        {
            if (string.IsNullOrWhiteSpace(transportId))
            {
                return false;
            }

            if (_transports.TryGetValue(transportId, out var transport))
            {
                // 移除事件处理
                transport.MessageReceived -= OnTransportMessageReceived;
                transport.ErrorOccurred -= OnTransportErrorOccurred;

                return _transports.TryRemove(transportId, out _);
            }

            return false;
        }

        /// <summary>
        /// 断开所有传输连接。
        /// </summary>
        public async Task<int> DisconnectAllTransportsAsync()
        {
            var transportIds = _transports.Keys.ToList();
            int count = 0;

            foreach (var transportId in transportIds)
            {
                if (await DisconnectTransportAsync(transportId))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 获取活跃的传输数量。
        /// </summary>
        public int GetActiveTransportCount()
        {
            return _transports.Count;
        }

        /// <summary>
        /// 获取第一个活跃的传输（如果有）。
        /// </summary>
        public ITransport? GetFirstActiveTransport()
        {
            return _transports.Values.FirstOrDefault(t => t.IsConnected);
        }

        /// <summary>
        /// 获取第一个连接的传输 ID（如果有）。
        /// </summary>
        public string? GetFirstActiveTransportId()
        {
            foreach (var kvp in _transports)
            {
                if (kvp.Value.IsConnected)
                {
                    return kvp.Key;
                }
            }

            return null;
        }

        /// <summary>
        /// 生成唯一的传输 ID。
        /// </summary>
        private static string GenerateTransportId()
        {
            return $"transport_{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        }

        /// <summary>
        /// 当传输收到消息时触发的事件。
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        /// <summary>
        /// 当传输发生错误时触发的事件。
        /// </summary>
        public event EventHandler<TransportErrorEventArgs>? ErrorOccurred;

        /// <summary>
        /// 处理传输的消息接收事件。
        /// </summary>
        private void OnTransportMessageReceived(object? sender, MessageReceivedEventArgs e)
        {
            MessageReceived?.Invoke(this, e);
        }

        /// <summary>
        /// 处理传输的错误事件。
        /// </summary>
        private void OnTransportErrorOccurred(object? sender, TransportErrorEventArgs e)
        {
            ErrorOccurred?.Invoke(this, e);
        }
    }
}
