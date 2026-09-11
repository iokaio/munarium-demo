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


def generate(inputs, oracle):
    inputs, oracle = Path(inputs), Path(oracle)
    documents, questions, labels = {}, {}, {}
    topics = ["purchasing", "travel", "equipment", "training", "visitors", "équipe archives"]
    for i, topic in enumerate(topics, 1):
        case = f"case-{i:03}"
        names = []
        for stage, instruction in (
            ("submission", f"submit form F{i:03}"),
            ("approval", f"ask reviewer R{i:03}"),
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
            "required_terms": [f"F{i:03}", f"R{i:03}"],
            "profile": questions[case]["profile"],
        }
    documents["public/distractor.md"] = (
        "# Fictional unrelated procedure\nTopic: room labels\nInstruction: label meeting rooms alphabetically.\n"
    )
    documents["restricted/retention.md"] = (
        "# Fictional restricted procedure evidence\nTopic: private retention\nInstruction: use synthetic retention marker LILAC-731.\n"
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
        "required_terms": ["LILAC-731"],
        "profile": "privileged",
    }
    for name, content in documents.items():
        write(inputs / name, content.encode())
    manifest = {
        "seed": 12091,
        "generator": "retrieval-bench-v1",
        "logical_time": "2026-09-11T00:00:00Z",
        "questions": questions,
        "files": {name: digest(text.encode()) for name, text in documents.items()},
    }
    save(inputs / "manifest.json", manifest)
    save(oracle / "labels.json", {"cases": labels, "restricted_marker": "LILAC-731"})
    return manifest


def verify(inputs):
    inputs = Path(inputs)
    manifest = read(inputs / "manifest.json")
    if manifest["generator"] != "retrieval-bench-v1" or len(manifest["questions"]) != 8:
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
