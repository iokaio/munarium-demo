# SPDX-License-Identifier: Apache-2.0
"""Operator-only Matrix registration and Server runbook bootstrap."""

import base64
import dataclasses
import os
import time
from pathlib import Path

import yaml
from munarium_client import ClientOptions, MunariumClient, MunariumError
from munarium_matrix import MatrixClient, MatrixError

from .fixtures import REV, digest, read, save, verify


def client(token, uid):
    return MunariumClient.rest(
        ClientOptions(
            os.environ.get("MUNARIUM_REST_URL", "http://server:8080"), token=token, uid=uid
        )
    )


def matrix():
    return MatrixClient("http://matrix:8180", token="inventory-matrix-rw")


def ready_server():
    deadline = time.monotonic() + 120
    while True:
        try:
            with client("inventory-rw", "bootstrap") as api:
                if api.server_version().version != "1.1.1":
                    raise ValueError("Expected Server 1.1.1")
                return
        except MunariumError:
            if time.monotonic() > deadline:
                raise RuntimeError("Server readiness timed out") from None
            time.sleep(1)


def ready():
    deadline = time.monotonic() + 120
    while True:
        try:
            with client("inventory-rw", "bootstrap") as api, matrix() as mx:
                if api.server_version().version != "1.1.1":
                    raise ValueError("Expected Server 1.1.1")
                if not mx.version().lockstep_ok:
                    raise ValueError("Matrix must report exact Server compatibility")
                return
        except (MunariumError, MatrixError):
            if time.monotonic() > deadline:
                raise RuntimeError("Server/Matrix readiness timed out") from None
            time.sleep(2)


def bootstrap(inputs, credentials, work, provider, model):
    inputs, credentials, work = Path(inputs), Path(credentials), Path(work)
    manifest = verify(inputs)
    if provider != "fixture" and manifest["profile"] != "default":
        raise ValueError("Online qualification requires the default profile")
    ready()
    revision = digest((inputs / "manifest.json").read_bytes())[:12]
    namespace = (
        "inventory-"
        + digest(
            (inputs / "manifest.json").read_bytes()
            + Path("/app/runbooks/brief.yaml").read_bytes()
            + Path("/app/matrix/contract.yaml").read_bytes()
            + model.encode()
            + provider.encode()
            + str(work).encode()
        )[:12]
    )
    report = {
        "client_revision": REV,
        "fixture_manifest": manifest,
        "namespace": namespace,
        "runbooks": {},
        "variants": {},
        "provider": provider,
        "model": model,
        "fixture_revision": revision,
        "contracts": {
            "normal": "below-threshold@2",
            "limited": "below-threshold-limited@2",
            "stale": namespace + "-stale@2",
            "empty": "below-threshold@2",
            "expiry": namespace + "-expiry@2",
        },
    }
    with matrix() as mx:
        report["matrix_version"] = dataclasses.asdict(mx.version())
        mx.apply(Path("/app/matrix/datasource.yaml").read_text())
        report["introspection"] = mx.introspect("inventory")
        contract = yaml.safe_load(Path("/app/matrix/contract.yaml").read_text())
        mx.apply(yaml.safe_dump(contract))
        verified = mx.verify("below-threshold")
        report["contract_verification"] = dataclasses.asdict(verified)
        save(work / "matrix.json", report)
        if verified.failed or verified.passed != 1:
            raise RuntimeError("Matrix contract questions must actually pass")
        contract["metadata"]["name"] = "below-threshold-limited"
        contract["spec"]["limits"]["maxRows"] = 1
        contract["spec"]["verifiedQuestions"][0]["expect"]["rows"] = 1
        mx.apply(yaml.safe_dump(contract))
        limited = mx.verify("below-threshold-limited")
        save(work / "limited-verification.json", dataclasses.asdict(limited))
        if limited.failed:
            raise RuntimeError("Limited contract verification failed")
        for variant in ("stale", "expiry"):
            contract = yaml.safe_load(Path("/app/matrix/contract.yaml").read_text())
            contract["metadata"]["name"] = namespace + "-" + variant
            # Server deduplicates equal logical results across contracts. Give
            # disposable scenarios distinct fixture row keys so purging one
            # cannot delete a business case's shared artifact or provenance.
            sql = contract["spec"]["statementByDialect"]["postgres"]["inline"]
            contract["spec"]["statementByDialect"]["postgres"]["inline"] = sql.replace(
                "SELECT sku,", "SELECT sku || '-" + namespace + "-" + variant + "' AS sku,"
            )
            if variant == "stale":
                contract["spec"]["verifiedQuestions"][0]["parameters"]["as_of"] = "2026-09-20"
            mx.apply(yaml.safe_dump(contract))
            if mx.verify(namespace + "-" + variant).failed:
                raise RuntimeError("Disposable scenario contract verification failed")
    config_name = namespace + "-model"
    spec = {
        "provider": "ollama" if provider == "fixture" else provider,
        "models": {"complete": [model], "fast": model},
        "budgets": {"rpm": 60, "dailyTokens": {"fast": 150000}},
    }
    if provider == "fixture":
        spec["endpoint"] = "http://provider-fixture:11434"
    else:
        spec["credentialRef"] = {"env": provider.upper() + "_API_KEY"}
    with client(os.environ["MUNARIUM_TOKEN"], "inventory-bootstrap") as ops:
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
        if not ops.providers.health(config_name).healthy:
            raise RuntimeError("Provider health failed")
        ops.runbooks.apply_shape(Path("/app/shapes/documents.yaml").read_text())
        for case in list(manifest["cases"]) + ["limited", "stale", "empty", "expiry", "privileged"]:
            base = case if case.startswith("case-") else "case-001"
            name = namespace + "-" + case
            runbook = yaml.safe_load(Path("/app/runbooks/brief.yaml").read_text())
            runbook["metadata"]["name"] = name
            level = 2 if case == "privileged" else 0
            runbook["spec"]["collections"] = [
                {
                    "name": name,
                    "shape": "inventory-documents@1",
                    "accessLevel": level,
                    "sources": {"filenamePrefix": name + "/"},
                }
            ]
            runbook["spec"]["models"]["tasks"]["completion"]["provider"] = config_name
            runbook["spec"]["retrieval"]["researchProfiles"][0]["layers"][1]["sources"] = [name]
            runbook["spec"]["dataViews"] = [
                {
                    "name": "below-threshold",
                    "contract": report["contracts"].get(case, report["contracts"]["normal"]),
                    "accessLevel": level,
                    "parameters": {
                        "warehouse": {"type": "string", "value": base},
                        "as_of": {
                            "type": "date",
                            "value": "2026-09-20"
                            if case == "stale"
                            else "2026-09-10"
                            if case == "empty"
                            else "2026-09-11",
                        },
                    },
                }
            ]
            ops.runbooks.apply_runbook(yaml.safe_dump(runbook))
            raw = (inputs / base / "procedure.md").read_bytes()
            result = ops.ingest.ingest(
                {
                    "filename": name + "/procedure.md",
                    "media_type": "text/plain",
                    "content_base64": base64.b64encode(raw).decode(),
                    "sha256": digest(raw),
                }
            )
            if result.error or result.bound_to != [name]:
                raise RuntimeError("Procedure ingestion binding mismatch")
            run = ops.runbooks.run_runbook(name)
            state = ops.runbooks.get_run(run.run_id)
            save(work / (case + "-index.json"), state.model_dump(mode="json"))
            pending = [s for s in state.steps if s.state == "awaiting_approval"]
            if len(pending) != 1 or pending[0].name != "cutover:" + name:
                raise RuntimeError("Unexpected build or data-view verification failure")
            ops.runbooks.approve_step(run.run_id, pending[0].ordinal)
            if ops.runbooks.get_run(run.run_id).state != "done":
                raise RuntimeError("Cutover did not complete")
            report["runbooks" if case.startswith("case-") else "variants"][case] = name + "@1"
    with client(os.environ["MUNARIUM_MGMT_TOKEN"], "inventory-issuer") as issuer:
        refs = list(report["runbooks"].values()) + [
            ref for key, ref in report["variants"].items() if key != "privileged"
        ]
        grant = issuer.tokens.mint(
            uid="inventory-planner",
            scopes=["query", "evidence"],
            runbook_refs=[ref.rsplit("@", 1)[0] for ref in refs],
            ttl_secs=3600,
        )
    save(credentials / "query.json", {**report, "token": grant.token, "uid": "inventory-planner"})
    save(work / "bootstrap.json", report)


def query_client(credentials):
    config = read(Path(credentials) / "query.json")
    return client(config["token"], config["uid"]), config
