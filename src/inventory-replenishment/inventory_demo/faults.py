# SPDX-License-Identifier: Apache-2.0
"""Local transport fault injection around the real Server."""

from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import httpx

MODE = "normal"


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        pass

    def handle_request(self):
        global MODE
        if self.path.startswith("/control/"):
            MODE = self.path.rsplit("/", 1)[-1]
            self.send_response(200)
            self.end_headers()
            return
        if MODE == "outage":
            self.send_error(502)
            return
        size = int(self.headers.get("Content-Length", "0"))
        if size > 1_000_000:
            self.send_error(413)
            return
        headers = {
            key: value
            for key, value in self.headers.items()
            if key.lower() in ("authorization", "x-munarium-uid", "x-uid", "content-type", "accept")
        }
        response = httpx.request(
            self.command,
            "http://server:8080" + self.path,
            headers=headers,
            content=self.rfile.read(size),
            timeout=120,
        )
        if MODE == "drop" and self.command == "POST" and "/turns" in self.path:
            self.send_error(502, "Synthetic lost accepted response")
            return
        self.send_response(response.status_code)
        self.send_header("Content-Type", response.headers.get("Content-Type", "application/json"))
        self.send_header("Content-Length", str(len(response.content)))
        self.end_headers()
        self.wfile.write(response.content)

    do_GET = handle_request
    do_POST = handle_request


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", 11435), Handler).serve_forever()
