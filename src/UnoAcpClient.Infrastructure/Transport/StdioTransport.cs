using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnoAcpClient.Domain.Interfaces.Transport;
using UnoAcpClient.Domain.Models.JsonRpc;

namespace UnoAcpClient.Infrastructure.Transport
{
    /// <summary>
    /// Stdio 传输层实现。
    /// 通过标准输入/输出与 Agent 进程通信。
    /// </summary>
    public class StdioTransport : ITransport, IDisposable
    {
        private Process? _process;
        private StreamWriter? _stdin;
        private StreamReader? _stdout;
        private StreamReader? _stderr;
        private CancellationTokenSource? _readCts;
        private readonly string _command;
        private readonly string[] _args;
        private readonly Encoding _encoding;
        private bool _disposed;
        private readonly object _lock = new();

        /// <summary>
        /// 消息接收事件。
        /// </summary>
        public event EventHandler<MessageReceivedEventArgs>? MessageReceived;

        /// <summary>
        /// 传输错误事件。
        /// </summary>
        public event EventHandler<TransportErrorEventArgs>? ErrorOccurred;

        /// <summary>
        /// 判断传输是否已连接。
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// 创建新的 StdioTransport 实例。
        /// </summary>
        /// <param name="command">Agent 可执行文件的命令</param>
        /// <param name="args">命令行参数</param>
        /// <param name="encoding">字符编码</param>
        public StdioTransport(string command, string[]? args = null, Encoding? encoding = null)
        {
            _command = command ?? throw new ArgumentNullException(nameof(command));
            _args = args ?? Array.Empty<string>();
            _encoding = encoding ?? Encoding.UTF8;
        }

        /// <summary>
        /// 建立与 Agent 的连接。
        /// </summary>
        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (IsConnected)
                {
                    return true;
                }

                try
                {
                    _readCts = new CancellationTokenSource();

                    var processInfo = new ProcessStartInfo
                    {
                        FileName = _command,
                        Arguments = string.Join(" ", _args.Select(a => $"\"{a}\"")),
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardInputEncoding = _encoding,
                        StandardOutputEncoding = _encoding,
                        StandardErrorEncoding = _encoding,
                        WorkingDirectory = Environment.CurrentDirectory
                    };

                    _process = new Process { StartInfo = processInfo };
                    _process.EnableRaisingEvents = true;
                    _process.Exited += OnProcessExited;

                    _process.Start();

                    _stdin = _process.StandardInput;
                    _stdout = _process.StandardOutput;
                    _stderr = _process.StandardError;

                    // 启动读取循环
                    _ = ReadLoopAsync(_readCts.Token);
                    _ = ReadErrorLoopAsync(_readCts.Token);

                    IsConnected = true;
                    return true;
                }
                catch (Exception ex)
                {
                    OnErrorOccurred(new TransportErrorEventArgs($"无法启动进程：{ex.Message}", ex));
                    return false;
                }
            }
        }

        /// <summary>
        /// 断开与 Agent 的连接。
        /// </summary>
        public async Task<bool> DisconnectAsync()
        {
            lock (_lock)
            {
                if (!IsConnected)
                {
                    return true;
                }

                try
                {
                    // 取消读取操作
                    _readCts?.Cancel();

                    // 关闭标准输入
                    _stdin?.Flush();
                    _stdin?.Close();
                    _stdin?.Dispose();

                    // 等待进程退出
                    if (_process != null && !_process.HasExited)
                   {
                       try
                       {
                           _process.Kill();
                           _process.WaitForExit();
                       }
                       catch
                       {
                           // 如果进程无法终止，继续执行
                       }
                   }

                    _stdout?.Dispose();
                    _stderr?.Dispose();
                    _process?.Dispose();

                    IsConnected = false;
                    return true;
                }
                catch (Exception ex)
                {
                    OnErrorOccurred(new TransportErrorEventArgs($"断开连接时出错：{ex.Message}", ex));
                    return false;
                }
            }
        }

        /// <summary>
        /// 发送消息。
        /// </summary>
        public async Task<bool> SendMessageAsync(string message, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (!IsConnected || _stdin == null)
                {
                    OnErrorOccurred(new TransportErrorEventArgs("传输未连接"));
                    return false;
                }

                // 在 lock 外进行异步操作
            }

            try
            {
                // 发送消息后添加换行符
                await _stdin.WriteLineAsync(message);
                await _stdin.FlushAsync();
                return true;
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new TransportErrorEventArgs($"发送消息失败：{ex.Message}", ex));
                return false;
            }
        }

        /// <summary>
        /// 读取输出循环。
        /// </summary>
        private async Task ReadLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _stdout != null && !_stdout.EndOfStream)
                {
                    var line = await _stdout.ReadLineAsync();

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        OnMessageReceived(new MessageReceivedEventArgs(line));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new TransportErrorEventArgs($"读取输出失败：{ex.Message}", ex));
            }
        }

        /// <summary>
        /// 读取错误循环。
        /// </summary>
        private async Task ReadErrorLoopAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && _stderr != null)
                {
                    var line = await _stderr.ReadLineAsync();

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        // 将 stderr 作为错误事件触发
                        OnErrorOccurred(new TransportErrorEventArgs($"进程错误：{line}"));
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
            catch (Exception ex)
            {
                OnErrorOccurred(new TransportErrorEventArgs($"读取错误流失败：{ex.Message}", ex));
            }
        }

        /// <summary>
        /// 进程退出事件处理。
        /// </summary>
        private void OnProcessExited(object? sender, EventArgs e)
        {
            IsConnected = false;
            _readCts?.Cancel();
            OnErrorOccurred(new TransportErrorEventArgs("Agent 进程已退出"));
        }

        /// <summary>
        /// 触发消息接收事件。
        /// </summary>
        protected virtual void OnMessageReceived(MessageReceivedEventArgs e)
        {
            MessageReceived?.Invoke(this, e);
        }

        /// <summary>
        /// 触发错误事件。
        /// </summary>
        protected virtual void OnErrorOccurred(TransportErrorEventArgs e)
        {
            ErrorOccurred?.Invoke(this, e);
        }

        /// <summary>
        /// 释放资源。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisconnectAsync().Wait();
            GC.SuppressFinalize(this);
        }
    }
}
