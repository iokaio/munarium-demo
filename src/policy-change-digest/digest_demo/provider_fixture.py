# SPDX-License-Identifier: Apache-2.0
"""Canned protocol responses read only supplied evidence, never the oracle."""

import json
import re
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from threading import Lock

LOCK = Lock()
STATE = {"mode": "ok", "calls": 0}


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_args):
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
            self.respond({"models": [{"name": "digest-fixture", "model": "digest-fixture"}]})
        elif self.path == "/state":
            with LOCK:
                self.respond(dict(STATE))
        else:
            self.respond({}, 404)

    def do_POST(self):
        size = int(self.headers.get("Content-Length", "0"))
        if not 0 < size < 1_000_000:
            self.respond({}, 400)
            return
        body = json.loads(self.rfile.read(size))
        if self.path == "/mode":
            if body.get("mode") not in ("ok", "unavailable", "irrelevant"):
                self.respond({}, 400)
                return
            with LOCK:
                STATE["mode"] = body["mode"]
            self.respond({})
            return
        if self.path != "/api/chat":
            self.respond({}, 404)
            return
        with LOCK:
            STATE["calls"] += 1
            mode = STATE["mode"]
        if mode == "unavailable":
            self.respond({"error": "Synthetic dependency outage"}, 503)
            return
        prompt = "\n".join(message["content"] for message in body["messages"])
        comparison = json.JSONDecoder().raw_decode(prompt.split("REVISION_COMPARISON=", 1)[1])[0]
        answer = {
            "status": comparison["status"],
            "explanation": "Candidate impacts only; retrieved documents do not establish exhaustive coverage.",
            "candidates": [],
        }
        if comparison["status"] == "candidate_impacts":
            labels = re.findall(r"\[([^\]]+)\] # Fictional (?:policy|downstream checklist)", prompt)
            ident = re.search(r"Checklist ID: (checklist-\d+)", prompt).group(1)
            if mode == "irrelevant":
                labels = re.findall(r"\[([^\]]+)\] # Fictional unrelated checklist", prompt)
            answer["candidates"] = [
                {
                    "checklist_id": ident,
                    "explanation": "Review the checklist instruction because the referenced policy rule changed.",
                    "citations": labels,
                }
            ]
        self.respond(
            {
                "model": body["model"],
                "done": True,
                "done_reason": "stop",
                "message": {"role": "assistant", "content": json.dumps(answer)},
                "prompt_eval_count": 120,
                "eval_count": 60,
            }
        )


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", 11434), Handler).serve_forever()
