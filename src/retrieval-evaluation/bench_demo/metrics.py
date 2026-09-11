# SPDX-License-Identifier: Apache-2.0
"""Offline evaluation joins saved responses to private source labels."""

import csv
import io
import json
import statistics
from pathlib import Path

from .fixtures import read, save, write


def score(record, labels):
    result = record["result"]
    expected = labels["cases"][record["identity"]["case"]]
    wanted = set(expected["sources"])
    prefix = record["namespace"] + "/"
    served = {
        f"{hit['collection']}/{hit['chunk_id']}": hit["source_path"].removeprefix(prefix)
        for hit in result["hits"]
    }
    found = set(served.values())
    answer = record["answer"]
    citations = answer.get("citations", []) if answer else []
    resolved = [c for c in citations if c in served]
    relevant = [c for c in resolved if served[c] in wanted]
    completion = result.get("completion")
    serialized = json.dumps(result, ensure_ascii=False)
    leakage = record["profile"] == "public" and (
        any(path.startswith("restricted/") for path in found)
        or labels["restricted_marker"] in serialized
    )
    validity = answer is not None and not answer.get("invalid_schema", False)
    return {
        "case": record["identity"]["case"],
        "profile": record["profile"],
        "setting": record["setting"],
        "mode": "completion" if completion else "retrieval",
        "expected_sources": sorted(wanted),
        "retrieved_sources": sorted(found),
        "source_recall": len(wanted & found) / len(wanted) if wanted else None,
        "citation_resolution": len(resolved) / len(citations) if citations else None,
        "citation_relevance": len(relevant) / len(citations) if citations else None,
        "abstention_correct": answer.get("abstained") == expected["abstain"] if validity else None,
        "required_terms_present": all(
            term in answer.get("answer", "") for term in expected["required_terms"]
        )
        if validity
        else None,
        "schema_valid": validity if completion else None,
        "access_leakage": bool(leakage),
        "verification_violations": (completion.get("verification") or {}).get("violations", [])
        if completion
        else [],
        "latency_seconds": record["elapsed_seconds"],
        "input_tokens": completion["input_tokens"] if completion else 0,
        "output_tokens": completion["output_tokens"] if completion else 0,
        "provider": completion["provider"] if completion else None,
        "model": completion["model"] if completion else None,
    }


def report(root, labels_path):
    root = Path(root)
    labels = read(labels_path)
    rows = [score(read(path), labels) for path in sorted(root.rglob("record.json"))]
    if not rows:
        raise ValueError("No executed experiments")
    groups = {}
    for row in rows:
        key = row["setting"] + "/" + row["mode"]
        groups.setdefault(key, []).append(row)
    summary = {}
    for key, values in groups.items():
        recall = [v["source_recall"] for v in values if v["source_recall"] is not None]
        latency = [v["latency_seconds"] for v in values if v["latency_seconds"] is not None]
        repeats = {}
        for value in values:
            repeats.setdefault(value["case"], []).append(value)
        summary[key] = {
            "runs": len(values),
            "mean_source_recall": statistics.mean(recall) if recall else None,
            "mean_latency_seconds": statistics.mean(latency) if latency else None,
            "input_tokens": sum(v["input_tokens"] for v in values),
            "output_tokens": sum(v["output_tokens"] for v in values),
            "replicates_per_case": {case: len(reps) for case, reps in repeats.items()},
            "uncertainty": "Observed repeats only; synthetic cases and small samples do not establish production confidence.",
        }
    result = {
        "experiments": rows,
        "summary": summary,
        "access_leakage": sum(r["access_leakage"] for r in rows),
        "cost_currency": None,
    }
    save(root / "metrics.json", result)
    output = io.StringIO(newline="")
    writer = csv.DictWriter(output, fieldnames=list(rows[0]))
    writer.writeheader()
    writer.writerows(rows)
    write(root / "metrics.csv", output.getvalue().encode())
    text = "# Retrieval evaluation bench\n\nFixed synthetic workload; one setting varies from baseline at a time. Token usage is not monetary cost.\n\n| Setting / mode | Runs | Mean source recall | Mean seconds | Input / output tokens |\n|---|---:|---:|---:|---:|\n"
    for key, value in summary.items():
        text += f"| {key} | {value['runs']} | {value['mean_source_recall']} | {value['mean_latency_seconds']} | {value['input_tokens']} / {value['output_tokens']} |\n"
    text += f"\nForbidden-source exposures: {result['access_leakage']}.\n\nCitation resolution and relevance, abstention, schema validity and verification violations are retained per experiment in CSV and JSON. Missing metrics are null, not successful zeroes. Recovered turns have no reconstructed latency.\n\nObserved repeats are retained separately. Small synthetic samples do not establish production confidence or comparative model rankings.\n"
    write(root / "metrics.md", text.encode())
    if result["access_leakage"]:
        raise RuntimeError("Access-isolation regression")
    return result
