# SPDX-License-Identifier: Apache-2.0
from bench_demo.fixtures import read, save
from bench_demo.metrics import score


def credentials(case):
    profile = read("/inputs/manifest.json")["questions"][case]["profile"]
    return "/credentials" if profile == "public" else "/privileged_credentials"


def assess(record, path, completion=False):
    measured = score(record, read("/oracle/labels.json"))
    assert not measured["access_leakage"]
    manifest = read("/inputs/manifest.json")
    for hit in record["result"]["hits"]:
        source = hit["source_path"].removeprefix(record["namespace"] + "/")
        assert manifest["files"][source] == hit["source_content_hash"]
    if measured["source_recall"] is not None:
        assert measured["source_recall"] == 1
    if completion:
        assert measured["schema_valid"]
        assert measured["abstention_correct"]
        assert measured["required_terms_present"]
        if measured["expected_sources"]:
            assert measured["citation_resolution"] == 1
            assert measured["citation_relevance"] == 1
        assert not measured["verification_violations"]
        assert record["result"]["completion"]["model"] == record["identity"]["model"]
    save(path / "quality.json", measured)
    return measured
