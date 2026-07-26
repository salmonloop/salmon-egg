using System;
using System.Text;

namespace SalmonEgg.Infrastructure.Network;

/// <summary>
/// Server-Sent Events 行级累加器,按 WHATWG EventSource 处理模型解析:
/// 字段名后冒号、值前至多一个空格可选;多行 data 以 "\n" 连接;
/// 空行派发事件;":" 开头为注释;event/id/retry 等字段识别但不改变派发语义
/// (ACP Streamable HTTP 草案 v1 未启用流恢复,id 仅透传给日志层)。
/// </summary>
internal sealed class SseEventAccumulator
{
    private readonly StringBuilder _data = new();
    private bool _hasData;

    /// <summary>
    /// 送入一行(不含行结束符)。当该行结束一个事件且事件含 data 字段时,
    /// 返回 true 并输出以 "\n" 连接的完整 data 负载。
    /// </summary>
    public bool TryAppendLine(string line, out string? dispatchedData)
    {
        dispatchedData = null;
        if (line.Length == 0)
        {
            if (!_hasData)
            {
                return false;
            }

            dispatchedData = _data.ToString();
            _data.Clear();
            _hasData = false;
            return true;
        }

        if (line[0] == ':')
        {
            return false;
        }

        var separatorIndex = line.IndexOf(':');
        string fieldName;
        string fieldValue;
        if (separatorIndex < 0)
        {
            fieldName = line;
            fieldValue = string.Empty;
        }
        else
        {
            fieldName = line.Substring(0, separatorIndex);
            var valueStart = separatorIndex + 1;
            if (valueStart < line.Length && line[valueStart] == ' ')
            {
                valueStart++;
            }

            fieldValue = line.Substring(valueStart);
        }

        if (!string.Equals(fieldName, "data", StringComparison.Ordinal))
        {
            return false;
        }

        if (_hasData)
        {
            _data.Append('\n');
        }

        _data.Append(fieldValue);
        _hasData = true;
        return false;
    }
}
