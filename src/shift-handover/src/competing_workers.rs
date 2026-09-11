// SPDX-License-Identifier: Apache-2.0
use crate::{bootstrap, data};
use anyhow::{ensure, Result};
use munarium_client::{dto, MunariumError};
use serde_json::json;
use std::{path::Path, time::Duration};

async fn barrier(path: impl AsRef<Path>) -> Result<()> {
    let deadline = tokio::time::Instant::now() + Duration::from_secs(60);
    while !path.as_ref().exists() {
        ensure!(
            tokio::time::Instant::now() < deadline,
            "Worker barrier timed out"
        );
        tokio::time::sleep(Duration::from_millis(50)).await;
    }
    Ok(())
}

pub async fn run(action: &str, work: &str) -> Result<()> {
    let c = bootstrap::writer_at(&bootstrap::endpoint())?;
    let path = |name: &str| Path::new(work).join(name);
    if action == "init" {
        ensure!(!path("version.json").exists(), "Fresh race state required");
        let result = c
            .commands
            .create_version(
                dto::CreateVersionRequest {
                    parent_version_id: None,
                    metadata: Some(json!({"application":"shift-worker-race"})),
                },
                Some(uuid::Uuid::new_v4().to_string()),
            )
            .await?;
        data::save(path("version.json"), &json!({"version":result.version_id}))?;
        return Ok(());
    }
    let state = data::read(path("version.json"))?;
    let version = state["version"]
        .as_str()
        .ok_or_else(|| anyhow::anyhow!("Missing version"))?;
    if action == "verify" {
        let a = data::read(path("a-done.json"))?;
        let b = data::read(path("b-done.json"))?;
        let conflicts =
            a["conflicts"].as_u64().unwrap_or(99) + b["conflicts"].as_u64().unwrap_or(99);
        ensure!(
            conflicts == 1
                && c.query.head(version).await? == 2
                && a["replay_stable"] == true
                && b["replay_stable"] == true,
            "Expected one conflict, two writes and stable completed replays"
        );
        data::save(
            path("quality.json"),
            &json!({"passed":true,"tests":1,"workers":2,"conflicts":conflicts,"version":version}),
        )?;
        data::write(path("tests.xml"), "<testsuite name=\"competing-worker-containers\" tests=\"1\" failures=\"0\" skipped=\"0\"><testcase name=\"fresh-head-and-key-after-conflict\"/></testsuite>")?;
        return Ok(());
    }
    ensure!(action == "a" || action == "b", "Unknown worker");
    let peer = if action == "a" { "b" } else { "a" };
    let mut head = c.query.head(version).await?;
    ensure!(head == 0, "Fresh ledger required");
    data::save(path(&format!("{action}-ready.json")), &json!({"head":head}))?;
    barrier(path(&format!("{peer}-ready.json"))).await?;
    let mut attempts = Vec::new();
    let mut conflicts = 0;
    for _ in 0..2 {
        let key = uuid::Uuid::new_v4().to_string();
        let body = json!({"expected_head":head,"claim_type":"fact","subject":format!("worker_{action}"),"key":"handover_review","value":"reviewed"});
        attempts.push(json!({"idempotency_key":key,"body":body}));
        data::save(path(&format!("{action}-journal.json")), &attempts)?;
        match c
            .commands
            .propose_claim(
                version,
                serde_json::from_value(body.clone())?,
                Some(key.clone()),
            )
            .await
        {
            Ok(result) => {
                let accepted = serde_json::to_value(result)?;
                ensure!(
                    accepted["claim"]["id"].is_string(),
                    "Missing accepted claim identity"
                );
                data::save(path(&format!("{action}-accepted.json")), &accepted)?;
                barrier(path(&format!("{peer}-accepted.json"))).await?;
                let completed_head = c.query.head(version).await?;
                let replay = serde_json::to_value(
                    c.commands
                        .propose_claim(version, serde_json::from_value(body)?, Some(key))
                        .await?,
                )?;
                ensure!(
                    replay["claim"]["id"] == accepted["claim"]["id"]
                        && c.query.head(version).await? == completed_head,
                    "Completed replay changed ledger"
                );
                data::save(
                    path(&format!("{action}-done.json")),
                    &json!({"conflicts":conflicts,"replay_stable":true,"attempts":attempts}),
                )?;
                return Ok(());
            }
            Err(MunariumError::HeadConflict { .. }) => {
                conflicts += 1;
                head = c.query.head(version).await?;
                ensure!(head == 1, "Unexpected competing head");
            }
            Err(error) => return Err(error.into()),
        }
    }
    anyhow::bail!("Worker did not finish within retry bound")
}
