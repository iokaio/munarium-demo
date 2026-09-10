# SPDX-License-Identifier: Apache-2.0
"""Smoke-test a loaded demo's real pages, sources, scopes, and optional completions.

These are functional checks, not a reproduction of historical answer-quality scores.
Supply DEMO_VERIFY_COOKIE privately when testing an installation with its gate enabled.
"""
import argparse
import html
import json
import os
from pathlib import Path
import re
import time
import urllib.error
import urllib.request
import yaml

ROOT = Path(__file__).resolve().parents[1]
PAGES = {"revolution": "Revolution", "support": "Support", "dataroom": "Dataroom",
         "advisory": "Advisory", "patents": "Patents", "intel": "Intel"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-url", default="http://127.0.0.1:5310")
    parser.add_argument("--corpus", choices=["all", *PAGES], default="all")
    parser.add_argument("--allow-model-calls", action="store_true", help="enable searches and answers, including configured model query expansion")
    parser.add_argument("--family", choices=["ollama", "claude", "gpt", "openrouter"], default="ollama")
    parser.add_argument("--output", type=Path, default=ROOT / ".local/acceptance.json")
    args = parser.parse_args()
    config = json.loads((ROOT / "src/Demo.Web/appsettings.json").read_text())["Corpora"]
    checks = []

    def request(path, body=None, *, document=False):
        headers = {"Content-Type": "application/json"}
        if os.environ.get("DEMO_VERIFY_COOKIE"):
            headers["Cookie"] = os.environ["DEMO_VERIFY_COOKIE"]
        req = urllib.request.Request(args.base_url.rstrip("/") + path,
            data=json.dumps(body).encode() if body is not None else None, headers=headers)
        try:
            with urllib.request.urlopen(req, timeout=180) as response:
                raw = response.read().decode()
                if document:
                    assert "/gate" not in response.url, "Visitor authentication required"
                    return raw
                return json.loads(raw)
        except urllib.error.HTTPError as error:
            raise RuntimeError(f"{path}: HTTP {error.code}") from None

    assert request("/readyz")["ok"]
    for corpus, page in PAGES.items():
        if args.corpus not in ("all", corpus):
            continue
        assert "data-dm" in request("/" + corpus, document=True)
        source = (ROOT / "src/Demo.Web/Pages" / (page + ".cshtml")).read_text(encoding="utf-8")
        question = html.unescape(re.search(r"data-dm-chip>(.*?)</button>", source, re.S).group(1)).strip()
        checks.append({"corpus": corpus, "check": "page"})
        if args.allow_model_calls:
            found = request("/api/search/" + corpus, {"query": question})
            assert found.get("hits") and all(h.get("docId") for h in found["hits"]), corpus + ": no source hits"
            checks.append({"corpus": corpus, "check": "search", "hits": len(found["hits"])})
            answer = request("/api/chat/" + corpus, {"message": question, "family": args.family, "tier": "fast"})
            assert answer.get("answer") and answer.get("hits") and answer.get("model") and answer.get("provider"), corpus + ": completion or evidence missing"
            assert not answer["answer"].startswith("(The model returned no answer"), corpus + ": model exhausted its answer budget"
            checks.append({"corpus": corpus, "check": "grounded-completion", "family": args.family,
                           "model": answer.get("model"), "hits": len(answer["hits"])})
        if corpus == "dataroom":
            runbook = yaml.safe_load((ROOT / "vendor/runbooks/due-diligence.yaml").read_text(encoding="utf-8"))
            for persona, policy in config[corpus]["Personas"].items():
                allowed = {c["name"] for c in runbook["spec"]["collections"]
                           if c["accessLevel"] <= policy["AccessLevel"]
                           and set(c.get("compartments", [])) <= set(policy["Compartments"])}
                session = request("/api/session/dataroom", {"persona": persona})
                assert set(session["permittedCollections"]) == allowed, persona + ": session scope mismatch"
                if args.allow_model_calls:
                    hits = request("/api/search/dataroom", {"query": "security incident privacy", "persona": persona})
                    assert set(hits["collectionsSearched"]) <= allowed, persona + ": search scope escaped"
                    assert all(h["collection"] in allowed for h in hits["hits"]), persona + ": restricted source returned"
                    answer = request("/api/chat/dataroom", {"message": "Summarize the privacy and security records you can access.",
                        "persona": persona, "family": args.family, "tier": "fast"})
                    assert all(h["collection"] in allowed for h in answer.get("hits", [])), persona + ": chat scope escaped"
                checks.append({"corpus": corpus, "check": "persona-scope", "persona": persona, "collections": len(allowed)})
        print("PASS", corpus, flush=True)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"status": "passed", "modelCalls": args.allow_model_calls,
        "checks": checks, "completedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}, indent=2) + "\n")
    print("Functional acceptance passed; evidence:", args.output)


if __name__ == "__main__":
    main()
