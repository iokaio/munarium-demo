# SPDX-License-Identifier: Apache-2.0
"""Evidence collection without access to evaluation labels."""

import fcntl
import json
import os
import time
from pathlib import Path

from munarium_client.models import TurnResult

from .fixtures import digest, encode, read, save, verify
from .server import query_client


def answer(result):
    if not result.completion:
        return None
    try:
        value = json.loads(
            result.completion.text.strip().removeprefix("```json\n").removesuffix("\n```")
        )
        if (
            set(value) != {"answer", "citations", "abstained"}
            or not isinstance(value["answer"], str)
            or not isinstance(value["abstained"], bool)
            or not isinstance(value["citations"], list)
            or not all(isinstance(c, str) for c in value["citations"])
        ):
            raise ValueError("Invalid answer schema")
        return value
    except (ValueError, TypeError):
        return {"invalid_schema": True}


def run(
    inputs,
    credentials,
    work,
    case,
    setting="topk",
    complete=False,
    recover=False,
    crash=False,
    override=False,
):
    inputs, work = Path(inputs), Path(work)
    manifest = verify(inputs)
    question = manifest["questions"][case]
    work.mkdir(parents=True, exist_ok=True)
    with (work / ".lock").open("a+b") as lease:
        fcntl.flock(lease, fcntl.LOCK_EX | fcntl.LOCK_NB)
        api, config = query_client(credentials)
        with api:
            if (
                config["fixture_revision"] != digest((inputs / "manifest.json").read_bytes())
                or config["profile"] != question["profile"]
            ):
                raise ValueError("Identity or fixture mismatch")
            identity = {
                "case": case,
                "question": question,
                "settings": config["settings"][setting],
                "runbook": config["runbooks"][setting],
                "model": config["model"],
                "provider": config["provider"],
                "uid": config["uid"],
                "complete": complete,
                "override": override,
                "manifest": config["fixture_revision"],
            }
            key = digest(encode(identity))
            path = work / "journal.json"
            if path.exists():
                journal = read(path)
                if journal["key"] != key:
                    raise ValueError("Experiment changed; use a new work directory")
                if journal.get("record"):
                    save(work / "record.json", journal["record"])
                    return journal["record"]
                if not recover or not journal.get("session_id"):
                    raise RuntimeError("Uncertain turn; inspect its transcript without replay")
                transcript = api.sessions.get(journal["session_id"])
                if transcript.uid != config["uid"] or transcript.runbook_ref != identity["runbook"]:
                    raise ValueError("Transcript identity mismatch")
                turns = [
                    t
                    for t in transcript.turns
                    if t.query == question["query"] and (not complete or t.completion)
                ]
                if len(turns) != 1:
                    raise RuntimeError("No uniquely recoverable turn")
                turn = turns[0]
                completion = dict(turn.completion) if turn.completion else None
                if completion:
                    completion["was_override"] = completion["resolved"]["was_override"]
                result = TurnResult(
                    session_id=transcript.session_id,
                    ordinal=turn.ordinal,
                    collections_searched=turn.collections_searched,
                    hits=turn.hits or [],
                    envelopes=turn.envelope or [],
                    completion=completion,
                )
                journal["recovered"] = True
                elapsed = None
            else:
                journal = {
                    "key": key,
                    "identity": identity,
                    "state": "creating_session",
                    "progress": [],
                }
                save(path, journal)
                session = api.sessions.create(identity["runbook"])
                journal.update(state="uncertain", session_id=session.session_id)
                save(path, journal)
                started = time.monotonic()
                result = None
                try:
                    selected = (
                        {"provider": config["provider_config"], "tier": "fast"}
                        if override
                        else None
                    )
                    for event in api.sessions.turn_stream(
                        session.session_id,
                        query=question["query"],
                        complete=complete,
                        model_override=selected,
                    ):
                        if isinstance(event, TurnResult):
                            result = event
                        else:
                            journal["progress"].append(event.model_dump(mode="json"))
                            save(path, journal)
                except Exception as error:
                    journal["error_category"] = type(error).__name__
                    save(path, journal)
                    raise
                elapsed = time.monotonic() - started
                if result is None:
                    raise RuntimeError("Stream omitted final result")
                if crash:
                    os._exit(71)
            raw = result.model_dump(mode="json")
            if journal.get("recovered"):
                raw.pop("skipped", None)
            record = {
                "identity": identity,
                "namespace": config["namespace"],
                "profile": config["profile"],
                "setting": setting,
                "result": raw,
                "answer": answer(result),
                "elapsed_seconds": elapsed,
                "recovered": journal.get("recovered", False),
            }
            journal.update(state="complete", record=record)
            save(path, journal)
            save(work / "record.json", record)
            return record
