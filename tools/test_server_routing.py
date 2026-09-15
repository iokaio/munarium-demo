# SPDX-License-Identifier: Apache-2.0
"""Run the pinned SDK's nine controlled routing checks against Server 1.2.1.

The upstream test_server_111 module deliberately requires its historical release.
Reuse its tests and transport fixture unchanged; only the local setup targets the
current demo release. Keep the official SDK checkout immutable.
"""

from __future__ import annotations

import base64
import importlib.util
from collections.abc import Iterator
from uuid import uuid4

import pytest
from munarium_client import ClientOptions, MunariumClient

_spec = importlib.util.spec_from_file_location(
    "sdk_historical_routing",
    "/opt/munarium/clients/python/conformance/test_server_111.py",
)
assert _spec is not None and _spec.loader is not None
_sdk = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(_sdk)
REST, TOKEN, ENDPOINT = _sdk.REST, _sdk.TOKEN, _sdk.ENDPOINT
variant = _sdk.variant
test_named_ollama = _sdk.test_named_ollama
test_override_routing = _sdk.test_override_routing
test_stream_exposes_actual_models = _sdk.test_stream_exposes_actual_models


@pytest.fixture(scope="module")
def routing() -> Iterator[dict[str, str]]:
    with MunariumClient.rest(ClientOptions(REST, token=TOKEN, uid="qualification")) as ops:
        version = ops.server_version()
        assert (version.name, version.version) == ("munarium-server", "1.2.1")
        prefix = "client121-" + uuid4().hex[:12]
        names = {
            key: prefix + "-" + key for key in ("baseline", "selected", "shape", "docs", "runbook")
        }
        for family in ("baseline", "selected"):
            ops.providers.apply_config(f"""apiVersion: munarium.ioka.io/v1
kind: ProviderConfig
metadata: {{ name: {names[family]} }}
spec:
  provider: ollama
  endpoint: {ENDPOINT}
  models:
    complete: [{family}-fast, {family}-capable, explicit]
    embed: [embed]
    fast: {family}-fast
    capable: {family}-capable
""")
        ops.runbooks.apply_shape(f"""apiVersion: munarium.ioka.io/v1
kind: Shape
metadata: {{ name: {names["shape"]}, version: 1 }}
spec:
  fact:
    schema: {{ type: object }}
""")
        ops.runbooks.apply_runbook(f"""apiVersion: munarium.ioka.io/v1
kind: Runbook
metadata: {{ name: {names["runbook"]}, version: 1 }}
spec:
  collections:
    - name: {names["docs"]}
      shape: {names["shape"]}@1
      sources: {{ filenamePrefix: "{prefix}/" }}
  retrieval:
    modelQueryExpansion: {{ maxTerms: 4, maxTokens: 64, required: false }}
  models:
    allowOverrides: [{names["selected"]}]
    tasks:
      query_expansion: {{ provider: {names["baseline"]}, tier: fast }}
      completion: {{ provider: {names["baseline"]}, tier: capable }}
  completion: {{ promptTemplate: "{{query}}\\n{{context}}", maxTokens: 256 }}
  steps:
    - resolveSources: {{}}
    - buildIndex: {{}}
    - verify: {{}}
    - cutover: {{ approval: required }}
""")
        ops.ingest.ingest(
            {
                "filename": prefix + "/procedure.md",
                "media_type": "text/markdown",
                "content_base64": base64.b64encode(
                    b"The journey procedure requires a review."
                ).decode(),
            }
        )
        run = ops.runbooks.run_runbook(names["runbook"])
        status = ops.runbooks.get_run(run.run_id)
        step = next(s for s in status.steps if s.state == "awaiting_approval")
        ops.runbooks.approve_step(run.run_id, step.ordinal)
        assert ops.runbooks.get_run(run.run_id).state == "done"
        inventory = {p.name: p for p in ops.providers.list()}
        assert inventory[names["selected"]].provider == "ollama"
        assert inventory[names["selected"]].credential_ok
        yield names
