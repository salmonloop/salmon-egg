#!/usr/bin/env python3
"""A bounded, local ACP peer and browser observer for native mobile UI acceptance."""

import argparse
import asyncio
import json
import os
from pathlib import Path

from aiohttp import web


class ElicitationPeer:
    def __init__(self, artifacts):
        self.artifacts = artifacts
        self.connection = None
        self.capabilities = None
        self.loaded = False
        self.responses = []
        self.visits = []
        self.reports = []
        self.offered = []
        self.nonce = os.urandom(16).hex()
        self.private_value = "mobile-page-private-canary"
        self.url = ""

    def state(self):
        return {"capabilities": self.capabilities, "loaded": self.loaded,
                "responses": self.responses, "visits": self.visits,
                "reports": self.reports, "offered": self.offered}

    def persist(self):
        (self.artifacts / "peer-state.json").write_text(json.dumps(self.state(), indent=2) + "\n")

    async def connect(self, request):
        socket = web.WebSocketResponse()
        await socket.prepare(request)
        self.connection = socket
        try:
            async for frame in socket:
                if frame.type != web.WSMsgType.TEXT:
                    continue
                message = json.loads(frame.data)
                method = message.get("method")
                result = {}
                if method == "initialize":
                    self.capabilities = message["params"]["clientCapabilities"]
                    result = {"protocolVersion": 1,
                              "agentInfo": {"name": "mobile-elicitation-fixture", "version": "1"},
                              "agentCapabilities": {"loadSession": True, "sessionCapabilities": {"list": {}}}}
                elif method in ("session/load", "session/new"):
                    self.loaded = True
                    result = {"sessionId": "native-elicitation-session"}
                elif method == "session/list":
                    result = {"sessions": [{"sessionId": "native-elicitation-session", "cwd": "/acceptance",
                                            "title": "Native acceptance session"}]}
                elif method == "session/prompt":
                    result = {"stopReason": "end_turn"}
                elif not method:
                    self.responses.append(message)
                    self.persist()
                    continue
                elif "id" in message:
                    await socket.send_json({"jsonrpc": "2.0", "id": message["id"],
                                            "error": {"code": -32601, "message": "Unsupported fixture method"}})
                    continue
                if "id" in message:
                    await socket.send_json({"jsonrpc": "2.0", "id": message["id"], "result": result})
                self.persist()
        finally:
            if self.connection is socket:
                self.connection = None
        return socket

    async def control(self, request):
        instruction = await request.json()
        action = instruction["action"]
        socket = self.connection
        if socket is None:
            raise web.HTTPConflict(text="The product has no ACP connection")
        if action == "disconnect":
            await socket.close()
        elif action == "complete":
            await socket.send_json({"jsonrpc": "2.0", "method": "elicitation/complete",
                                    "params": {"elicitationId": instruction.get("id", "native-url-open")}})
        else:
            label = "native-" + action
            params = {"mode": "form" if action.startswith("form-") else "url",
                      "sessionId": "native-elicitation-session", "message": label}
            if params["mode"] == "url":
                params.update(elicitationId=label, url=self.url)
            else:
                params["requestedSchema"] = {"type": "object", "properties": {
                    "answer": {"type": "string", "title": "Acceptance answer", "minLength": 1}},
                    "required": ["answer"]}
            await socket.send_json({"jsonrpc": "2.0", "id": label, "method": "elicitation/create", "params": params})
            self.offered.append(label)
        self.persist()
        return web.json_response(self.state())

    async def inspect(self, _request):
        return web.json_response(dict(self.state(), url=self.url))

    async def page(self, request):
        if request.query.get("token") != self.nonce:
            raise web.HTTPNotFound()
        self.visits.append({"referrer": request.headers.get("Referer", ""),
                            "userAgent": request.headers.get("User-Agent", "")})
        self.persist()
        # The page's private field travels only to this browser observer, never over ACP.
        return web.Response(text="<!doctype html><title>Mobile external acceptance</title>"
                            "<input id='private' value='" + self.private_value + "'>"
                            "<script>fetch('/report',{method:'POST',headers:{'Content-Type':'application/json'},"
                            "body:JSON.stringify({openerNull:window.opener===null,referrer:document.referrer,"
                            "privateValue:document.getElementById('private').value})});</script>", content_type="text/html")

    async def report(self, request):
        self.reports.append(await request.json())
        self.persist()
        return web.Response(status=204)


async def serve(args):
    args.artifacts.mkdir(parents=True, exist_ok=True)
    peer = ElicitationPeer(args.artifacts)
    app = web.Application()
    app.add_routes([web.get("/acp", peer.connect), web.post("/control", peer.control),
                    web.get("/state", peer.inspect), web.get("/authorize", peer.page), web.post("/report", peer.report)])
    runner = web.AppRunner(app, access_log=None)
    await runner.setup()
    site = web.TCPSite(runner, "127.0.0.1", args.port)
    await site.start()
    peer.url = f"http://127.0.0.1:{args.port}/authorize?token={peer.nonce}"
    peer.persist()
    args.ready.write_text(json.dumps({"endpoint": f"ws://127.0.0.1:{args.port}/acp",
                                     "control": f"http://127.0.0.1:{args.port}"}))
    try:
        await asyncio.Event().wait()
    finally:
        await runner.cleanup()


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", required=True, type=int)
    parser.add_argument("--ready", required=True, type=Path)
    parser.add_argument("--artifacts", required=True, type=Path)
    asyncio.run(serve(parser.parse_args()))
