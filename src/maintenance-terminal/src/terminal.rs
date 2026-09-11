// SPDX-License-Identifier: Apache-2.0
use crate::{
    bootstrap::{self, Grant},
    data::*,
};
use anyhow::{bail, ensure, Result};
use futures_util::StreamExt;
use munarium_client::{dto, TurnStreamEvent};
use serde_json::{json, Value};
use std::{fs, path::Path};

fn lock_directory(dir: &str) -> Result<fs::File> {
    fs::create_dir_all(dir)?;
    let file = fs::OpenOptions::new()
        .read(true)
        .write(true)
        .create(true)
        .truncate(false)
        .open(format!("{dir}/.lock"))?;
    file.try_lock()
        .map_err(|_| anyhow::anyhow!("This output directory is already in use"))?;
    Ok(file)
}

pub fn request(asset: &Asset, complete: bool) -> dto::TurnRequest {
    dto::TurnRequest {query:format!("Explain the maintenance procedure code, inspection and revision notice for fictional asset {} selected revision {}. {}",asset.asset,asset.revision,if asset.historical {"Historical evidence only; not a current procedure."} else {"Use only this selected current revision; abstain if required evidence is missing."}),top_k:Some(6),complete:Some(complete),model_override:None,research_profile:None}
}
pub async fn find(g: &Grant, asset: &Asset, dir: &str) -> Result<Value> {
    let _lock = lock_directory(dir)?;
    ensure!(
        !Path::new(&format!("{dir}/intent.json")).exists(),
        "This directory has a saved explanation intent; use a fresh directory for new retrieval"
    );
    let c = bootstrap::client(&g.token, &g.uid)?;
    let session = c.sessions.create(&g.scopes[&asset.id].retrieve).await?;
    let response = c
        .sessions
        .turn(&session.session_id, request(asset, false))
        .await?;
    save(format!("{dir}/retrieval.json"), &response)?;
    ensure!(
        response.completion.is_none(),
        "Retrieval unexpectedly completed"
    );
    let value = serde_json::to_value(response)?;
    validate_evidence(&value, g, asset)?;
    save(format!("{dir}/selection.json"), asset)?;
    fs::write(format!("{dir}/retrieval.txt"), display(asset, &value))?;
    Ok(value)
}
pub fn validate_evidence(turn: &Value, g: &Grant, asset: &Asset) -> Result<()> {
    ensure!(
        turn["collections_searched"] == json!([g.scopes[&asset.id].collection]),
        "Wrong searched collections"
    );
    let hits = turn["hits"]
        .as_array()
        .ok_or_else(|| anyhow::anyhow!("Missing hits"))?;
    ensure!(!hits.is_empty(), "No evidence available");
    for hit in hits {
        ensure!(
            hit["collection"] == g.scopes[&asset.id].collection,
            "Scope leak"
        );
        let path = hit["source_path"].as_str().unwrap_or("");
        let kind = path.rsplit('/').next().unwrap_or("");
        ensure!(
            ["manual.txt", "inspection.txt", "revision.txt"].contains(&kind),
            "Unknown source"
        );
        ensure!(
            path == format!("{}/{kind}", g.scopes[&asset.id].collection),
            "Incorrect source path"
        );
        let bytes = fs::read(format!("/inputs/documents/{}-{kind}", asset.id))?;
        ensure!(
            hit["source_content_hash"] == hash(bytes),
            "Source hash mismatch"
        );
        ensure!(
            !hit["chunk_id"].as_str().unwrap_or("").is_empty(),
            "Missing chunk identity"
        );
        ensure!(
            hit["text"]
                .as_str()
                .unwrap_or("")
                .contains(&format!("revision {}", asset.revision)),
            "Revision not in evidence"
        );
    }
    Ok(())
}
pub fn parse_answer(turn: &Value) -> Result<Value> {
    let raw = turn["completion"]["text"]
        .as_str()
        .ok_or_else(|| anyhow::anyhow!("No completion"))?;
    let text = raw
        .trim()
        .strip_prefix("```json")
        .or_else(|| raw.trim().strip_prefix("```"))
        .unwrap_or(raw.trim())
        .trim();
    Ok(serde_json::from_str(
        text.strip_suffix("```").unwrap_or(text).trim(),
    )?)
}
pub fn validate_answer(turn: &Value, g: &Grant, asset: &Asset) -> Result<Value> {
    validate_evidence(turn, g, asset)?;
    let answer = parse_answer(turn)?;
    ensure!(
        answer["revision"] == asset.revision,
        "Wrong answer revision"
    );
    let status = answer["status"].as_str().unwrap_or("");
    ensure!(
        ["available", "historical", "insufficient"].contains(&status),
        "Invalid status"
    );
    ensure!(
        !asset.historical || status == "historical",
        "Historical source promoted to current"
    );
    ensure!(
        asset.historical || status != "historical",
        "Current source labeled historical"
    );
    let code = answer["code"].as_str().unwrap_or("");
    ensure!(!code.is_empty(), "Empty procedure code");
    let explanation = answer["explanation"].as_str().unwrap_or("");
    ensure!(
        explanation.contains(code),
        "Explanation does not identify procedure code"
    );
    validate_citations(&answer, turn["hits"].as_array().unwrap())?;
    if code.ends_with("-REQUIRED") {
        ensure!(
            status == "insufficient",
            "Missing evidence became actionable"
        );
    }
    let completion = &turn["completion"];
    ensure!(
        completion["provider"]
            == if g.provider == "fixture" {
                "ollama"
            } else {
                &g.provider
            },
        "Wrong provider family"
    );
    ensure!(completion["model"] == g.model, "Wrong model");
    if let Some(v) = completion["verification"]["violations"].as_array() {
        ensure!(v.is_empty(), "Remaining verification violations");
    }
    Ok(answer)
}
fn validate_citations(answer: &Value, hits: &[Value]) -> Result<()> {
    let code = answer["code"].as_str().unwrap_or("");
    let explanation = answer["explanation"].as_str().unwrap_or("");
    let citations = answer["citations"]
        .as_array()
        .ok_or_else(|| anyhow::anyhow!("Missing citations"))?;
    ensure!(!citations.is_empty(), "Empty citations");
    let mut grounded = false;
    for citation in citations {
        let label = citation
            .as_str()
            .ok_or_else(|| anyhow::anyhow!("Invalid citation"))?;
        let hit = hits
            .iter()
            .find(|h| {
                format!(
                    "{}/{}",
                    h["collection"].as_str().unwrap(),
                    h["chunk_id"].as_str().unwrap()
                ) == label
            })
            .ok_or_else(|| anyhow::anyhow!("Unresolved citation: {label}"))?;
        grounded |= hit["text"]
            .as_str()
            .unwrap_or("")
            .contains(&format!("Procedure code: {code}."))
            && explanation.contains(&format!("[{label}]"));
    }
    ensure!(grounded, "Procedure code not grounded in cited evidence");
    for bracket in explanation.split('[').skip(1) {
        let label = bracket.split(']').next().unwrap_or("");
        if label.contains('/') {
            ensure!(
                citations.iter().any(|c| c.as_str() == Some(label)),
                "Unresolved inline citation: {label}"
            );
        }
    }
    Ok(())
}
pub async fn explain(g: &Grant, asset: &Asset, dir: &str, url: Option<&str>) -> Result<Value> {
    let _lock = lock_directory(dir)?;
    let journal_path = format!("{dir}/intent.json");
    if Path::new(&journal_path).exists() {
        let journal = read(&journal_path)?;
        ensure!(
            journal["asset"] == serde_json::to_value(asset)?
                && journal["config"] == g.config
                && journal["uid"] == g.uid,
            "Saved intent belongs to another selection/configuration/identity"
        );
        if Path::new(&format!("{dir}/response.json")).exists() {
            let response = read(format!("{dir}/response.json"))?;
            export(g, asset, dir, &response)?;
            return Ok(response);
        }
        return reconcile_saved(g, asset, dir).await;
    }
    ensure!(
        read(format!("{dir}/selection.json"))? == serde_json::to_value(asset)?,
        "Find this exact revision before explain"
    );
    validate_evidence(&read(format!("{dir}/retrieval.json"))?, g, asset)?;
    let c = bootstrap::client_at(url.unwrap_or(&bootstrap::endpoint()), &g.token, &g.uid)?;
    let session = c.sessions.create(&g.scopes[&asset.id].explain).await?;
    let req = request(asset, true);
    save(
        &journal_path,
        &json!({"state":"uncertain","session_id":session.session_id,"query":req.query,"asset":asset,"config":g.config,"uid":g.uid}),
    )?;
    let mut stream = c.sessions.turn_stream(&session.session_id, req).await?;
    let mut progress = vec![];
    while let Some(event) = stream.next().await {
        match event? {
            TurnStreamEvent::Progress(p) => {
                let v = serde_json::to_value(p)?;
                eprintln!("progress: {}", v["stage"]);
                progress.push(v);
                save(format!("{dir}/progress.json"), &progress)?;
            }
            TurnStreamEvent::Done(response) => {
                let value = serde_json::to_value(response)?;
                save(format!("{dir}/response.json"), &value)?;
                export(g, asset, dir, &value)?;
                return Ok(value);
            }
        }
    }
    bail!("Stream ended without a result; intent retained as uncertain. Reconcile the saved transcript.")
}
pub async fn reconcile(g: &Grant, asset: &Asset, dir: &str) -> Result<Value> {
    let _lock = lock_directory(dir)?;
    reconcile_saved(g, asset, dir).await
}
async fn reconcile_saved(g: &Grant, asset: &Asset, dir: &str) -> Result<Value> {
    let path = format!("{dir}/intent.json");
    let journal = read(&path)?;
    ensure!(
        journal["asset"] == serde_json::to_value(asset)?
            && journal["config"] == g.config
            && journal["uid"] == g.uid,
        "Recovery identity/configuration mismatch"
    );
    let transcript = bootstrap::client(&g.token, &g.uid)?
        .sessions
        .get(journal["session_id"].as_str().unwrap())
        .await?;
    save(format!("{dir}/transcript.json"), &transcript)?;
    ensure!(
        transcript.uid == g.uid && transcript.runbook_ref == g.scopes[&asset.id].explain,
        "Recovered session identity or runbook mismatch"
    );
    let matching: Vec<_> = transcript
        .turns
        .iter()
        .filter(|t| t.query == journal["query"].as_str().unwrap() && t.completion.is_some())
        .collect();
    ensure!(
        matching.len() == 1,
        "Transcript has {} matching completed turns; remains uncertain, no resubmission",
        matching.len()
    );
    let turn = matching[0];
    let mut completion = turn.completion.clone().unwrap();
    ensure!(
        completion["resolved"]["provider"] == g.config,
        "Recovered provider configuration mismatch"
    );
    completion["was_override"] = completion["resolved"]["was_override"].clone();
    let value = json!({"session_id":transcript.session_id,"ordinal":turn.ordinal,"collections_searched":turn.collections_searched,"hits":turn.hits,"envelopes":turn.envelope,"completion":completion,"recovered":true});
    save(format!("{dir}/response.json"), &value)?;
    export(g, asset, dir, &value)?;
    Ok(value)
}
fn export(g: &Grant, asset: &Asset, dir: &str, response: &Value) -> Result<()> {
    let answer = validate_answer(response, g, asset)?;
    save(format!("{dir}/answer.json"), &answer)?;
    let text = format!(
        "{}\nEXPLANATION ({})\n{}\nProvider: {} / {}\n",
        display(asset, response),
        answer["status"].as_str().unwrap(),
        answer["explanation"].as_str().unwrap(),
        g.provider,
        g.model
    );
    fs::write(format!("{dir}/answer.txt.tmp"), text)?;
    fs::rename(format!("{dir}/answer.txt.tmp"), format!("{dir}/answer.txt"))?;
    let mut journal = read(format!("{dir}/intent.json"))?;
    journal["state"] = json!("complete");
    save(format!("{dir}/intent.json"), &journal)
}
pub fn display(asset: &Asset, turn: &Value) -> String {
    let mut text=format!("MAINTENANCE PROCEDURE TERMINAL | synthetic equipment\nAsset {} | Revision {} | {}\nCollections searched: {}\n",asset.asset,asset.revision,if asset.historical {"HISTORICAL - reference only"} else {"CURRENT"},turn["collections_searched"]);
    if let Some(skipped) = turn.get("skipped") {
        text.push_str(&format!("Collections skipped: {skipped}\n"));
    }
    for (i, h) in turn["hits"]
        .as_array()
        .unwrap_or(&vec![])
        .iter()
        .enumerate()
    {
        text.push_str(&format!(
            "\nSOURCE {}  {}\nChunk: {}\nSHA256: {}\n{}\n",
            i + 1,
            h["source_path"].as_str().unwrap_or(""),
            h["chunk_id"].as_str().unwrap_or(""),
            h["source_content_hash"].as_str().unwrap_or(""),
            h["text"].as_str().unwrap_or("")
        ));
    }
    text
}
pub async fn tui(g: &Grant, dir: &str) -> Result<()> {
    use tokio::io::{AsyncBufReadExt, BufReader};
    let assets = catalogue("/inputs")?;
    println!("MAINTENANCE PROCEDURE TERMINAL\nSynthetic equipment | retrieval first\nCommands: list, find ASSET REVISION, source N, explain, export, quit");
    for a in &assets {
        println!(
            "{} {} {}",
            a.asset,
            a.revision,
            if a.historical {
                "HISTORICAL"
            } else {
                "CURRENT"
            }
        );
    }
    let mut selected: Option<(Asset, Value, String)> = None;
    let mut lines = BufReader::new(tokio::io::stdin()).lines();
    while let Some(line) = lines.next_line().await? {
        let args: Vec<_> = line.split_whitespace().collect();
        let result: Result<()> = async {
            match args.as_slice() {
                ["quit"] => return Ok(()),
                ["list"] => {
                    for a in &assets {
                        println!(
                            "{} {} {}",
                            a.asset,
                            a.revision,
                            if a.historical {
                                "HISTORICAL"
                            } else {
                                "CURRENT"
                            }
                        );
                    }
                }
                ["find", id, revision] => {
                    selected = None;
                    let a = select(&assets, id, revision)?.clone();
                    let path = format!("{dir}/{}", uuid::Uuid::new_v4());
                    let v = find(g, &a, &path).await?;
                    println!(
                        "{}\nUse source N to inspect; explain to request AI.",
                        display(&a, &v)
                    );
                    selected = Some((a, v, path));
                }
                ["source", number] => {
                    let (_, v, _) = selected
                        .as_ref()
                        .ok_or_else(|| anyhow::anyhow!("Find a revision first"))?;
                    let n: usize = number.parse()?;
                    ensure!(n > 0, "Source numbers start at 1");
                    let h = v["hits"]
                        .as_array()
                        .unwrap()
                        .get(n - 1)
                        .ok_or_else(|| anyhow::anyhow!("No such source"))?;
                    println!("{}", serde_json::to_string_pretty(h)?);
                }
                ["explain"] => {
                    let (a, _, path) = selected
                        .as_ref()
                        .ok_or_else(|| anyhow::anyhow!("Find a revision first"))?;
                    explain(g, a, path, None).await?;
                    println!("{}", fs::read_to_string(format!("{path}/answer.txt"))?);
                }
                ["export"] => {
                    let (_, _, path) = selected
                        .as_ref()
                        .ok_or_else(|| anyhow::anyhow!("Find a revision first"))?;
                    println!("Evidence and any verified explanation are saved at {path}");
                }
                _ => bail!("Commands: list, find ASSET REVISION, source N, explain, export, quit"),
            }
            Ok(())
        }
        .await;
        if let Err(e) = result {
            println!("{e:#}");
        }
        if args == ["quit"] {
            break;
        }
    }
    Ok(())
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn citations_allow_supporting_sources_but_reject_invented_inline_labels() {
        let hits = vec![
            json!({"collection":"scope","chunk_id":"manual","text":"Procedure code: BLUE-17."}),
            json!({"collection":"scope","chunk_id":"note","text":"Inspection present."}),
        ];
        let mut answer = json!({"code":"BLUE-17","explanation":"BLUE-17 [scope/manual]","citations":["scope/manual","scope/note"]});
        assert!(validate_citations(&answer, &hits).is_ok());
        answer["explanation"] = json!("BLUE-17 [scope/manual] [scope/invented]");
        assert!(validate_citations(&answer, &hits).is_err());
        answer["explanation"] = json!("BLUE-17 [scope/note]");
        assert!(validate_citations(&answer, &hits).is_err());
    }
    #[test]
    fn concurrent_directory_use_is_rejected_and_lock_releases() {
        let dir = std::env::temp_dir().join(uuid::Uuid::new_v4().to_string());
        let path = dir.to_str().unwrap();
        let first = lock_directory(path).unwrap();
        assert!(lock_directory(path).is_err());
        drop(first);
        assert!(lock_directory(path).is_ok());
    }
    #[test]
    fn retrieval_has_no_completion_or_override() {
        let a = Asset {
            id: "x".into(),
            asset: "A".into(),
            revision: "R1".into(),
            historical: true,
        };
        let r = request(&a, false);
        assert_eq!(r.complete, Some(false));
        assert!(r.model_override.is_none());
        assert!(r.query.contains("Historical"));
    }
    #[test]
    fn malformed_answer_fails_closed() {
        assert!(parse_answer(&json!({"completion":{"text":"not json"}})).is_err());
        assert!(parse_answer(&json!({})).is_err());
    }
}
