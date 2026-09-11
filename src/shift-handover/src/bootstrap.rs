// SPDX-License-Identifier: Apache-2.0
use crate::data::*;
use anyhow::{ensure, Result};
use munarium_client::{dto, MunariumClient, MunariumClientOptions};
use serde_json::json;
use std::{collections::BTreeMap, env, fs, time::Duration};
pub fn endpoint() -> String {
    env::var("MUNARIUM_REST_URL").unwrap_or("http://server:8080".into())
}
pub fn client_at(url: &str, token: &str, uid: &str) -> Result<MunariumClient> {
    Ok(MunariumClient::rest(
        MunariumClientOptions::new(url)
            .token(token)
            .uid(uid)
            .read_retries(0),
    )?)
}
pub fn writer_at(url: &str) -> Result<MunariumClient> {
    client_at(url, &env::var("MUNARIUM_TOKEN")?, "shift-writer")
}
pub fn reader() -> Result<MunariumClient> {
    client_at(
        &endpoint(),
        &env::var("MUNARIUM_READ_TOKEN")?,
        "shift-reader",
    )
}
pub async fn ready() -> Result<()> {
    let c = reader()?;
    for _ in 0..60 {
        if let Ok(v) = c.server_version().await {
            ensure!(v.version == "1.1.1", "Server 1.1.1 required");
            return Ok(());
        }
        tokio::time::sleep(Duration::from_secs(1)).await;
    }
    anyhow::bail!("Server not ready")
}
pub fn versions() -> Result<BTreeMap<String, String>> {
    Ok(serde_json::from_value(read("/credentials/versions.json")?)?)
}
pub async fn run() -> Result<()> {
    ready().await?;
    let c = writer_at(&endpoint())?;
    let manifest = read("/inputs/manifest.json")?;
    for (name, expected) in manifest["files"].as_object().unwrap() {
        ensure!(
            name.starts_with("shift-") && !name.contains('/') && !name.contains('\\'),
            "Unexpected fixture path"
        );
        ensure!(
            hash(fs::read(format!("/inputs/{name}"))?) == expected.as_str().unwrap(),
            "Fixture mismatch"
        );
    }
    c.runbooks
        .apply_shape(&fs::read_to_string("/app/shapes/shift.yaml")?, None)
        .await?;
    let run = uuid::Uuid::new_v4().to_string();
    let mut versions = BTreeMap::new();
    for i in 1..=8 {
        let shift = format!("shift-{i:03}");
        let key = uuid::Uuid::new_v4().to_string();
        let req = dto::CreateVersionRequest {
            parent_version_id: None,
            metadata: Some(
                json!({"application":"shift-handover","shift":shift,"manifest_hash":hash(serde_json::to_vec(&manifest)?)}),
            ),
        };
        save(
            format!("/work/bootstrap/{run}/{shift}-intent.json"),
            &json!({"key":key,"body":req}),
        )?;
        let result = c.commands.create_version(req, Some(key)).await?;
        versions.insert(shift, result.version_id);
        save(format!("/work/bootstrap/{run}/versions.json"), &versions)?;
    }
    save("/credentials/versions.json", &versions)?;
    save(
        format!("/work/bootstrap/{run}/manifest.json"),
        &json!({"inputs":manifest,"client_revision":REVISION,"versions":versions,"model_calls":0}),
    )?;
    println!("Eight ledger versions initialized; no model or document index required.");
    Ok(())
}
