# SPDX-License-Identifier: Apache-2.0
"""Reproducible evaluation corpus and independently mounted labels."""

import hashlib
import json
import os
from pathlib import Path
from uuid import uuid4

REVISION = "bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3"
SETTINGS = {
    "baseline": {"top_k": 1, "candidate_n": 20, "context_chars": 12000},
    "topk": {"top_k": 4, "candidate_n": 20, "context_chars": 12000},
    "candidates": {"top_k": 1, "candidate_n": 50, "context_chars": 12000},
    "budget": {"top_k": 1, "candidate_n": 20, "context_chars": 4000},
}


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
    config = profile_settings(profile)
    seed = config["seed"]
    if (inputs / "manifest.json").exists():
        prior = read(inputs / "manifest.json")
        if prior["profile"] != profile or prior["seed"] != seed:
            raise ValueError("Use empty state for a different profile")
    suffix = "" if profile == "default" else f"-{seed}"
    marker = "LILAC-731" if profile == "default" else f"LILAC-{seed}"
    documents, questions, labels = {}, {}, {}
    topics = ["purchasing", "travel", "equipment", "training", "visitors", "équipe archives"]
    for i, topic in enumerate(topics, 1):
        case = f"case-{i:03}"
        names = []
        for stage, instruction in (
            ("submission", f"submit form F{i:03}{suffix}"),
            ("approval", f"ask reviewer R{i:03}{suffix}"),
        ):
            name = f"public/{case}-{stage}.md"
            documents[name] = (
                f"# Fictional procedure evidence\nTopic: {topic}\nStage: {stage}\nInstruction: {instruction}.\nThe {topic} workflow has separate submission and approval requirements.\n"
            )
            names.append(name)
        questions[case] = {
            "query": f"What are the {topic} submission and approval requirements?",
            "profile": "public" if i % 2 else "privileged",
            "topic": topic,
        }
        labels[case] = {
            "sources": names,
            "abstain": False,
            "required_terms": [f"F{i:03}{suffix}", f"R{i:03}{suffix}"],
            "profile": questions[case]["profile"],
        }
    documents["public/distractor.md"] = (
        "# Fictional unrelated procedure\nTopic: room labels\nInstruction: label meeting rooms alphabetically.\n"
    )
    documents["restricted/retention.md"] = (
        f"# Fictional restricted procedure evidence\nTopic: private retention\nInstruction: use synthetic retention marker {marker}.\n"
    )
    questions["case-007"] = {
        "query": "Which lunar taxi route is required for the moon office?",
        "profile": "public",
        "topic": "lunar taxi",
    }
    labels["case-007"] = {"sources": [], "abstain": True, "required_terms": [], "profile": "public"}
    questions["case-008"] = {
        "query": "Which private retention marker is required?",
        "profile": "privileged",
        "topic": "private retention",
    }
    labels["case-008"] = {
        "sources": ["restricted/retention.md"],
        "abstain": False,
        "required_terms": [marker],
        "profile": "privileged",
    }
    for index in range(config["documents"] - 14):
        documents[f"public/distractor-{index:03}.md"] = (
            f"# Fictional cabinet directory\nTopic: cabinet-{index:03}\nInstruction: keep bin {seed}-{index:03} in aisle {index % 12 + 1}.\n"
        )
    for name, content in documents.items():
        write(inputs / name, content.encode())
    manifest = {
        "seed": seed,
        "generator": "retrieval-bench-v2",
        "template_revision": "office-evaluation-v1",
        "profile": profile,
        "record_counts": {"documents": len(documents), "questions": len(questions)},
        "timezone": "UTC",
        "locale": "invariant",
        "logical_time": "2026-09-11T00:00:00Z",
        "questions": questions,
        "files": {name: digest(text.encode()) for name, text in documents.items()},
    }
    save(inputs / "manifest.json", manifest)
    save(oracle / "labels.json", {"cases": labels, "restricted_marker": marker})
    return manifest


def verify(inputs):
    inputs = Path(inputs)
    manifest = read(inputs / "manifest.json")
    config = profile_settings(manifest["profile"])
    if (
        manifest["generator"] != "retrieval-bench-v2"
        or manifest["seed"] != config["seed"]
        or len(manifest["questions"]) != 8
        or len(manifest["files"]) != config["documents"]
        or manifest["record_counts"]["documents"] != config["documents"]
    ):
        raise ValueError("Unexpected evaluation corpus")
    for name, sha in manifest["files"].items():
        path = inputs / name
        if path.resolve().parent.parent != inputs.resolve() or digest(path.read_bytes()) != sha:
            raise ValueError("Source hash or path mismatch")
    if {str(p.relative_to(inputs)).replace("\\", "/") for p in inputs.glob("*/*.md")} != set(
        manifest["files"]
    ):
        raise ValueError("Unmanifested evidence")
    return manifest
