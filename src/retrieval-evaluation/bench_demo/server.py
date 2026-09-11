# SPDX-License-Identifier: Apache-2.0
"""Trusted provisioning of named evaluation settings and separate identities."""

import base64
import os
import time
from pathlib import Path

import yaml
from munarium_client import ClientOptions, MunariumClient, MunariumError

from .fixtures import REVISION, SETTINGS, digest, encode, read, save, verify


def client(token, uid):
    return MunariumClient.rest(
        ClientOptions(
            os.environ.get("MUNARIUM_REST_URL", "http://server:8080"),
            token=token,
            uid=uid,
            read_retries=0,
        )
    )


def ready():
    for _ in range(60):
        try:
            with client("bench-rw", "bootstrap") as api:
                if api.server_version().version != "1.1.1":
                    raise ValueError("Server 1.1.1 required")
                return
        except MunariumError:
            time.sleep(1)
    raise RuntimeError("Server not ready")


def bootstrap(inputs, work, provider, model):
    manifest = verify(inputs)
    if provider != "fixture" and manifest["profile"] != "default":
        raise ValueError("Online qualification requires the default profile")
    ready()
    revision = digest((inputs / "manifest.json").read_bytes())
    config_hash = digest(
        Path("/app/runbooks/bench.yaml").read_bytes()
        + Path("/app/shapes/documents.yaml").read_bytes()
        + encode(SETTINGS)
        + model.encode()
    )[:8]
    name = f"bench-{revision[:12]}-{provider}-{config_hash}"
    provider_name = name + "-model"
    spec = {
        "provider": "ollama" if provider == "fixture" else provider,
        "models": {"complete": [model], "fast": model},
        "budgets": {"rpm": 60, "dailyTokens": {"fast": 150000}},
    }
    if provider == "fixture":
        spec["endpoint"] = "http://provider-fixture:11434"
    else:
        spec["credentialRef"] = {"env": provider.upper() + "_API_KEY"}
    report = {
        "client_revision": REVISION,
        "manifest": manifest,
        "provider": provider,
        "model": model,
        "runbooks": {},
        "settings": SETTINGS,
    }
    with client(os.environ["MUNARIUM_TOKEN"], "bench-bootstrap") as api:
        api.providers.apply_config(
            yaml.safe_dump(
                {
                    "apiVersion": "munarium.ioka.io/v1",
                    "kind": "ProviderConfig",
                    "metadata": {"name": provider_name},
                    "spec": spec,
                }
            )
        )
        if not api.providers.health(provider_name).healthy:
            raise RuntimeError("Provider health failed")
        api.runbooks.apply_shape(Path("/app/shapes/documents.yaml").read_text())
        collections = [
            {
                "name": name + "-" + scope,
                "shape": "bench-documents@1",
                "accessLevel": level,
                "sources": {"filenamePrefix": name + "/" + scope + "/"},
            }
            for scope, level in (("public", 0), ("restricted", 1))
        ]
        for setting, values in SETTINGS.items():
            runbook = yaml.safe_load(Path("/app/runbooks/bench.yaml").read_text())
            runbook["metadata"]["name"] = name + "-" + setting
            runbook["spec"]["collections"] = collections
            runbook["spec"]["retrieval"].update(
                topK=values["top_k"], candidateN=values["candidate_n"]
            )
            runbook["spec"]["completion"]["contextCharBudget"] = values["context_chars"]
            runbook["spec"]["models"]["tasks"]["completion"]["provider"] = provider_name
            runbook["spec"]["models"]["allowOverrides"] = [provider_name]
            api.runbooks.apply_runbook(yaml.safe_dump(runbook))
            report["runbooks"][setting] = runbook["metadata"]["name"] + "@1"
        for path in manifest["files"]:
            result = api.ingest.ingest(
                {
                    "filename": name + "/" + path,
                    "media_type": "text/plain",
                    "content_base64": base64.b64encode((inputs / path).read_bytes()).decode(),
                    "sha256": manifest["files"][path],
                }
            )
            if result.error or len(result.bound_to) != 1:
                raise RuntimeError("Unexpected ingestion binding")
        run = api.runbooks.run_runbook(report["runbooks"]["baseline"])
        report["index_run"] = run.run_id
        save(work / "bootstrap.json", report)
        remaining = {"cutover:" + c["name"] for c in collections}
        while remaining:
            status = api.runbooks.get_run(run.run_id)
            save(work / "index-run.json", status.model_dump(mode="json"))
            pending = [s for s in status.steps if s.state == "awaiting_approval"]
            if len(pending) != 1 or pending[0].name not in remaining:
                raise RuntimeError("Unexpected verified cutover")
            api.runbooks.approve_step(run.run_id, pending[0].ordinal)
            remaining.remove(pending[0].name)
        final = api.runbooks.get_run(run.run_id)
        if final.state != "done":
            raise RuntimeError("Index not activated")
        save(work / "index-run.json", final.model_dump(mode="json"))
    with client(os.environ["MUNARIUM_MGMT_TOKEN"], "bench-issuer") as issuer:
        for profile, level, target in (
            ("public", 0, "/credentials"),
            ("privileged", 1, "/privileged_credentials"),
        ):
            uid = "bench-" + profile
            grant = issuer.tokens.mint(
                uid=uid,
                access_level=level,
                scopes=["query"],
                runbook_refs=[r.rsplit("@", 1)[0] for r in report["runbooks"].values()],
                ttl_secs=3600,
            )
            save(
                Path(target) / "query.json",
                {
                    "token": grant.token,
                    "uid": uid,
                    "profile": profile,
                    "runbooks": report["runbooks"],
                    "settings": SETTINGS,
                    "fixture_revision": revision,
                    "provider": provider,
                    "model": model,
                    "provider_config": provider_name,
                    "namespace": name,
                },
            )
    return report


def query_client(credentials):
    config = read(Path(credentials) / "query.json")
    return client(config["token"], config["uid"]), config
