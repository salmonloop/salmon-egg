#!/usr/bin/env python3
"""Controlled ACP peer to launch behind the actual production bridge, not a replacement bridge."""

import json
import pathlib
import sys


def main():
    if len(sys.argv) != 2:
        raise SystemExit("usage: cancellation-peer.py /absolute/fresh-peer-stdin.ndjson")
    log_path = pathlib.Path(sys.argv[1])
    if not log_path.is_absolute():
        raise SystemExit("peer stdin log must use an absolute path")
    request_count = 0
    with log_path.open("x", encoding="utf-8") as log:
        for line in sys.stdin:
            frame = json.loads(line)
            log.write(json.dumps(frame, separators=(",", ":")) + "\n")
            log.flush()
            method = frame.get("method")
            response = None
            if method == "initialize":
                response = {
                    "jsonrpc": "2.0", "id": frame["id"],
                    "result": {
                        "protocolVersion": 1,
                        "agentInfo": {"name": "cancellation-peer", "version": "1.0"},
                        "agentCapabilities": {},
                    },
                }
            elif method == "session/new":
                request_count += 1
                if request_count == 2:
                    response = {
                        "jsonrpc": "2.0", "id": frame["id"],
                        "result": {"sessionId": "session-after-cancel"},
                    }
            elif method == "$/cancel_request":
                response = {
                    "jsonrpc": "2.0", "id": frame["params"]["requestId"],
                    "error": {"code": -32800, "message": "Cancelled"},
                }
            if response is not None:
                print(json.dumps(response, separators=(",", ":")), flush=True)


if __name__ == "__main__":
    main()
