import { createHash } from "node:crypto";
import { createServer } from "node:http";

export async function startAcpWebSocketServer(options = {}) {
  const agentReplyText = options.agentReplyText ?? "WASM smoke agent reply";
  const sessionId = options.sessionId ?? "wasm-full-chain-session-01";
  const sessionTitle = options.sessionTitle ?? "WASM full chain session";

  let initializeRequest;
  let sessionNewRequest;
  let sessionPromptRequest;
  let resolveInitialize;
  let resolveSessionNew;
  let resolveSessionPrompt;
  const initializePromise = new Promise(resolve => {
    resolveInitialize = resolve;
  });
  const sessionNewPromise = new Promise(resolve => {
    resolveSessionNew = resolve;
  });
  const sessionPromptPromise = new Promise(resolve => {
    resolveSessionPrompt = resolve;
  });
  const sockets = new Set();

  const server = createServer();
  server.on("upgrade", (request, socket) => {
    const key = request.headers["sec-websocket-key"];
    if (!key) {
      socket.destroy();
      return;
    }

    const accept = createHash("sha1")
      .update(`${key}258EAFA5-E914-47DA-95CA-C5AB0DC85B11`)
      .digest("base64");

    socket.write([
      "HTTP/1.1 101 Switching Protocols",
      "Upgrade: websocket",
      "Connection: Upgrade",
      `Sec-WebSocket-Accept: ${accept}`,
      "",
      ""
    ].join("\r\n"));

    sockets.add(socket);
    let buffer = Buffer.alloc(0);
    socket.on("data", chunk => {
      buffer = Buffer.concat([buffer, chunk]);
      const result = readWebSocketTextFrames(buffer);
      buffer = result.remaining;

      for (const text of result.messages) {
        const message = JSON.parse(text);
        if (message.method === "initialize") {
          initializeRequest = message;
          resolveInitialize(message);
          writeJsonRpc(socket, {
            jsonrpc: "2.0",
            id: message.id,
            result: {
              protocolVersion: 1,
              agentInfo: {
                name: "wasm-smoke-agent",
                title: "WASM Smoke Agent",
                version: "1.0.0"
              },
              agentCapabilities: {}
            }
          });
          continue;
        }

        if (message.method === "session/new") {
          sessionNewRequest = message;
          resolveSessionNew(message);
          writeJsonRpc(socket, {
            jsonrpc: "2.0",
            id: message.id,
            result: {
              sessionId,
              modes: {
                currentModeId: "planner",
                availableModes: [
                  {
                    id: "agent",
                    name: "Agent 01",
                    description: "General conversation mode"
                  },
                  {
                    id: "planner",
                    name: "Planner 01",
                    description: "Structured planning mode"
                  }
                ]
              },
              configOptions: [
                {
                  id: "mode",
                  name: "Mode",
                  description: "Conversation mode",
                  type: "select",
                  currentValue: "planner",
                  options: [
                    {
                      value: "agent",
                      name: "Agent 01"
                    },
                    {
                      value: "planner",
                      name: "Planner 01"
                    }
                  ]
                }
              ]
            }
          });
          writeSessionUpdate(socket, sessionId, {
            sessionUpdate: "session_info_update",
            title: sessionTitle
          });
          continue;
        }

        if (message.method === "session/prompt") {
          sessionPromptRequest = message;
          resolveSessionPrompt(message);
          writeSessionUpdate(socket, sessionId, {
            sessionUpdate: "agent_message_chunk",
            content: {
              type: "text",
              text: agentReplyText
            }
          });
          writeJsonRpc(socket, {
            jsonrpc: "2.0",
            id: message.id,
            result: {
              stopReason: "end_turn",
              userMessageId: message.params?.messageId ?? null
            }
          });
        }
      }
    });
    socket.on("close", () => sockets.delete(socket));
    socket.on("error", () => sockets.delete(socket));
  });

  await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
  const address = server.address();
  const port = typeof address === "object" && address ? address.port : 0;

  return {
    url: `ws://127.0.0.1:${port}/acp`,
    waitForInitialize: async () => {
      if (initializeRequest) {
        return initializeRequest;
      }

      return await waitWithTimeout(
        initializePromise,
        "Timed out waiting for ACP initialize request.",
        30_000);
    },
    waitForSessionNew: async () => {
      if (sessionNewRequest) {
        return sessionNewRequest;
      }

      return await waitWithTimeout(
        sessionNewPromise,
        "Timed out waiting for ACP session/new request.",
        30_000);
    },
    waitForSessionPrompt: async () => {
      if (sessionPromptRequest) {
        return sessionPromptRequest;
      }

      return await waitWithTimeout(
        sessionPromptPromise,
        "Timed out waiting for ACP session/prompt request.",
        30_000);
    },
    close: async () => {
      for (const socket of sockets) {
        socket.destroy();
      }

      await new Promise(resolve => server.close(resolve));
    }
  };
}

async function waitWithTimeout(promise, message, timeoutMs) {
  let timeoutId;
  const timeout = new Promise((_, reject) => {
    timeoutId = setTimeout(() => reject(new Error(message)), timeoutMs);
  });

  try {
    return await Promise.race([promise, timeout]);
  } finally {
    if (timeoutId) {
      clearTimeout(timeoutId);
    }
  }
}

function writeSessionUpdate(socket, sessionId, update) {
  writeJsonRpc(socket, {
    jsonrpc: "2.0",
    method: "session/update",
    params: {
      sessionId,
      update
    }
  });
}

function writeJsonRpc(socket, message) {
  socket.write(encodeWebSocketTextFrame(JSON.stringify(message)));
}

function readWebSocketTextFrames(buffer) {
  const messages = [];
  let offset = 0;

  while (buffer.length - offset >= 2) {
    const first = buffer[offset];
    const second = buffer[offset + 1];
    const opcode = first & 0x0f;
    const masked = (second & 0x80) !== 0;
    let payloadLength = second & 0x7f;
    let headerLength = 2;

    if (payloadLength === 126) {
      if (buffer.length - offset < 4) {
        break;
      }

      payloadLength = buffer.readUInt16BE(offset + 2);
      headerLength = 4;
    } else if (payloadLength === 127) {
      if (buffer.length - offset < 10) {
        break;
      }

      const high = buffer.readUInt32BE(offset + 2);
      const low = buffer.readUInt32BE(offset + 6);
      payloadLength = high * 2 ** 32 + low;
      headerLength = 10;
    }

    const maskLength = masked ? 4 : 0;
    const frameLength = headerLength + maskLength + payloadLength;
    if (buffer.length - offset < frameLength) {
      break;
    }

    let payload = buffer.subarray(offset + headerLength + maskLength, offset + frameLength);
    if (masked) {
      const mask = buffer.subarray(offset + headerLength, offset + headerLength + 4);
      payload = Buffer.from(payload.map((byte, index) => byte ^ mask[index % 4]));
    }

    if (opcode === 0x1) {
      messages.push(payload.toString("utf8"));
    }

    offset += frameLength;
  }

  return {
    messages,
    remaining: buffer.subarray(offset)
  };
}

function encodeWebSocketTextFrame(text) {
  const payload = Buffer.from(text, "utf8");
  if (payload.length < 126) {
    return Buffer.concat([Buffer.from([0x81, payload.length]), payload]);
  }

  if (payload.length <= 0xffff) {
    const header = Buffer.alloc(4);
    header[0] = 0x81;
    header[1] = 126;
    header.writeUInt16BE(payload.length, 2);
    return Buffer.concat([header, payload]);
  }

  const header = Buffer.alloc(10);
  header[0] = 0x81;
  header[1] = 127;
  header.writeUInt32BE(0, 2);
  header.writeUInt32BE(payload.length, 6);
  return Buffer.concat([header, payload]);
}
