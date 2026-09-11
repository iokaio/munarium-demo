// SPDX-License-Identifier: Apache-2.0
use crate::data::*;
use anyhow::{ensure, Result};
use base64::{engine::general_purpose::STANDARD, Engine};
use munarium_client::{dto, MunariumClient, MunariumClientOptions};
use serde::{Deserialize, Serialize};
use serde_json::json;
use std::{collections::BTreeMap, env, fs, time::Duration};

#[derive(Clone, Serialize, Deserialize)]
pub struct Scope {
    pub retrieve: String,
    pub explain: String,
    pub collection: String,
}
#[derive(Clone, Serialize, Deserialize)]
pub struct Grant {
    pub token: String,
    pub uid: String,
    pub provider: String,
    pub model: String,
    pub config: String,
    pub scopes: BTreeMap<String, Scope>,
}
pub fn endpoint() -> String {
    env::var("MUNARIUM_REST_URL").unwrap_or("http://server:8080".into())
}
pub fn client(token: &str, uid: &str) -> Result<MunariumClient> {
    client_at(&endpoint(), token, uid)
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
    client(
        &env::var(if management {
            "MUNARIUM_MGMT_TOKEN"
        } else {
            "MUNARIUM_TOKEN"
        })?,
        "maintenance-bootstrap",
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
        tokio::time::sleep(Duration::from_secs(2)).await;
    }
    anyhow::bail!("Server did not become ready")
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
        "Bootstrap requires --approve for this isolated demo's cutovers"
    );
    assignments(provider)?;
    verify("/inputs")?;
    ensure!(
        provider == "fixture" || read("/inputs/manifest.json")?["profile"] == "default",
        "Online qualification requires default fixtures"
    );
    let model = if provider == "fixture" {
        "maintenance-selected".into()
    } else {
        env::var(format!("{}_MODEL", provider.to_uppercase()))?
    };
    ensure!(!model.trim().is_empty(), "Model is required");
    let template = fs::read_to_string("runbooks/maintenance.yaml")?;
    let shape = fs::read_to_string("shapes/documents.yaml")?;
    let completion = fs::read_to_string("runbooks/completion.yaml")?;
    let namespace = format!(
        "maint-{}",
        &hash(format!(
            "{}{}{}{}{}{}{}",
            fs::read_to_string("/inputs/manifest.json")?,
            template,
            shape,
            completion,
            REVISION,
            provider,
            model
        ))[..12]
    );
    let config = format!("{namespace}-model");
    let c = ops(false)?;
    ready(&c).await?;
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
    let model_json = serde_json::to_string(&model)?;
    let rpm = if provider == "fixture" {
        (catalogue("/inputs")?.len() * 3).max(30)
    } else {
        30
    };
    c.providers.apply_config(&format!("apiVersion: munarium.ioka.io/v1\nkind: ProviderConfig\nmetadata: {{name: {config}}}\nspec:\n  provider: {family}\n  {connection}\n  models:\n    complete: [{model_json}]\n    fast: {model_json}\n  budgets: {{rpm: {rpm}, dailyTokens: {{fast: 100000}}}}\n")).await?;
    ensure!(
        c.providers.health(&config).await?.healthy,
        "Named provider health failed: {provider}"
    );
    c.runbooks.apply_shape(&shape, None).await?;
    let mut scopes = BTreeMap::new();
    for asset in catalogue("/inputs")? {
        let collection = format!("{namespace}-{}", asset.id);
        let retrieve = format!("{collection}-find");
        let explain = format!("{collection}-explain");
        for (name, completion) in [
            (&retrieve, String::new()),
            (&explain, completion.replace("__PROVIDER__", &config)),
        ] {
            c.runbooks
                .apply_runbook(
                    &template
                        .replace("__NAME__", name)
                        .replace("__COLLECTION__", &collection)
                        .replace("__COMPLETION__", &completion),
                )
                .await?;
        }
        for kind in ["manual", "inspection", "revision"] {
            let content = fs::read(format!("/inputs/documents/{}-{kind}.txt", asset.id))?;
            let result = c
                .ingest
                .ingest(dto::IngestFileRequest {
                    filename: format!("{collection}/{kind}.txt"),
                    media_type: "text/plain".into(),
                    content_base64: STANDARD.encode(content),
                    sha256: None,
                    collections: None,
                })
                .await?;
            ensure!(
                result.error.is_none() && result.bound_to.contains(&collection),
                "Unexpected ingest binding"
            );
        }
        let run = c.runbooks.run_runbook(&retrieve, None).await?;
        save(
            format!("/work/bootstrap/{provider}-{}.json", run.run_id),
            &json!({"run_id":run.run_id,"collection":collection,"manifest":read("/inputs/manifest.json")?,"client_revision":REVISION,"provider":provider,"model":model}),
        )?;
        let state = c.runbooks.get_run(&run.run_id).await?;
        let pending: Vec<_> = state
            .steps
            .iter()
            .filter(|s| s.state == "awaiting_approval")
            .collect();
        ensure!(
            pending.len() == 1 && pending[0].name == format!("cutover:{collection}"),
            "Unexpected cutover"
        );
        c.runbooks
            .approve_step(&run.run_id, pending[0].ordinal)
            .await?;
        ensure!(
            c.runbooks.get_run(&run.run_id).await?.state == "done",
            "Incomplete index run"
        );
        scopes.insert(
            asset.id,
            Scope {
                retrieve: format!("{retrieve}@1"),
                explain: format!("{explain}@1"),
                collection,
            },
        );
    }
    let refs = scopes
        .values()
        .flat_map(|s| {
            [
                s.retrieve.split('@').next().unwrap().into(),
                s.explain.split('@').next().unwrap().into(),
            ]
        })
        .collect();
    let token = mint("maintenance-reader", refs, 3600).await?;
    save(
        "/credentials/query.json",
        &Grant {
            token,
            uid: "maintenance-reader".into(),
            provider: provider.into(),
            model,
            config,
            scopes,
        },
    )?;
    println!("Ready: {provider}; selected asset/revision scopes; query capability issued.");
    Ok(())
}
