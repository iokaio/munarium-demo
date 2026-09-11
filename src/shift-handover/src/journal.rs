// SPDX-License-Identifier: Apache-2.0
use crate::{bootstrap::*, data::*};
use anyhow::{ensure, Result};
use munarium_client::{
    dto, ContextQuery, FactsQuery, FindingsQuery, MunariumClient, MunariumError,
};
use serde_json::{json, Value};
use std::{fs, path::Path};
pub struct Lease(fs::File);
impl Lease {
    pub fn new(dir: &str) -> Result<Self> {
        fs::create_dir_all(dir)?;
        let f = fs::OpenOptions::new()
            .create(true)
            .truncate(false)
            .read(true)
            .write(true)
            .open(format!("{dir}/.lock"))?;
        f.try_lock()?;
        Ok(Self(f))
    }
}
impl Drop for Lease {
    fn drop(&mut self) {
        let _ = self.0.unlock();
    }
}
pub fn journal(dir: &str) -> Result<Value> {
    let path = format!("{dir}/journal.json");
    if Path::new(&path).exists() {
        read(path)
    } else {
        Ok(json!({"events":{}}))
    }
}
fn store(dir: &str, j: &Value) -> Result<()> {
    save(format!("{dir}/journal.json"), j)
}
pub async fn scan(input: &str, dir: &str, url: &str, crash: bool) -> Result<()> {
    let _lease = Lease::new(dir)?;
    let rows = events(input)?;
    let catalogue = versions()?;
    let mut j = journal(dir)?;
    // Validate identities and duplicate revisions before writing any item in this scan.
    for e in &rows {
        ensure!(catalogue.contains_key(&e.shift), "Unknown shift scope");
        if let Some(old) = j["events"].get(&e.id) {
            ensure!(
                old["hash"] == hash(serde_json::to_vec(e)?),
                "Changed event ID; submit a new reviewed event ID"
            );
            ensure!(
                old.get("version")
                    .is_none_or(|version| version == &catalogue[&e.shift]),
                "Catalogue version changed; use a new journal directory"
            );
        }
    }
    let c = writer_at(url)?;
    for e in rows {
        if j["events"][&e.id]["status"] == "complete"
            || j["events"][&e.id]["status"] == "review_required"
        {
            continue;
        }
        ensure!(
            j["events"][&e.id]["status"] != "uncertain",
            "Uncertain command must be reconciled before another submission"
        );
        let version = &catalogue[&e.shift];
        let fingerprint = hash(serde_json::to_vec(&e)?);
        if !e.reviewed {
            j["events"][&e.id] = json!({"event":e,"hash":fingerprint,"status":"review_required"});
            store(dir, &j)?;
            continue;
        }
        let mut history = j["events"][&e.id]["attempts"]
            .as_array()
            .cloned()
            .unwrap_or_default();
        for _ in 0..3 {
            let head = c.query.head(version).await?;
            let key = uuid::Uuid::new_v4().to_string();
            let evidence = json!({"shift_event":e.id,"event_hash":fingerprint,"command_key":key,"reviewer":e.reviewer});
            let body = match e.kind.as_str() {
                "fact" | "update" => {
                    let supersedes = if e.kind == "update" {
                        let current = c
                            .query
                            .facts(
                                version,
                                FactsQuery {
                                    as_of_seq: Some(head),
                                    ..Default::default()
                                },
                            )
                            .await?
                            .facts
                            .into_iter()
                            .filter(|f| f.subject == e.subject && f.key == e.key)
                            .collect::<Vec<_>>();
                        ensure!(
                            current.len() == 1,
                            "Update needs exactly one current reviewed fact"
                        );
                        Some(current[0].id.clone())
                    } else {
                        None
                    };
                    json!({"expected_head":head,"claim_type":e.kind,"subject":e.subject,"key":e.key,"value":e.value,"scope_path":e.shift,"provenance":"witnessed","supersedes_id":supersedes,"evidence":evidence,"shape_ref":"shift-observation@1"})
                }
                "anchor" => {
                    json!({"subject":e.subject,"key":e.key,"value":e.value,"scope_path":e.shift,"evidence":evidence})
                }
                "promise" => {
                    json!({"key":e.key,"kind":"handover","description":e.value,"origin_scope":e.shift,"due_scope":format!("{}.next",e.shift)})
                }
                "fulfill" => json!({"key":e.key}),
                _ => unreachable!(),
            };
            j["events"][&e.id] = json!({"event":e,"hash":fingerprint,"version":version,"status":"uncertain","idempotency_key":key,"body":body,"attempts":history});
            store(dir, &j)?;
            match dispatch(&c, version, &e.kind, &body, &key).await {
                Ok(response) => {
                    if crash {
                        std::process::exit(71);
                    }
                    let missing_promise = e.kind == "fulfill" && response["fulfilled"] != true;
                    j["events"][&e.id]["response"] = response;
                    if missing_promise {
                        j["events"][&e.id]["status"] = json!("review_required");
                        store(dir, &j)?;
                        anyhow::bail!("Promise was not fulfilled; operator review required");
                    }
                    j["events"][&e.id]["status"] = json!("complete");
                    store(dir, &j)?;
                    break;
                }
                Err(MunariumError::HeadConflict { expected, actual }) => {
                    history
                        .push(json!({"key":key,"body":body,"expected":expected,"actual":actual}));
                    j["events"][&e.id]["attempts"] = json!(history);
                    j["events"][&e.id]["status"] = json!("head_conflict");
                    store(dir, &j)?;
                }
                Err(error) => return Err(error.into()),
            }
        }
        ensure!(
            j["events"][&e.id]["status"] == "complete",
            "Head conflicts require operator review"
        );
    }
    Ok(())
}
async fn dispatch(
    c: &MunariumClient,
    version: &str,
    kind: &str,
    body: &Value,
    key: &str,
) -> std::result::Result<Value, MunariumError> {
    let parse = |e: serde_json::Error| MunariumError::InvalidInput {
        detail: e.to_string(),
    };
    match kind {
        "fact" | "update" => Ok(serde_json::to_value(
            c.commands
                .propose_claim(
                    version,
                    serde_json::from_value(body.clone()).map_err(parse)?,
                    Some(key.into()),
                )
                .await?,
        )
        .unwrap()),
        "anchor" => Ok(serde_json::to_value(
            c.commands
                .lock_anchor(
                    version,
                    serde_json::from_value(body.clone()).map_err(parse)?,
                    Some(key.into()),
                )
                .await?,
        )
        .unwrap()),
        "promise" => Ok(serde_json::to_value(
            c.commands
                .open_promise(
                    version,
                    serde_json::from_value(body.clone()).map_err(parse)?,
                    Some(key.into()),
                )
                .await?,
        )
        .unwrap()),
        "fulfill" => Ok(serde_json::to_value(
            c.commands
                .fulfill_promise(version, body["key"].as_str().unwrap(), Some(key.into()))
                .await?,
        )
        .unwrap()),
        _ => Err(MunariumError::InvalidInput {
            detail: "Unknown command kind".into(),
        }),
    }
}
pub async fn reconcile(dir: &str, id: &str) -> Result<()> {
    let _lease = Lease::new(dir)?;
    let mut j = journal(dir)?;
    let entry = &j["events"][id];
    ensure!(
        entry["status"] == "uncertain",
        "Only uncertain commands need reconciliation"
    );
    let version = entry["version"].as_str().unwrap();
    let kind = entry["event"]["kind"].as_str().unwrap();
    let body = &entry["body"];
    let c = reader()?;
    // Facts have an immutable command marker. Other command kinds require explicit operator investigation;
    // matching a promise's text or an anchor's value alone cannot prove which writer created it.
    ensure!(
        matches!(kind, "fact" | "update"),
        "Uncertain non-claim command needs operator investigation; no replay"
    );
    let found = c
        .query
        .facts(
            version,
            FactsQuery {
                statuses: vec![dto::ClaimStatusDto::Accepted, dto::ClaimStatusDto::Disputed],
                ..Default::default()
            },
        )
        .await?
        .facts
        .into_iter()
        .filter(|f| {
            f.evidence.as_ref() == Some(&body["evidence"])
                && f.subject == body["subject"]
                && f.key == body["key"]
                && f.value == body["value"]
        })
        .collect::<Vec<_>>();
    if found.len() != 1 {
        return Ok(());
    }
    let claim = &found[0];
    // Findings require trusted read access on Server 1.1.1; the writer retrieves them for this explicit recovery command.
    let findings = writer_at(&endpoint())?
        .query
        .findings(version, FindingsQuery::default())
        .await?
        .findings
        .into_iter()
        .filter(|f| f.seq == claim.seq)
        .map(|f| f.finding)
        .collect::<Vec<_>>();
    j["events"][id]["response"] = json!({"claim":claim,"head_seq":claim.seq,"findings":findings});
    j["events"][id]["status"] = json!("complete");
    j["events"][id]["recovered"] = json!(true);
    store(dir, &j)
}
pub async fn close_shift(dir: &str, shift: &str) -> Result<()> {
    let _lease = Lease::new(dir)?;
    let versions = versions()?;
    let version = versions
        .get(shift)
        .ok_or_else(|| anyhow::anyhow!("Unknown shift"))?;
    let j = journal(dir)?;
    ensure!(
        j["events"]
            .as_object()
            .unwrap()
            .values()
            .filter(|e| e["event"]["shift"] == shift)
            .all(|e| e["status"] == "complete" || e["status"] == "review_required"),
        "Cannot pin uncertain work"
    );
    let path = format!("{dir}/close-{shift}.json");
    if Path::new(&path).exists() {
        ensure!(read(&path)?["version"] == *version, "Catalogue changed");
        return Ok(());
    }
    let head = reader()?.query.head(version).await?;
    ensure!(head > 0, "Cannot close an empty shift");
    save(
        path,
        &json!({"shift":shift,"version":version,"as_of_seq":head,"journal_hash":hash(serde_json::to_vec(&j)?)}),
    )
}
pub async fn brief(dir: &str, shift: &str, historical: bool, budget: u64) -> Result<Value> {
    ensure!(
        (32..=4000).contains(&budget),
        "Context budget must be 32..4000"
    );
    let versions = versions()?;
    let version = versions
        .get(shift)
        .ok_or_else(|| anyhow::anyhow!("Unknown shift"))?;
    let pin = if historical {
        let saved = read(format!("{dir}/close-{shift}.json"))?;
        ensure!(saved["version"] == *version, "Pinned version mismatch");
        Some(saved["as_of_seq"].as_u64().unwrap())
    } else {
        Some(reader()?.query.head(version).await?)
    };
    ensure!(pin.unwrap() > 0, "No ledger events yet");
    let c = reader()?;
    let facts = c
        .query
        .facts(
            version,
            FactsQuery {
                as_of_seq: pin,
                ..Default::default()
            },
        )
        .await?;
    let anchors = c.query.anchors(version, pin).await?;
    let promises = c.query.promises(version, pin, None).await?;
    let context = c
        .query
        .compose_context(
            version,
            ContextQuery {
                as_of_seq: pin,
                budget_tokens: Some(budget),
                ..Default::default()
            },
        )
        .await?;
    let result = json!({"shift":shift,"version":version,"as_of_seq":pin,"historical":historical,"facts":facts,"anchors":anchors,"promises":promises,"context":context,"model_calls":0,"requested_budget":budget,"budget_exceeded":context.estimated_tokens>budget});
    let label = if historical { "historical" } else { "current" };
    save(format!("{dir}/{label}-{shift}.json"), &result)?;
    write(format!("{dir}/{label}-{shift}.txt"),&format!("SHIFT HANDOVER | fictional office journal\nShift: {shift} | view: {label} | pin: {}\nVersion: {version}\nContext budget: {budget} | estimated tokens: {} | exceeded: {}\n\n{}\n",pin.unwrap(),context.estimated_tokens,context.estimated_tokens>budget,context.text))?;
    Ok(result)
}
