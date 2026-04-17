# SalmonEgg 使用指南

## 如何配置 ACP Server

### 方法 1: 通过设置页面配置（推荐）

1. **打开设置页面**
   - 在主页面点击右上角的"⚙️ 设置"按钮
   - 将打开设置页面

2. **添加服务器配置**
   - 点击"添加配置"按钮
   - 填写服务器信息：
     - **名称**: 给服务器起一个易于识别的名字（如"本地开发服务器"）
     - **传输类型**:
       - `Stdio（子进程）`: 启动本地 Agent，或启动 `ssh` 这类桥接进程
       - `WebSocket`: 远程 WebSocket ACP 服务
       - `HTTP SSE`: 远程 HTTP SSE ACP 服务
     - **服务器 URL**: 当传输类型为 WebSocket / HTTP SSE 时填写
       - WebSocket 格式: `ws://localhost:8080` 或 `wss://your-server.com`
       - HTTP SSE 格式: `http://localhost:8080/sse` 或 `https://your-server.com/sse`
     - **Stdio 命令 / 参数**: 当传输类型为 `Stdio（子进程）` 时填写
       - 本地示例: `python` + `server.py --port 8080`
       - SSH bridge 示例:
         - 命令: `ssh`
         - 参数: `-T -o BatchMode=yes -o RequestTTY=no -o LogLevel=ERROR user@host /opt/acp/bin/agent stdio`
     - **心跳间隔**: 心跳消息间隔（默认 30 秒）
     - **连接超时**: 连接超时时间（默认 10 秒）
     - **认证信息**（可选）:
       - Token: 认证令牌
       - API Key: API 密钥
     - **代理配置**（可选）:
       - 启用代理
       - 代理 URL: 如 `http://proxy.example.com:8080`
   - 点击"保存"保存配置

3. **连接服务器**
   - 返回主页面
   - 在"服务器"下拉框中选择刚添加的服务器
   - 点击"连接"按钮

### 方法 2: 直接编辑配置文件

配置文件存储位置：
- Windows: `%LOCALAPPDATA%\SalmonEgg\config\servers\`
- 每个配置都是一个 YAML 文件

配置文件格式示例：
```yaml
schema_version: 1
id: "server-stdio-ssh"
name: "远程 SSH Agent"
transport: "stdio"
stdio_command: "ssh"
stdio_args: "-T -o BatchMode=yes -o RequestTTY=no -o LogLevel=ERROR user@host /opt/acp/bin/agent stdio"
heartbeat_interval_seconds: 30
connection_timeout_seconds: 10
authentication:
  mode: "none"
proxy:
  enabled: false
  proxy_url: ""
```

## 常用 ACP Server 地址

### 本地开发
- WebSocket: `ws://localhost:8080`
- HTTP SSE: `http://localhost:8080/sse`

### 示例服务器
- MCP Test Server: `ws://localhost:3000`

## 发送消息

### 常用方法

1. **初始化连接**
   - 方法: `initialize`
   - 参数: `{"version": "1.0"}`

2. **列出工具**
   - 方法: `tools/list`
   - 参数: `{}`

3. **调用工具**
   - 方法: `tools/call`
   - 参数: `{"name": "tool-name", "arguments": {"arg1": "value1"}}`

4. **列出资源**
   - 方法: `resources/list`
   - 参数: `{}`

## 故障排除

### 无法连接
- 检查服务器 URL 是否正确
- 确认服务器已启动
- 检查防火墙设置
- 查看日志文件（位于 `%LOCALAPPDATA%\SalmonEgg\logs\`）

### 连接后立即断开
- 检查认证信息是否正确
- 确认服务器支持所选传输类型
- 查看服务器日志
- 如果使用 SSH bridge:
  - 不要使用 `ssh -t`
  - 确保远端 shell 不会向 `stdout` 输出 banner / MOTD / 欢迎语
  - 优先启用 `-o BatchMode=yes`

### 找不到配置
- 确认已保存配置
- 检查配置文件是否存在
- 尝试重新添加配置

## 开发和调试

### 启用调试日志
在应用启动时会自动创建日志文件，位于：
- Windows: `%LOCALAPPDATA%\SalmonEgg\logs\`

### 运行测试
```bash
dotnet test
```

### 构建
```bash
dotnet build --configuration Release
```
