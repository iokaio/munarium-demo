# SPDX-License-Identifier: Apache-2.0
"""Canned evidence-based answers; no model or label-file access."""

import json
import re
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from threading import Lock

LOCK = Lock()
STATE = {"mode": "ok", "calls": 0}


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_args):
        pass

    def respond(self, value, status=200):
        raw = json.dumps(value).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(raw)))
        self.end_headers()
        self.wfile.write(raw)

    def do_GET(self):
        if self.path == "/api/tags":
            self.respond({"models": [{"name": "bench-fixture", "model": "bench-fixture"}]})
        elif self.path == "/state":
            with LOCK:
                self.respond(dict(STATE))
        else:
            self.respond({}, 404)

    def do_POST(self):
        length = int(self.headers.get("Content-Length", "0"))
        if not 0 < length < 1_000_000:
            self.respond({}, 400)
            return
        body = json.loads(self.rfile.read(length))
        if self.path == "/mode":
            if body.get("mode") not in ("ok", "irrelevant", "unavailable"):
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
            self.respond({"error": "Synthetic provider outage"}, 503)
            return
        prompt = "\n".join(message["content"] for message in body["messages"])
        question = prompt.rsplit("Question:", 1)[-1].strip()
        match = re.search(r"What are the (.+?) submission and approval requirements", question)
        topic = (
            match.group(1)
            if match
            else "private retention"
            if "private retention marker" in question
            else "lunar taxi"
        )
        chunks = re.findall(
            r"\[([^\]]+)\] (# Fictional .*?)(?=\n\n\[|\nQuestion:|\Z)", prompt, re.S
        )
        selected = [(label, text) for label, text in chunks if f"Topic: {topic}\n" in text]
        if mode == "irrelevant":
            selected = [(label, text) for label, text in chunks if f"Topic: {topic}\n" not in text][
                :1
            ]
        instructions = [re.search(r"Instruction: ([^\n]+)", text).group(1) for _, text in selected]
        value = {
            "answer": " ".join(instructions)
            if instructions
            else "Insufficient evidence for this question.",
            "citations": [label for label, _ in selected],
            "abstained": not bool(selected),
        }
        self.respond(
            {
                "model": body["model"],
                "done": True,
                "done_reason": "stop",
                "message": {"role": "assistant", "content": json.dumps(value)},
                "prompt_eval_count": 100,
                "eval_count": 40,
            }
        )


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", 11434), Handler).serve_forever()
