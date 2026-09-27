# SPDX-License-Identifier: Apache-2.0
"""Canned wire fixture; reads only the evidence supplied in the prompt."""

import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

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
            self.respond({"models": [{"name": "inventory-fixture", "model": "inventory-fixture"}]})
        elif self.path == "/state":
            self.respond(STATE)
        else:
            self.respond({}, 404)

    def do_POST(self):
        size = int(self.headers.get("Content-Length", "0"))
        if not 0 < size < 1_000_000:
            self.respond({}, 400)
            return
        body = json.loads(self.rfile.read(size))
        if self.path == "/mode":
            STATE["mode"] = body["mode"]
            self.respond({})
            return
        STATE["calls"] += 1
        if STATE["mode"] == "unavailable":
            self.respond({"error": "Controlled provider outage"}, 503)
            return
        prompt = "\n".join(m["content"] for m in body["messages"])
        envelopes = [
            json.loads(line)
            for line in (line.removeprefix("Evidence: ") for line in prompt.splitlines())
            if line.startswith("{")
        ]
        procedure = next(
            item["citation_id"]
            for item in envelopes
            if item.get("source_role") == "document_hit" and item.get("layer") == "procedures"
        )
        actions = []
        for envelope in envelopes:
            if envelope.get("source_role") != "sealed_table":
                continue
            columns = envelope["content"]["columns"]
            for row in envelope["content"]["rows"]:
                cells = dict(zip(columns, row["cells"], strict=True))
                actions.append(
                    {
                        "sku": cells["sku"],
                        "constraint_code": cells["constraint_code"],
                        "citations": [
                            row["citation_id"],
                            procedure,
                        ],
                    }
                )
        if STATE["mode"] == "irrelevant" and actions:
            actions[0]["citations"][0] = "evidence/ev-unknown#missing"
        answer = {
            "actions": actions,
            "procedure_quote": "Obtain supplier confirmation before requesting replenishment.",
        }
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
