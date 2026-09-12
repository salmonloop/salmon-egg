#!/usr/bin/env python3
"""Local MCP tool used to verify a real ACP Agent's URL-elicitation forwarding."""

import json
import sys


def send(frame):
    print(json.dumps(frame, separators=(",", ":")), flush=True)


def main():
    url = sys.argv[1]
    tool_request_id = None
    for line in sys.stdin:
        frame = json.loads(line)
        method = frame.get("method")
        if method == "initialize":
            send({"jsonrpc": "2.0", "id": frame["id"], "result": {
                "protocolVersion": frame["params"]["protocolVersion"],
                "capabilities": {"tools": {}},
                "serverInfo": {"name": "salmon-egg-elicitation-acceptance", "version": "1"},
            }})
        elif method == "tools/list":
            send({"jsonrpc": "2.0", "id": frame["id"], "result": {"tools": [{
                "name": "request_url", "description": "Ask for a harmless local test-page confirmation using URL elicitation.",
                "inputSchema": {"type": "object", "properties": {}, "additionalProperties": False},
            }]}})
        elif method == "tools/call":
            tool_request_id = frame["id"]
            send({"jsonrpc": "2.0", "id": "url-confirmation", "method": "elicitation/create", "params": {
                "mode": "url", "message": "Confirm the local interoperability test page.",
                "url": url, "elicitationId": "mcp-url-confirmation",
            }})
        elif frame.get("id") == "url-confirmation":
            accepted = frame.get("result", {}).get("action") == "accept"
            if accepted:
                send({"jsonrpc": "2.0", "method": "notifications/elicitation/complete",
                      "params": {"elicitationId": "mcp-url-confirmation"}})
            send({"jsonrpc": "2.0", "id": tool_request_id, "result": {
                "content": [{"type": "text", "text": "URL_ACCEPTED" if accepted else "URL_CANCELLED"}],
            }})
        elif "id" in frame:
            send({"jsonrpc": "2.0", "id": frame["id"], "error": {"code": -32601, "message": "Method not found"}})


if __name__ == "__main__":
    main()
