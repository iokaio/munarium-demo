# SPDX-License-Identifier: Apache-2.0
import os
import subprocess
import sys
from pathlib import Path

import pytest

from bench_demo.fixtures import SETTINGS, generate, read, verify
from bench_demo.metrics import score


def test_independent_generator_processes(tmp_path):
    for name in ("a", "b"):
        subprocess.run(
            [
                sys.executable,
                "-m",
                "bench_demo",
                "generate",
                "--inputs",
                str(tmp_path / name),
                "--oracle",
                str(tmp_path / (name + "-oracle")),
                "--work",
                str(tmp_path / "work"),
            ],
            check=True,
        )
    a = {
        str(p.relative_to(tmp_path / "a")): p.read_bytes()
        for p in (tmp_path / "a").rglob("*")
        if p.is_file()
    }
    b = {
        str(p.relative_to(tmp_path / "b")): p.read_bytes()
        for p in (tmp_path / "b").rglob("*")
        if p.is_file()
    }
    assert a == b and read(tmp_path / "a-oracle/labels.json") == read(
        tmp_path / "b-oracle/labels.json"
    )
    (Path(os.environ["BENCH_REPORT_DIR"]) / "reproducible-manifest.json").write_bytes(
        (tmp_path / "a/manifest.json").read_bytes()
    )


@pytest.mark.parametrize("setting", ["topk", "candidates", "budget"])
def test_one_setting_changes(setting):
    assert sum(SETTINGS[setting][k] != value for k, value in SETTINGS["baseline"].items()) == 1


def test_irrelevant_resolved_citation_is_not_grounding():
    labels = {
        "cases": {
            "case": {"sources": ["correct.md"], "abstain": False, "required_terms": ["required"]}
        },
        "restricted_marker": "canary",
    }
    record = {
        "identity": {"case": "case"},
        "namespace": "ns",
        "profile": "public",
        "setting": "topk",
        "elapsed_seconds": 0.1,
        "answer": {
            "answer": "A fluent but irrelevant explanation.",
            "citations": ["col/chunk"],
            "abstained": False,
        },
        "result": {
            "hits": [{"collection": "col", "chunk_id": "chunk", "source_path": "ns/wrong.md"}],
            "completion": {
                "input_tokens": 10,
                "output_tokens": 5,
                "provider": "fixture",
                "model": "fixture",
            },
        },
    }
    result = score(record, labels)
    assert result["citation_resolution"] == 1 and result["citation_relevance"] == 0
    assert result["source_recall"] == 0 and not result["required_terms_present"]


def test_manifest_detects_tampering(tmp_path):
    generate(tmp_path / "inputs", tmp_path / "oracle")
    (tmp_path / "inputs/public/distractor.md").write_text("Changed")
    with pytest.raises(ValueError, match="hash"):
        verify(tmp_path / "inputs")
