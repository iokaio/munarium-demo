// SPDX-License-Identifier: Apache-2.0
use crate::data::*;
use anyhow::{ensure, Result};
use base64::{engine::general_purpose::STANDARD, Engine};
use munarium_client::{dto, MunariumClient, MunariumClientOptions};
use serde::{Deserialize, Serialize};
use serde_json::json;
use std::{collections::BTreeMap, env, fs, time::Duration};
#[derive(Clone, Serialize, Deserialize)]
pub struct Grant {
    pub token: String,
    pub uid: String,
    pub provider: String,
    pub model: String,
    pub config: String,
    pub scopes: BTreeMap<String, String>,
}
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
pub fn ops(management: bool) -> Result<MunariumClient> {
    client_at(
        &endpoint(),
        &env::var(if management {
            "MUNARIUM_MGMT_TOKEN"
        } else {
            "MUNARIUM_TOKEN"
        })?,
        "engineering-bootstrap",
    )
}
pub fn grant() -> Result<Grant> {
    Ok(serde_json::from_value(read("/credentials/query.json")?)?)
}
pub async fn ready(c: &MunariumClient) -> Result<()> {
    for _ in 0..60 {
        if let Ok(v) = c.server_version().await {
            ensure!(v.version == "1.1.1", "Server 1.1.1 required");
            return Ok(());
        }
        tokio::time::sleep(Duration::from_secs(1)).await;
    }
    anyhow::bail!("Server not ready")
}
pub async fn mint(uid: &str, refs: Vec<String>, ttl: u64) -> Result<String> {
    Ok(ops(true)?
        .tokens
        .mint(dto::IssueTokenRequest {
            uid: uid.into(),
            access_level: 0,
            compartments: vec![],
            scopes: vec!["query".into()],
            runbook_refs: Some(refs),
            ttl_secs: Some(ttl),
        })
        .await?
        .token)
}
pub async fn run(provider: &str, approve: bool) -> Result<()> {
    ensure!(
        approve,
        "Explicit --approve required for isolated verified cutovers"
    );
    assignments(provider)?;
    verify("/inputs")?;
    let model = if provider == "fixture" {
        "engineering-fixture".into()
    } else {
        env::var(format!("{}_MODEL", provider.to_uppercase()))?
    };
    let template = fs::read_to_string("/app/runbooks/review.yaml")?;
    let shape = fs::read_to_string("/app/shapes/documents.yaml")?;
    let namespace = format!(
        "engineering-{}",
        &hash(format!(
            "{}{}{}{}{}",
            fs::read_to_string("/inputs/manifest.json")?,
            template,
            shape,
            provider,
            model
        ))[..12]
    );
    let config = format!("{namespace}-model");
    let api = ops(false)?;
    ready(&api).await?;
    let connection = if provider == "fixture" {
        "endpoint: http://provider-fixture:11434".into()
    } else {
        format!(
            "credentialRef: {{env: {}_API_KEY}}",
            provider.to_uppercase()
        )
    };
    let family = if provider == "fixture" {
        "ollama"
    } else {
        provider
    };
    let encoded = serde_json::to_string(&model)?;
    api.providers.apply_config(&format!("apiVersion: munarium.ioka.io/v1\nkind: ProviderConfig\nmetadata: {{name: {config}}}\nspec:\n  provider: {family}\n  {connection}\n  models: {{complete: [{encoded}], fast: {encoded}}}\n  budgets: {{rpm: 60, dailyTokens: {{fast: 200000}}}}\n")).await?;
    ensure!(
        api.providers.health(&config).await?.healthy,
        "Provider health failed"
    );
    api.runbooks.apply_shape(&shape, None).await?;
    let mut scopes = BTreeMap::new();
    for component in ["billing", "catalog", "security-admin"] {
        let name = format!("{namespace}-{component}");
        let book=template.replace("__NAME__",&name).replace("__PROVIDER__",&config).replace("__LEVEL__",if component=="security-admin"{"2"}else{"0"})+"\n  steps:\n    - resolveSources: {}\n    - buildIndex: {}\n    - verify: {}\n    - cutover: {approval: required}\n";
        api.runbooks.apply_runbook(&book).await?;
        for kind in ["architecture", "release"] {
            let content = if component == "security-admin" {
                b"RESTRICTED_ENGINEERING_SENTINEL. Fictional privileged procedure.".to_vec()
            } else {
                fs::read(format!("/inputs/documents/{component}-{kind}.txt"))?
            };
            let result = api
                .ingest
                .ingest(dto::IngestFileRequest {
                    filename: format!("{name}/{kind}.txt"),
                    media_type: "text/plain".into(),
                    sha256: Some(hash(&content)),
                    content_base64: STANDARD.encode(content),
                    collections: None,
                })
                .await?;
            ensure!(
                result.error.is_none() && result.bound_to == vec![name.clone()],
                "Unexpected ingest binding"
            );
        }
        let run = api.runbooks.run_runbook(&name, None).await?;
        save(
            format!("/work/bootstrap/{}.json", run.run_id),
            &json!({"run":run,"provider":provider,"model":model,"client_revision":REVISION,"component":component}),
        )?;
        let status = api.runbooks.get_run(&run.run_id).await?;
        let pending: Vec<_> = status
            .steps
            .iter()
            .filter(|s| s.state == "awaiting_approval")
            .collect();
        ensure!(
            pending.len() == 1 && pending[0].name == format!("cutover:{name}"),
            "Unexpected cutover"
        );
        api.runbooks
            .approve_step(&run.run_id, pending[0].ordinal)
            .await?;
        ensure!(
            api.runbooks.get_run(&run.run_id).await?.state == "done",
            "Incomplete index run"
        );
        if component != "security-admin" {
            scopes.insert(component.into(), format!("{name}@1"));
        }
    }
    let token = mint(
        "engineering-ci",
        scopes
            .values()
            .map(|r| r.split('@').next().unwrap().into())
            .collect(),
        3600,
    )
    .await?;
    save(
        "/credentials/query.json",
        &Grant {
            token,
            uid: "engineering-ci".into(),
            provider: provider.into(),
            model,
            config,
            scopes,
        },
    )?;
    Ok(())
}
