# SPDX-License-Identifier: Apache-2.0
"""Bootstrap owns operator credentials; the batch receives only a scoped capability."""

from __future__ import annotations

import base64
import json
import os
import time
from pathlib import Path

import yaml
from munarium_client import ClientOptions, MunariumClient, MunariumError

from .fixtures import digest, save, verify


def client(token: str, uid: str) -> MunariumClient:
    return MunariumClient.rest(
        ClientOptions(
            os.environ.get("MUNARIUM_REST_URL", "http://server:8080"), token=token, uid=uid
        )
    )


def ready() -> None:
    deadline = time.monotonic() + 120
    while True:
        try:
            with client("digest-rw", "bootstrap") as ops:
                version = ops.server_version()
                if (version.name, version.version) != ("munarium-server", "1.1.1"):
                    raise ValueError("This demo requires Munarium Server 1.1.1")
                return
        except ValueError:
            raise
        except MunariumError:
            if time.monotonic() > deadline:
                raise RuntimeError("Server did not become ready within 120 seconds") from None
            time.sleep(2)


def bootstrap(
    inputs: Path, credentials: Path, work: Path, provider: str, model: str, approve: bool
) -> dict:
    if provider not in ("fixture", "openai", "anthropic", "openrouter"):
        raise ValueError("Unsupported demo provider")
    manifest = verify(inputs)
    if provider != "fixture" and manifest["profile"] != "default":
        raise ValueError("Online qualification requires default fixtures")
    ready()
    revision = digest((inputs / "manifest.json").read_bytes())[:12]
    configuration_hash = digest(
        Path("/app/runbooks/digest.yaml").read_bytes()
        + Path("/app/shapes/documents.yaml").read_bytes()
        + model.encode()
    )[:8]
    namespace = f"digest-{revision}-{provider}-{configuration_hash}"
    config_name = namespace + "-model"
    spec = {
        "provider": "ollama" if provider == "fixture" else provider,
        "models": {"complete": [model], "fast": model},
        "budgets": {
            "rpm": max(60, len(manifest["cases"]) * 3) if provider == "fixture" else 60,
            "dailyTokens": {"fast": 150000},
        },
    }
    if provider == "fixture":
        spec["endpoint"] = "http://provider-fixture:11434"
    else:
        spec["credentialRef"] = {"env": provider.upper() + "_API_KEY"}
    report = {
        "server_version": "1.1.1",
        "fixture_manifest": manifest,
        "provider": provider,
        "model": model,
        "namespace": namespace,
        "client_revision": "bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3",
        "runs": {},
        "runbooks": {},
        "revision_runbooks": {},
    }
    with client(os.environ["MUNARIUM_TOKEN"], "digest-bootstrap") as ops:
        ops.providers.apply_config(
            yaml.safe_dump(
                {
                    "apiVersion": "munarium.ioka.io/v1",
                    "kind": "ProviderConfig",
                    "metadata": {"name": config_name},
                    "spec": spec,
                }
            )
        )
        # Health checks model availability without spending completion tokens.
        if not ops.providers.health(config_name).healthy:
            raise RuntimeError(
                "Named provider health failed; check selected model and Server credentials"
            )
        ops.runbooks.apply_shape(Path("/app/shapes/documents.yaml").read_text())
        for folder in sorted(inputs.glob("case-*")):
            name = namespace + "-" + folder.name
            prefix = name + "/"
            runbook = yaml.safe_load(Path("/app/runbooks/digest.yaml").read_text())
            runbook["metadata"]["name"] = name
            collections = [
                {
                    "name": name + "-r1",
                    "shape": "digest-documents@1",
                    "accessLevel": 0,
                    "sources": {"filenamePrefix": prefix + "before.md"},
                },
                {
                    "name": name + "-r2",
                    "shape": "digest-documents@1",
                    "accessLevel": 0,
                    "sources": {
                        "filenamePrefix": prefix
                        + ("after.md" if (folder / "after.md").exists() else "missing.md")
                    },
                },
                {
                    "name": name + "-checklists",
                    "shape": "digest-documents@1",
                    "accessLevel": 0,
                    "sources": {"filenamePrefix": prefix + "downstream/"},
                },
            ]
            runbook["spec"]["collections"] = collections
            runbook["spec"]["models"]["tasks"]["completion"]["provider"] = config_name
            ops.runbooks.apply_runbook(yaml.safe_dump(runbook))
            for index, revision_name in enumerate(("r1", "r2")):
                view = yaml.safe_load(yaml.safe_dump(runbook))
                view["metadata"]["name"] = name + "-" + revision_name
                view["spec"]["collections"] = [collections[index]]
                ops.runbooks.apply_runbook(yaml.safe_dump(view))
            report["revision_runbooks"][folder.name] = {
                revision_name: name + "-" + revision_name + "@1" for revision_name in ("r1", "r2")
            }
            for path in sorted(folder.iterdir()):
                raw = path.read_bytes()
                # Immutable policy revisions and checklist text remain separate evidence.
                result = ops.ingest.ingest(
                    {
                        "filename": prefix
                        + ("downstream/" if path.name in ("checklist.md", "unrelated.md") else "")
                        + path.name,
                        "media_type": "text/plain",
                        "content_base64": base64.b64encode(raw).decode(),
                    }
                )
                if (
                    result.error
                    or len(result.bound_to) != 1
                    or result.bound_to[0] not in {c["name"] for c in collections}
                ):
                    raise RuntimeError("Ingestion failed or bound to an unexpected collection")
            run = ops.runbooks.run_runbook(name)
            report["runs"][folder.name] = run.run_id
            report["runbooks"][folder.name] = name + "@1"
            save(work / "bootstrap.json", report)
            remaining = {"cutover:" + c["name"] for c in collections}
            while remaining:
                status = ops.runbooks.get_run(run.run_id)
                save(work / (folder.name + "-index-run.json"), status.model_dump(mode="json"))
                pending = [step for step in status.steps if step.state == "awaiting_approval"]
                if len(pending) != 1 or pending[0].name not in remaining:
                    raise RuntimeError("Unexpected pending collection cutover")
                if not approve:
                    raise RuntimeError(
                        "Index is awaiting trusted bootstrap approval; run IDs saved"
                    )
                ops.runbooks.approve_step(run.run_id, pending[0].ordinal)
                remaining.remove(pending[0].name)
            if ops.runbooks.get_run(run.run_id).state != "done":
                raise RuntimeError("Approved index run did not complete")
            save(
                work / (folder.name + "-index-run.json"),
                ops.runbooks.get_run(run.run_id).model_dump(mode="json"),
            )
    with client(os.environ["MUNARIUM_MGMT_TOKEN"], "digest-issuer") as issuer:
        grant = issuer.tokens.mint(
            uid="digest-reviewer",
            scopes=["query"],
            # Server 1.1.1 checks the metadata name; sessions still pin name@version.
            runbook_refs=[
                ref.rsplit("@", 1)[0]
                for ref in list(report["runbooks"].values())
                + [ref for pair in report["revision_runbooks"].values() for ref in pair.values()]
            ],
            ttl_secs=3600,
        )
    save(
        credentials / "query.json",
        {
            "token": grant.token,
            "uid": "digest-reviewer",
            "runbooks": report["runbooks"],
            "revision_runbooks": report["revision_runbooks"],
            "fixture_revision": revision,
            "provider": provider,
            "model": model,
            "namespace": namespace,
        },
    )
    return report


def query_client(credentials: Path) -> tuple[MunariumClient, dict]:
    config = json.loads((credentials / "query.json").read_text())
    return client(config["token"], config["uid"]), config
