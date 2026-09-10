# SPDX-License-Identifier: Apache-2.0
"""Bootstrap public images and bundled data. Python 3.11+ and tools/requirements.txt."""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import time
import urllib.error
import urllib.request
import yaml

ROOT = Path(__file__).resolve().parents[1]
RUNBOOKS = {"history": "history-revolution", "support": "support-knowledge", "dd": "due-diligence",
            "fin": "financial-advisory", "patents": "patent-analysis", "intel": "threat-intelligence"}


def settings() -> dict[str, str]:
    result = {}
    for line in (ROOT / ".env").read_text(encoding="utf-8").splitlines():
        if line.strip() and not line.lstrip().startswith("#"):
            key, value = line.split("=", 1)
            result[key.strip()] = value.strip()
    return result | dict(os.environ)


def command(*args: str, env: dict[str, str] | None = None) -> None:
    subprocess.run(args, cwd=ROOT, env=env, check=True)


def api(base: str, token: str, method: str, path: str, body: bytes | None = None,
        content_type: str = "application/json") -> dict:
    request = urllib.request.Request(base + path, data=body, method=method,
        headers={"Authorization": "Bearer " + token, "X-Munarium-Uid": "demo-bootstrap",
                 "Content-Type": content_type})
    try:
        with urllib.request.urlopen(request, timeout=590) as response:
            raw = response.read()
            if path == "/readyz" or not raw:
                return {}
            return json.loads(raw)
    except urllib.error.HTTPError as error:
        raise RuntimeError(f"{method} {path}: HTTP {error.code}") from None


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("action", choices=["init", "start", "load", "verify"])
    parser.add_argument("--provider", choices=["ollama", "anthropic", "openai", "openrouter"], default="ollama")
    parser.add_argument("--corpus", choices=["all", *RUNBOOKS], default="all")
    parser.add_argument("--approve", action="store_true", help="approve this command's index cutovers")
    args = parser.parse_args()
    if args.action == "init":
        path = ROOT / ".env"
        if path.exists():
            print(".env already exists; credentials retained.")
            return
        template = (ROOT / ".env.example").read_text(encoding="utf-8")
        for key in ["POSTGRES_PASSWORD", "MUNARIUM_API_TOKEN", "MUNARIUM_MGMT_TOKEN",
                    "MUNARIUM_TOKEN_SECRET", "DEMO_GATE_SECRET", "DEMO_ADMIN_PASSWORD"]:
            template = template.replace(key + "=\n", key + "=" + secrets.token_hex(32) + "\n")
        with path.open("x", encoding="utf-8") as stream:
            stream.write(template)
        path.chmod(0o600)
        print("Created private .env. No secrets printed.")
        return
    config = settings()
    base = config.get("MUNARIUM_BASE_URL", "http://127.0.0.1:" + config.get("SERVER_HOST_PORT", "8080")).rstrip("/")
    token = config["MUNARIUM_API_TOKEN"]
    selected = list(RUNBOOKS) if args.corpus == "all" else [args.corpus]
    if args.action == "start":
        profile = ("--profile", "local-model") if args.provider == "ollama" else ()
        command("docker", "compose", *profile, "up", "-d", "--build", env=config)
        if args.provider == "ollama":
            for model in ("qwen3:1.7b", "all-minilm:22m"):
                command("docker", "compose", "exec", "-T", "ollama", "ollama", "pull", model, env=config)
        print("Next: python tools/setup.py load --provider " + args.provider + " --approve")
        return
    for attempt in range(120):
        try:
            api(base, token, "GET", "/readyz")
            break
        except (RuntimeError, urllib.error.URLError):
            if attempt == 119:
                raise RuntimeError("Server did not become ready") from None
            time.sleep(2)
    if args.action == "verify":
        inventory = {c["id"]: c for c in json.loads((ROOT / "data/manifest.json").read_text())["corpora"]}
        for corpus in selected:
            result = api(base, token, "GET", "/v1/runbooks/" + RUNBOOKS[corpus])
            if result.get("status") != "active":
                raise RuntimeError("Runbook is not active: " + RUNBOOKS[corpus])
            collections = result.get("collections", [])
            expected = inventory[corpus]
            if len(collections) != expected["collection_prefix_count"] or any(
                    not c.get("active_index") or not c.get("source_count") for c in collections):
                raise RuntimeError("Missing populated active collection: " + RUNBOOKS[corpus])
            count = sum(c["source_count"] for c in collections)
            if count != expected["document_count"]:
                raise RuntimeError("Source count differs from bundled corpus: " + RUNBOOKS[corpus])
            print(RUNBOOKS[corpus], "active", len(collections), "collections", count, "sources")
        return
    provider = ROOT / "vendor/providers" / ("demo-" + args.provider + ".yaml")
    api(base, token, "POST", "/v1/providers", provider.read_bytes(), "application/yaml")
    for path in (ROOT / "vendor/runbooks").glob("*.yaml"):
        if yaml.safe_load(path.read_text(encoding="utf-8"))["kind"] == "Shape":
            api(base, token, "POST", "/v1/shapes", path.read_bytes(), "application/yaml")
    env = config | {"MUNARIUM_BASE_URL": base, "MUNARIUM_TOKEN": token, "MUNARIUM_UID": "demo-bootstrap"}
    progress_dir = ROOT / ".local"
    progress_dir.mkdir(exist_ok=True)
    for corpus in selected:
        runbook = RUNBOOKS[corpus]
        path = ROOT / "vendor/runbooks" / (runbook + ".yaml")
        document = yaml.safe_load(path.read_text(encoding="utf-8"))
        def select_provider(value):
            if isinstance(value, dict):
                return {k: "demo-" + args.provider if k == "provider" and v == "default"
                        else select_provider(v) for k, v in value.items()}
            if isinstance(value, list):
                return [select_provider(v) for v in value]
            return value
        payload = yaml.safe_dump(select_provider(document), sort_keys=False).encode()
        validation = api(base, token, "POST", "/v1/runbooks/validate?suggest=false", payload, "application/yaml")
        if not validation.get("valid"):
            raise RuntimeError("Runbook validation failed: " + runbook)
        api(base, token, "POST", "/v1/runbooks", payload, "application/yaml")
        command(sys.executable, "tools/corpora/unpack.py", "--corpus", corpus)
        for part in (["history", "history-newspapers"] if corpus == "history" else [corpus]):
            command(sys.executable, "tools/corpora/loader.py", "--corpus", part,
                    "--log", str(progress_dir / (part + ".jsonl")), env=env)
        state_file = progress_dir / (runbook + "-run.json")
        if state_file.exists():
            state = json.loads(state_file.read_text())
            if state["base"] != base:
                raise RuntimeError("Saved run belongs to another server; use a separate checkout.")
            run_id = state["run_id"]
        else:
            run = api(base, token, "POST", "/v1/runbooks/" + runbook + "/runs")
            run_id = run["run_id"]
            state_file.write_text(json.dumps({"base": base, "run_id": run_id}), encoding="utf-8")
        deadline = time.monotonic() + 7200
        while True:
            run = api(base, token, "GET", "/v1/runs/" + run_id)
            if run["state"] == "done":
                print(runbook, "indexed and active", flush=True)
                break
            if run["state"] == "failed" or time.monotonic() > deadline:
                raise RuntimeError("Index run failed or timed out: " + run_id)
            if run["state"] == "awaiting_approval":
                step = next(s for s in run["steps"] if s["state"] == "awaiting_approval")
                if not args.approve:
                    print("Review this run, then rerun with --approve:", run_id)
                    return
                print(runbook, "approve", step["ordinal"], flush=True)
                api(base, token, "POST", f"/v1/runs/{run_id}/steps/{step['ordinal']}/approve")
            else:
                time.sleep(2)


if __name__ == "__main__":
    main()
