# Uno ACP Client 使用指南

## 如何配置 ACP Server

### 方法 1: 通过设置页面配置（推荐）

1. **打开设置页面**
   - 在主页面点击右上角的"⚙️ 设置"按钮
   - 将打开设置页面

2. **添加服务器配置**
   - 点击"添加配置"按钮
   - 填写服务器信息：
     - **名称**: 给服务器起一个易于识别的名字（如"本地开发服务器"）
     - **服务器 URL**: ACP 服务器的地址
       - WebSocket 格式: `ws://localhost:8080` 或 `wss://your-server.com`
       - HTTP SSE 格式: `http://localhost:8080/sse` 或 `https://your-server.com/sse`
     - **传输类型**: 选择 WebSocket 或 HTTP SSE
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
- Windows: `%LOCALAPPDATA%\UnoAcpClient\configurations\`
- 每个配置都是一个 JSON 文件

配置文件格式示例：
```json
{
  "id": "server-001",
  "name": "本地开发服务器",
  "serverUrl": "ws://localhost:8080",
  "transport": 0,
  "heartbeatInterval": 30,
  "connectionTimeout": 10,
  "authentication": {
    "token": "your-token",
    "apiKey": "your-api-key"
  },
  "proxy": {
    "enabled": false,
    "proxyUrl": ""
  }
}
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
- 查看日志文件（位于 `%LOCALAPPDATA%\UnoAcpClient\logs\`）

### 连接后立即断开
- 检查认证信息是否正确
- 确认服务器支持所选传输类型
- 查看服务器日志

### 找不到配置
- 确认已保存配置
- 检查配置文件是否存在
- 尝试重新添加配置

## 开发和调试

### 启用调试日志
在应用启动时会自动创建日志文件，位于：
- Windows: `%LOCALAPPDATA%\UnoAcpClient\logs\`

### 运行测试
```bash
dotnet test
```

### 构建
```bash
dotnet build --configuration Release
```
