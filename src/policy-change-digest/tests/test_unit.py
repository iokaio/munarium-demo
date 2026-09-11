# SPDX-License-Identifier: Apache-2.0
import os
import subprocess
import sys

import pytest

from digest_demo.fixtures import generate, read, verify
from digest_demo.workflow import compare


def test_two_process_generator_reproducibility(tmp_path):
    for name in ("a", "b"):
        subprocess.run(
            [
                sys.executable,
                "-m",
                "digest_demo",
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
    assert a == b
    assert (tmp_path / "a-oracle/expected.json").read_bytes() == (
        tmp_path / "b-oracle/expected.json"
    ).read_bytes()
    from pathlib import Path

    (Path(os.environ["DIGEST_REPORT_DIR"]) / "reproducible-manifest.json").write_bytes(
        (tmp_path / "a/manifest.json").read_bytes()
    )


@pytest.mark.parametrize(
    "number,status",
    [
        (1, "candidate_impacts"),
        (6, "candidate_impacts"),
        (7, "unchanged"),
        (8, "insufficient_evidence"),
    ],
)
def test_explicit_revision_comparison(tmp_path, number, status):
    generate(tmp_path / "inputs", tmp_path / "oracle")
    result = compare(tmp_path / "inputs", f"case-{number:03}")
    assert result["status"] == status
    assert result["source_hashes"] and result["diff"].startswith("--- case-")
    assert read(tmp_path / "oracle/expected.json")[f"case-{number:03}"]["status"] == status


def test_source_tampering_is_rejected(tmp_path):
    generate(tmp_path / "inputs", tmp_path / "oracle")
    (tmp_path / "inputs/case-001/before.md").write_text("Changed source")
    with pytest.raises(ValueError, match="hash"):
        verify(tmp_path / "inputs")
