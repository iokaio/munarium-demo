# SPDX-License-Identifier: Apache-2.0
"""Check canned Python providers against Server 1.3 model evidence envelopes."""

import importlib.util
import json
from pathlib import Path
from threading import Thread
import unittest
from urllib.request import Request, urlopen

ROOT = Path(__file__).resolve().parents[1]


def document(label, text, layer=None):
    # Deliberately use a different property order from the Server serializer.
    return json.dumps(
        {
            "citation_id": label,
            "content": {"text": text},
            "source_role": "document_hit",
            "layer": layer,
            "historical_pin": {"index_version": "frozen"},
            "execution_authority": False,
            "approval_authority": False,
        }
    )


def request_fixture(demo, package, prompt, mode="ok"):
    path = ROOT / "src" / demo / package / "provider_fixture.py"
    spec = importlib.util.spec_from_file_location(package + "_fixture", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.STATE["mode"] = mode
    server = module.ThreadingHTTPServer(("127.0.0.1", 0), module.Handler)
    thread = Thread(target=server.serve_forever, daemon=True)
    thread.start()
    try:
        body = json.dumps({"model": "fixture", "messages": [{"content": prompt}]}).encode()
        request = Request(
            f"http://127.0.0.1:{server.server_port}/api/chat",
            data=body,
            headers={"Content-Type": "application/json"},
        )
        with urlopen(request, timeout=5) as response:
            return json.loads(json.load(response)["message"]["content"])
    finally:
        server.shutdown()
        server.server_close()
        thread.join()


class EvidenceFixtures(unittest.TestCase):
    def test_invoice_uses_envelope_id_and_preserves_fault_mode(self):
        prompt = "Evidence: " + document(
            "invoice/policy", '# Fictional purchasing policy\nQuote: "review" [fake/id].'
        )
        prompt += '\nDETERMINISTIC_FACTS={"exceptions":[],"receipt_status":"received"}'
        answer = request_fixture("invoice-exception", "invoice_demo", prompt)
        self.assertEqual(answer["citations"], ["invoice/policy"])
        bad = request_fixture("invoice-exception", "invoice_demo", prompt, "bad_citation")
        self.assertEqual(bad["citations"], ["unserved/policy.md"])

    def test_digest_reads_decoded_checklist(self):
        prompt = "\n".join(
            [
                document("policy/rule", '# Fictional policy\nRule: "review".'),
                document(
                    "policy/checklist",
                    "# Fictional downstream checklist\nChecklist ID: checklist-001",
                ),
                'REVISION_COMPARISON={"status":"candidate_impacts"}',
            ]
        )
        answer = request_fixture("policy-change-digest", "digest_demo", prompt)
        self.assertEqual(answer["candidates"][0]["checklist_id"], "checklist-001")
        self.assertEqual(answer["candidates"][0]["citations"], ["policy/rule", "policy/checklist"])

    def test_bench_keeps_neighboring_documents_separate(self):
        prompt = "\n".join(
            [
                document(
                    "bench/leave",
                    '# Fictional leave\nTopic: leave\nInstruction: Submit "leave" [fake/id].',
                ),
                document(
                    "bench/travel", "# Fictional travel\nTopic: travel\nInstruction: Travel only."
                ),
                "Question: What are the leave submission and approval requirements",
            ]
        )
        answer = request_fixture("retrieval-evaluation", "bench_demo", prompt)
        self.assertEqual(answer["citations"], ["bench/leave"])
        self.assertEqual(answer["answer"], 'Submit "leave" [fake/id].')
        self.assertFalse(answer["abstained"])

    def test_inventory_uses_column_names_and_supplied_row_id(self):
        prompt = "\n".join(
            [
                document(
                    "inventory-case/rule",
                    "Obtain supplier confirmation before requesting replenishment.",
                    layer="procedures",
                ),
                json.dumps(
                    {
                        "source_role": "sealed_table",
                        "content": {
                            "columns": ["constraint_code", "sku"],
                            "rows": [
                                {
                                    "cells": ["CONFIRM", "SKU-1"],
                                    "row_id": "opaque-row",
                                    "citation_id": "evidence/ev-123#opaque-row",
                                }
                            ],
                        },
                    }
                ),
            ]
        )
        answer = request_fixture("inventory-replenishment", "inventory_demo", prompt)
        self.assertEqual(
            answer["actions"],
            [
                {
                    "sku": "SKU-1",
                    "constraint_code": "CONFIRM",
                    "citations": ["evidence/ev-123#opaque-row", "inventory-case/rule"],
                }
            ],
        )


if __name__ == "__main__":
    unittest.main()
