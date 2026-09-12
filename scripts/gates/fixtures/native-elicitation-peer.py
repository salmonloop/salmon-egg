#!/usr/bin/env python3
"""Deterministic ACP peer for the native product gate; never a third-party Agent substitute."""

import json
from pathlib import Path
import select
import sys


def main():
    scenario = json.loads(Path(sys.argv[1]).read_text())
    log = Path(scenario["log"])
    control = Path(scenario["control"])
    handled_phase = 0
    session_id = "native-elicitation-session"

    def send(message):
        print(json.dumps(dict(jsonrpc="2.0", **message)), flush=True)

    def record(message):
        with log.open("a") as target:
            target.write(json.dumps(message) + "\n")

    while True:
        ready, _, _ = select.select([sys.stdin], [], [], 0.025)
        if ready:
            line = sys.stdin.readline()
            if not line:
                return
            message = json.loads(line)
            method = message.get("method")
            if method == "initialize":
                record({"method": method, "capabilities": message["params"]["clientCapabilities"]})
                send({"id": message["id"], "result": {"protocolVersion": 1,
                      "agentInfo": {"name": "native-elicitation-fixture", "version": "1"},
                      "agentCapabilities": {"loadSession": True, "sessionCapabilities": {"list": {}}}}})
            elif method in ("session/new", "session/load"):
                record({"method": method, "sessionId": session_id})
                send({"id": message["id"], "result": {"sessionId": session_id}})
            elif method == "session/list":
                send({"id": message["id"], "result": {"sessions": [{"sessionId": session_id,
                      "cwd": scenario["cwd"], "title": "Native URL acceptance"}]}})
            elif method == "session/prompt":
                send({"id": message["id"], "result": {"stopReason": "end_turn"}})
            elif method:
                record({"method": method})
                if "id" in message:
                    send({"id": message["id"], "error": {"code": -32601, "message": "Unsupported fixture method"}})
            else:
                record(message)

        if not control.exists():
            continue
        instruction = json.loads(control.read_text())
        phase = instruction["phase"]
        if phase <= handled_phase:
            continue
        handled_phase = phase
        action = instruction["action"]
        if action == "complete":
            send({"method": "elicitation/complete", "params": {"elicitationId": "native-url-open"}})
        elif action == "disconnect":
            record({"sent": action, "phase": phase})
            return
        else:
            label = "native-url-" + action
            send({"id": label, "method": "elicitation/create", "params": {"mode": "url",
                  "sessionId": session_id, "elicitationId": label, "message": label,
                  "url": scenario["url"]}})
        record({"sent": action, "phase": phase})


if __name__ == "__main__":
    main()
