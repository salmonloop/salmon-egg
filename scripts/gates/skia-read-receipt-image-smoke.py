#!/usr/bin/env python3
"""Verify unsupported inline images use readable native text without downloading."""
import argparse
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import json
import os
from pathlib import Path
import subprocess
import threading


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--artifacts", required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    artifacts = args.artifacts.resolve()
    artifacts.mkdir(parents=True, exist_ok=True)
    requests = []

    class ImageHandler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def do_GET(self):
            requests.append(self.path)
            self.send_error(404)

    server = ThreadingHTTPServer(("127.0.0.1", 0), ImageHandler)
    worker = threading.Thread(target=server.serve_forever, daemon=True)
    worker.start()
    environment = dict(os.environ,
                       SALMONEGG_READ_RECEIPT_IMAGE_URL=f"http://127.0.0.1:{server.server_port}/image.png",
                       SALMONEGG_READ_RECEIPT_RESULT_DIR=str(artifacts))
    try:
        result = subprocess.run(["bash", str(root / "scripts/gates/run-skia-read-receipt-probe.sh")],
                                cwd=root, env=environment, check=False, timeout=180)
        assert result.returncode == 0, f"Read receipt gate failed: {result.returncode}"
        assert not requests, f"Unsupported image caused a native download: {requests}"
        print("Unsupported inline image remained readable as full text; no image URL was fetched.")
    finally:
        server.shutdown()
        server.server_close()
        worker.join(timeout=5)
        (artifacts / "image-requests.json").write_text(json.dumps(requests))


if __name__ == "__main__":
    main()
