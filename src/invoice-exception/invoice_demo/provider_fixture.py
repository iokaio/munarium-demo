# SPDX-License-Identifier: Apache-2.0
"""Controlled provider responses prove wiring, not model quality. No oracle access."""

import json
import re
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from threading import Lock

LOCK = Lock()
STATE = {"mode": "ok", "calls": 0}


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass

    def respond(self, body, status=200):
        raw = json.dumps(body).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def do_GET(self):
        if self.path == "/api/tags":
            self.respond({"models": [{"name": "invoice-fixture", "model": "invoice-fixture"}]})
        elif self.path == "/state":
            with LOCK:
                self.respond(dict(STATE))
        else:
            self.respond({"error": "unknown route"}, 404)

    def do_POST(self):
        size = int(self.headers.get("Content-Length", "0"))
        if not 0 < size < 1000000:
            self.respond({"error": "bad request size"}, 400)
            return
        body = json.loads(self.rfile.read(size))
        if self.path == "/mode":
            if body.get("mode") not in ("ok", "unavailable", "bad_citation"):
                self.respond({"error": "unknown mode"}, 400)
                return
            with LOCK:
                STATE["mode"] = body["mode"]
            self.respond({"ok": True})
            return
        if self.path != "/api/chat":
            self.respond({"error": "unknown route"}, 404)
            return
        with LOCK:
            STATE["calls"] += 1
            mode = STATE["mode"]
        if mode == "unavailable":
            self.respond({"error": "synthetic provider outage"}, 503)
            return
        prompt = "\n".join(message["content"] for message in body["messages"])
        facts = json.JSONDecoder().raw_decode(prompt.split("DETERMINISTIC_FACTS=", 1)[1])[0]
        paths = re.findall(r"\[([^\]]+)\] # Fictional purchasing policy", prompt)
        explanation = "Fictional policy review: " + ", ".join(facts["exceptions"] or ["matched"])
        if facts["receipt_status"] == "missing":
            explanation += (
                ". Missing receipt: insufficient evidence of delivery; request the receipt."
            )
        answer = {
            "explanation": explanation,
            "citations": [paths[0] if paths and mode == "ok" else "unserved/policy.md"],
            "recommended_action": "review" if facts["exceptions"] else "acknowledge",
            "receipt_status": facts["receipt_status"],
        }
        self.respond(
            {
                "model": body["model"],
                "done": True,
                "done_reason": "stop",
                "message": {"role": "assistant", "content": json.dumps(answer)},
                "prompt_eval_count": 100,
                "eval_count": 50,
            }
        )


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", 11434), Handler).serve_forever()
