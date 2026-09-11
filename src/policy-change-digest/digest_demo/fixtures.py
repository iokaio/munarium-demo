# SPDX-License-Identifier: Apache-2.0
"""Seeded evidence generation; the private oracle is never ingested."""

import hashlib
import json
import os
from pathlib import Path
from uuid import uuid4

REVISION = "bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3"


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def encode(value):
    return (json.dumps(value, sort_keys=True, ensure_ascii=False, indent=2) + "\n").encode()


def write(path, raw):
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name("." + uuid4().hex + ".tmp")
    with temporary.open("wb") as stream:
        stream.write(raw)
        stream.flush()
        os.fsync(stream.fileno())
    temporary.replace(path)


def save(path, value):
    write(path, encode(value))


def read(path):
    return json.loads(Path(path).read_text())


def profile_settings(profile):
    return read(Path(__file__).resolve().parents[1] / "fixture-profiles.json")[profile]


def generate(inputs, oracle, profile="default"):
    inputs, oracle = Path(inputs), Path(oracle)
    settings = profile_settings(profile)
    seed, count = settings["seed"], settings["cases"]
    if (inputs / "manifest.json").exists():
        prior = read(inputs / "manifest.json")
        if prior.get("profile") != profile or prior["seed"] != seed:
            raise ValueError("Use empty state for a different profile")
    templates = [
        ("purchasing approval", "500 credits", "750 credits"),
        ("travel receipt", "50 credits", "35 credits"),
        ("equipment renewal", "36 months", "48 months"),
        ("training booking", "10 days", "15 days"),
        ("visitor review", "2 days", "3 days"),
        ("équipe office closing", "16:00 UTC", "17:00 UTC"),
        ("support notification", "2 hours", "2 hours"),
        ("archive review", "90 days", None),
    ]
    files, cases, expected = {}, {}, {}
    for number in range(1, count + 1):
        topic, old, new = templates[(number - 1) % len(templates)]
        if profile != "default":

            def shifted(rule):
                if rule is None:
                    return None
                if rule.endswith(" UTC"):
                    return str(int(rule[:2]) + (2 if profile == "heldout" else 4)) + rule[2:]
                quantity, unit = rule.split(" ", 1)
                return f"{int(quantity) + seed % 11 + 1} {unit}"

            old, new = shifted(old), shifted(new)
        case = f"case-{number:03}"
        checklist = f"checklist-{number:03}"
        paths = {
            "before.md": f"# Fictional policy | before revision r1\nPolicy ID: {case}\nTopic: {topic}\nRule: {old}.\n",
            "checklist.md": f"# Fictional downstream checklist\nChecklist ID: {checklist}\nTopic: {topic}\nCurrent instruction: use {old} for {topic}.\nThis checklist explicitly implements policy {case}.\n",
            "unrelated.md": f"# Fictional unrelated checklist\nChecklist ID: unrelated-{number:03}\nTopic: meeting room labels\nInstruction: label meeting rooms alphabetically. This is unrelated to {topic}.\n",
        }
        if new is not None:
            paths["after.md"] = (
                f"# Fictional policy | after revision r2\nPolicy ID: {case}\nTopic: {topic}\nRule: {new}.\n"
            )
        else:
            paths["missing.md"] = (
                f"# Fictional revision availability notice\nPolicy ID: {case}\nAfter revision r2 is unavailable. No after rule can be established.\n"
            )
        for filename, text in paths.items():
            name = f"{case}/{filename}"
            write(inputs / name, text.encode())
            files[name] = digest(text.encode())
        cases[case] = {
            "topic": topic,
            "before": "r1",
            "after": "r2",
            "after_available": new is not None,
        }
        expected[case] = {
            "status": "insufficient_evidence"
            if new is None
            else "unchanged"
            if new == old
            else "candidate_impacts",
            "candidate_ids": [] if new is None or new == old else [checklist],
            "old_rule": old,
            "new_rule": new,
        }
    manifest = {
        "seed": seed,
        "generator": "digest-v2",
        "template_revision": "office-policy-revisions-v1",
        "profile": profile,
        "record_counts": {"cases": count, "documents": len(files)},
        "timezone": "UTC",
        "locale": "invariant",
        "logical_time": "2026-09-11T00:00:00Z",
        "cases": cases,
        "files": files,
    }
    save(inputs / "manifest.json", manifest)
    save(oracle / "expected.json", expected)
    return manifest


def verify(inputs):
    inputs = Path(inputs)
    manifest = read(inputs / "manifest.json")
    settings = profile_settings(manifest["profile"])
    if (
        manifest["generator"] != "digest-v2"
        or manifest["seed"] != settings["seed"]
        or len(manifest["cases"]) != settings["cases"]
        or len(manifest["files"]) != settings["cases"] * 4
    ):
        raise ValueError("Unexpected fixture generation")
    for name, expected in manifest["files"].items():
        path = inputs / name
        if (
            path.resolve().parent.parent != inputs.resolve()
            or digest(path.read_bytes()) != expected
        ):
            raise ValueError("Source hash or path mismatch")
    actual = {
        str(p.relative_to(inputs)).replace("\\", "/")
        for p in inputs.glob("case-*/*")
        if p.is_file()
    }
    if actual != set(manifest["files"]):
        raise ValueError("Unmanifested source revision")
    return manifest
