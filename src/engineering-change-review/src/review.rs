// SPDX-License-Identifier: Apache-2.0
use crate::{
    bootstrap::{self, Grant},
    data::*,
};
use anyhow::{ensure, Result};
use futures_util::StreamExt;
use munarium_client::{dto, TurnStreamEvent};
use serde_json::{json, Value};
use std::{collections::BTreeSet, fs, path::Path};

pub fn exit_code(packet: &Value) -> i32 {
    if packet["analysis_status"] != "verified" {
        3
    } else if packet["deterministic_failures"]
        .as_array()
        .is_some_and(|x| !x.is_empty())
    {
        1
    } else {
        0
    }
}
pub async fn run(
    work: &str,
    input: &str,
    component: &str,
    recover: bool,
    crash: bool,
    url: Option<&str>,
) -> Result<Value> {
    let _lock = lock(work)?;
    let g = bootstrap::grant()?;
    let runbook = g
        .scopes
        .get(component)
        .ok_or_else(|| anyhow::anyhow!("Component is not in the trusted CI scope map"))?;
    let (change, diff) = load_change(input)?;
    let key = hash(format!(
        "{}{}{}{}{}",
        serde_json::to_string(&change)?,
        diff,
        runbook,
        g.uid,
        g.config
    ));
    let path = format!("{work}/journal.json");
    let mut journal = if Path::new(&path).exists() {
        let old = read(&path)?;
        ensure!(
            old["key"] == key,
            "Changed input, component or identity requires a fresh work directory"
        );
        old
    } else {
        let value = json!({"key":key,"state":"creating_session","query":format!("Trusted review component: {component}\nManifest: {}\nSupplied diff:\n{diff}",serde_json::to_string(&change)?),"runbook":runbook,"uid":g.uid,"input_hash":hash(&diff),"manifest_hash":hash(serde_json::to_vec(&change)?)});
        save(&path, &value)?;
        value
    };
    let result = if journal.get("result").is_some() {
        Ok(journal["result"].clone())
    } else {
        resolve(&g, &mut journal, &path, recover, crash, url).await
    };
    let mut packet = json!({"fictional":true,"change_id":change.id,"component":component,"manifest_component":change.component,"deterministic_failures":checks(&change),"analysis_status":"unavailable","advisory_only":true,"input_hash":journal["input_hash"],"manifest_hash":journal["manifest_hash"],"runbook":runbook,"client_revision":REVISION,"recovered":journal["recovered"].as_bool().unwrap_or(false)});
    match result {
        Ok(result) => {
            match validate(&result, &g, component, &change) {
                Ok(answer) => {
                    packet["analysis_status"] = json!("verified");
                    packet["suggestion"] = answer;
                }
                Err(error) => {
                    packet["analysis_status"] = json!("unverified");
                    packet["error"] = json!(error.to_string());
                }
            }
            packet["evidence"] = result;
        }
        Err(error) => {
            packet["error"] = json!(error.to_string());
        }
    }
    packet["exit_code"] = json!(exit_code(&packet));
    save(format!("{work}/review.json"), &packet)?;
    let text=format!("# Fictional engineering change review | {}\n\nTrusted component: {component}\nAnalysis: {} | CI exit: {}\nDeterministic failures: {}\nModel findings: advisory only\nSuggestion: {}\nRequirement: {}\nRelease procedure: {}\n\nCitations: {}\nDiff SHA-256: {}\nRunbook: {}\nSession: {}\nModel: {} / {}\nUnresolved: {}\nNo comment, merge, deployment or external publication performed.\n",change.id,packet["analysis_status"],packet["exit_code"],packet["deterministic_failures"],packet["suggestion"]["finding"],packet["suggestion"]["requirement_quote"],packet["suggestion"]["release_quote"],packet["suggestion"]["citations"],packet["input_hash"],runbook,packet["evidence"]["session_id"],packet["evidence"]["completion"]["provider"],packet["evidence"]["completion"]["model"],packet["error"]);
    write(format!("{work}/review.md"), text.as_bytes())?;
    Ok(packet)
}
async fn resolve(
    g: &Grant,
    journal: &mut Value,
    path: &str,
    recover: bool,
    crash: bool,
    url: Option<&str>,
) -> Result<Value> {
    let api = bootstrap::client_at(url.unwrap_or(&bootstrap::endpoint()), &g.token, &g.uid)?;
    if journal["session_id"].is_string() {
        ensure!(
            recover,
            "Uncertain turn requires explicit recovery; no blind replay"
        );
        let session = api
            .sessions
            .get(journal["session_id"].as_str().unwrap())
            .await?;
        ensure!(
            session.uid == g.uid && session.runbook_ref == journal["runbook"].as_str().unwrap(),
            "Transcript identity mismatch"
        );
        let matches: Vec<_> = session
            .turns
            .iter()
            .filter(|t| t.query == journal["query"].as_str().unwrap() && t.completion.is_some())
            .collect();
        ensure!(matches.len() == 1, "No uniquely recoverable completed turn");
        let turn = matches[0];
        let mut completion = turn.completion.clone().unwrap();
        ensure!(
            completion["resolved"]["provider"] == g.config,
            "Recovered route mismatch"
        );
        completion["was_override"] = completion["resolved"]["was_override"].clone();
        let result = json!({"session_id":session.session_id,"ordinal":turn.ordinal,"collections_searched":turn.collections_searched,"hits":turn.hits,"envelopes":turn.envelope,"completion":completion});
        journal["result"] = result.clone();
        journal["recovered"] = json!(true);
        journal["state"] = json!("response_saved");
        save(path, journal)?;
        return Ok(result);
    }
    ensure!(
        journal["state"] == "creating_session" && !recover,
        "Unknown session creation requires operator inspection"
    );
    // A saved creating_session intent is not retried after a failed create request.
    journal["state"] = json!("session_creation_uncertain");
    save(path, journal)?;
    let session = api
        .sessions
        .create(journal["runbook"].as_str().unwrap())
        .await?;
    journal["session_id"] = json!(session.session_id);
    journal["state"] = json!("turn_uncertain");
    save(path, journal)?;
    let request = dto::TurnRequest {
        query: journal["query"].as_str().unwrap().into(),
        top_k: Some(6),
        complete: Some(true),
        model_override: None,
        research_profile: None,
    };
    let mut stream = api
        .sessions
        .turn_stream(&session.session_id, request)
        .await?;
    let mut progress = vec![];
    while let Some(event) = stream.next().await {
        match event? {
            TurnStreamEvent::Progress(event) => {
                progress.push(serde_json::to_value(event)?);
                save(Path::new(path).with_file_name("progress.json"), &progress)?;
            }
            TurnStreamEvent::Done(response) => {
                if crash {
                    std::process::exit(71);
                }
                let result = serde_json::to_value(response)?;
                journal["result"] = result.clone();
                journal["state"] = json!("response_saved");
                save(path, journal)?;
                return Ok(result);
            }
        }
    }
    anyhow::bail!("Stream ended without a complete response; no replay")
}
pub fn validate(result: &Value, g: &Grant, component: &str, change: &Change) -> Result<Value> {
    let collection = g.scopes[component].split('@').next().unwrap();
    let completion = &result["completion"];
    ensure!(
        completion["provider"]
            == if g.provider == "fixture" {
                "ollama"
            } else {
                &g.provider
            }
            && completion["model"] == g.model,
        "Unexpected model identity"
    );
    if let Some(values) = completion["verification"]["violations"].as_array() {
        ensure!(values.is_empty(), "Unresolved verification violations");
    }
    let raw = completion["text"]
        .as_str()
        .ok_or_else(|| anyhow::anyhow!("No model answer"))?
        .trim();
    let raw = raw
        .strip_prefix("```json")
        .and_then(|s| s.strip_suffix("```"))
        .unwrap_or(raw)
        .trim();
    let answer: Value = serde_json::from_str(raw)?;
    let keys: BTreeSet<_> = answer
        .as_object()
        .ok_or_else(|| anyhow::anyhow!("Expected JSON object"))?
        .keys()
        .map(String::as_str)
        .collect();
    ensure!(
        keys == BTreeSet::from([
            "finding",
            "requirement_code",
            "requirement_quote",
            "release_quote",
            "advisory",
            "citations"
        ]),
        "Unexpected answer fields"
    );
    ensure!(
        answer["advisory"] == true,
        "Model findings must remain advisory"
    );
    ensure!(
        answer["finding"]
            == if change.migration_plan {
                "no_advisory"
            } else {
                "migration_review_missing"
            },
        "Unsupported finding"
    );
    let architecture =
        fs::read_to_string(format!("/inputs/documents/{component}-architecture.txt"))?;
    let procedure = fs::read_to_string(format!("/inputs/documents/{component}-release.txt"))?;
    let field = |source: &str, prefix: &str| -> Result<String> {
        let mut values = source.lines().filter_map(|line| line.strip_prefix(prefix));
        let value = values
            .next()
            .ok_or_else(|| anyhow::anyhow!("Missing authoritative field"))?
            .to_string();
        ensure!(
            !value.is_empty() && values.next().is_none(),
            "Ambiguous authoritative field"
        );
        Ok(value)
    };
    let code = field(&architecture, "Requirement code: ")?;
    let requirement = field(&architecture, "Requirement: ")?;
    let release = field(&procedure, "Release: ")?;
    ensure!(
        answer["requirement_code"] == code
            && answer["requirement_quote"] == requirement
            && answer["release_quote"] == release,
        "Requirement differs from the selected component"
    );
    let hits = result["hits"]
        .as_array()
        .ok_or_else(|| anyhow::anyhow!("No evidence"))?;
    ensure!(
        result["collections_searched"] == json!([collection]),
        "Unexpected searched scope"
    );
    for hit in hits {
        ensure!(
            hit["collection"] == collection,
            "Denied source escaped its scope"
        );
        let path = hit["source_path"].as_str().unwrap_or("");
        let kind = if path == format!("{collection}/architecture.txt") {
            "architecture"
        } else if path == format!("{collection}/release.txt") {
            "release"
        } else {
            anyhow::bail!("Unexpected source path")
        };
        ensure!(
            hit["source_content_hash"]
                == hash(fs::read(format!(
                    "/inputs/documents/{component}-{kind}.txt"
                ))?),
            "Source content hash mismatch"
        );
    }
    let citations = answer["citations"]
        .as_array()
        .ok_or_else(|| anyhow::anyhow!("Citations must be an array"))?;
    ensure!(
        citations.len() == 2,
        "Both requirement sources must be cited"
    );
    let mut seen = BTreeSet::new();
    for citation in citations {
        let hit = hits
            .iter()
            .find(|h| {
                citation
                    == &json!(format!(
                        "{}/{}",
                        h["collection"].as_str().unwrap_or(""),
                        h["chunk_id"].as_str().unwrap_or("")
                    ))
            })
            .ok_or_else(|| anyhow::anyhow!("Unserved citation"))?;
        let text = hit["text"].as_str().unwrap_or("");
        if text.contains(&requirement) {
            seen.insert("architecture");
        }
        if text.contains(&release) {
            seen.insert("release");
        }
    }
    ensure!(
        seen == BTreeSet::from(["architecture", "release"]),
        "Quotes are not grounded in the cited sources"
    );
    Ok(answer)
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn advisory_does_not_fail_ci() {
        assert_eq!(
            exit_code(
                &json!({"analysis_status":"verified","deterministic_failures":[],"suggestion":{"finding":"migration_review_missing"}})
            ),
            0
        );
    }
    #[test]
    fn deterministic_and_unavailable_are_distinct() {
        assert_eq!(
            exit_code(
                &json!({"analysis_status":"verified","deterministic_failures":["owner_required"]})
            ),
            1
        );
        assert_eq!(
            exit_code(&json!({"analysis_status":"unavailable","deterministic_failures":[]})),
            3
        );
    }
}
