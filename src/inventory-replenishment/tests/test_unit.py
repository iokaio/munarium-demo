# SPDX-License-Identifier: Apache-2.0
import os
import subprocess
import sys
from pathlib import Path

import pytest

from inventory_demo.fixtures import CLOUD_CASES, generate, verify


def test_generator_two_independent_processes(tmp_path):
    for name in ("a", "b"):
        subprocess.run(
            [
                sys.executable,
                "-m",
                "inventory_demo",
                "generate",
                "--inputs",
                str(tmp_path / name),
                "--oracle",
                str(tmp_path / (name + "-oracle")),
                "--seed",
                str(tmp_path / (name + "-seed")),
                "--work",
                str(tmp_path / "work"),
            ],
            check=True,
        )
    for suffix in ("", "-oracle", "-seed"):

        def tree(name, suffix=suffix):
            root = tmp_path / (name + suffix)
            return {
                str(p.relative_to(root)): p.read_bytes() for p in root.rglob("*") if p.is_file()
            }

        assert tree("a") == tree("b")
    (Path(os.environ["INVENTORY_REPORT_DIR"]) / "reproducible-manifest.json").write_bytes(
        (tmp_path / "a/manifest.json").read_bytes()
    )


def test_tamper_rejected(tmp_path):
    generate(tmp_path / "inputs", tmp_path / "oracle")
    (tmp_path / "inputs/case-001/procedure.md").write_text("changed")
    with pytest.raises(ValueError, match="hash"):
        verify(tmp_path / "inputs")


def test_seed_and_oracle_are_private(tmp_path):
    generate(tmp_path / "inputs", tmp_path / "oracle")
    assert all(
        b"RESTRICTED_STOCK_SENTINEL" not in p.read_bytes()
        for p in (tmp_path / "inputs").rglob("*")
        if p.is_file()
    )
    assert b"RESTRICTED_STOCK_SENTINEL" in (tmp_path / "oracle/seed/seed.sql").read_bytes()


def test_online_assignment():
    assert [len(v) for v in CLOUD_CASES.values()] == [3, 3, 2]
    assert sorted(n for values in CLOUD_CASES.values() for n in values) == list(range(1, 9))
