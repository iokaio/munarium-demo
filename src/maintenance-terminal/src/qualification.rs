// SPDX-License-Identifier: Apache-2.0
use crate::{bootstrap::*, data::*, terminal::*};
use anyhow::{ensure, Result};
use serde_json::{json, Value};
use std::{
    env, fs,
    time::{Duration, Instant},
};
async fn control(path: &str) -> Result<Value> {
    Ok(
        reqwest::get(format!("http://provider-fixture:11434/{path}"))
            .await?
            .error_for_status()?
            .json()
            .await?,
    )
}
async fn calls() -> Result<u64> {
    Ok(control("calls").await?["calls"].as_u64().unwrap())
}
fn xml(s: &str) -> String {
    s.replace('&', "&amp;")
        .replace('<', "&lt;")
        .replace('>', "&gt;")
        .replace('"', "&quot;")
}
fn report(dir: &str, rows: &[Value]) -> Result<()> {
    let failures = rows.iter().filter(|r| r["passed"] != true).count();
    save(
        format!("{dir}/quality.json"),
        &json!({"tests":rows.len(),"passed":rows.len()-failures,"failed":failures,"skipped":0,"cases":rows,"client_revision":REVISION,"threshold":"Every assigned case and recovery assertion must pass; all citations resolve; exact oracle status/code/revision; exact provider/model."}),
    )?;
    let mut out = format!(
        "<testsuite name=\"maintenance\" tests=\"{}\" failures=\"{failures}\" skipped=\"0\">",
        rows.len()
    );
    for row in rows {
        out.push_str(&format!(
            "<testcase name=\"{}\" time=\"{}\">",
            xml(row["name"].as_str().unwrap()),
            row["seconds"]
        ));
        if row["passed"] != true {
            out.push_str(&format!(
                "<failure>{}</failure>",
                xml(row["error"].as_str().unwrap_or("failed"))
            ));
        }
        out.push_str("</testcase>");
    }
    out.push_str("</testsuite>");
    fs::write(format!("{dir}/tests.xml"), out)?;
    Ok(())
}
async fn business(g: &Grant, a: &Asset, dir: &str, expected: &Value) -> Result<Value> {
    let before = if g.provider == "fixture" {
        Some(calls().await?)
    } else {
        None
    };
    find(g, a, dir).await?;
    if let Some(before) = before {
        ensure!(calls().await? == before, "Retrieval called provider");
    }
    let response = explain(g, a, dir, None).await?;
    let answer = validate_answer(&response, g, a)?;
    for field in ["status", "code", "revision"] {
        ensure!(answer[field] == expected[field], "Oracle mismatch: {field}");
    }
    let session = client(&g.token, &g.uid)?
        .sessions
        .get(response["session_id"].as_str().unwrap())
        .await?;
    save(format!("{dir}/transcript.json"), &session)?;
    ensure!(session.turns.len() == 1, "Unexpected completion turn count");
    ensure!(
        session.turns[0].completion.as_ref().unwrap()["resolved"]["provider"] == g.config,
        "Wrong named provider route"
    );
    let completion = &response["completion"];
    ensure!(
        completion["input_tokens"].as_u64().unwrap_or(0) > 0
            && completion["output_tokens"].as_u64().unwrap_or(0) > 0,
        "Missing token usage"
    );
    ensure!(
        !read(format!("{dir}/progress.json"))?
            .as_array()
            .unwrap()
            .is_empty(),
        "Missing streaming progress"
    );
    Ok(
        json!({"provider":g.provider,"model":g.model,"input_tokens":completion["input_tokens"],"output_tokens":completion["output_tokens"],"answer":answer}),
    )
}
pub async fn run(kind: &str, dir: &str) -> Result<()> {
    let g = grant()?;
    let assets = catalogue("/inputs")?;
    ready(&ops(false)?).await?;
    fs::create_dir_all(dir)?;
    save(
        format!("{dir}/manifest.json"),
        &read("/inputs/manifest.json")?,
    )?;
    let mut rows = vec![];
    macro_rules! check { ($name:expr,$body:expr) => {{
        let now=Instant::now(); let result:Result<Value>=$body.await;
        let row=match result {Ok(details)=>json!({"name":$name,"passed":true,"seconds":now.elapsed().as_secs_f64(),"details":details}),Err(e)=>json!({"name":$name,"passed":false,"seconds":now.elapsed().as_secs_f64(),"error":format!("{e:#}")})};
        println!("{} {} {}",if row["passed"]==true {"PASS"} else {"FAIL"},$name,row.get("error").unwrap_or(&Value::Null)); rows.push(row); report(dir,&rows)?;
    }}; }
    if kind == "restarted" {
        check!("server-restart-and-export-recovery", async {
            let checkpoint = read("/work/restart.json")?;
            let path = checkpoint["path"].as_str().unwrap();
            let a = &assets[0];
            let before = calls().await?;
            fs::remove_file(format!("{path}/answer.txt"))?;
            explain(&g, a, path, None).await?;
            ensure!(
                fs::read_to_string(format!("{path}/answer.txt"))?.contains("BLUE-17"),
                "Export not restored"
            );
            ensure!(calls().await? == before, "Restart repeated completion");
            let transcript = client(&g.token, &g.uid)?
                .sessions
                .get(
                    read(format!("{path}/intent.json"))?["session_id"]
                        .as_str()
                        .unwrap(),
                )
                .await?;
            ensure!(transcript.turns.len() == 1, "Duplicate turn after restart");
            Ok(json!({"turns":1}))
        });
    } else {
        let provider = kind.strip_prefix("cloud-").unwrap_or("fixture");
        ensure!(g.provider == provider, "Bootstrap provider mismatch");
        let expected = read("/oracle/expected.json")?;
        for id in assignments(provider)? {
            let a = assets.iter().find(|a| a.id == id).unwrap();
            if provider == "openrouter" {
                let delay = env::var("MAINTENANCE_OPENROUTER_CASE_DELAY_SECONDS")
                    .unwrap_or("60".into())
                    .parse::<u64>()?;
                println!("Pacing independent OpenRouter case by {delay}s");
                tokio::time::sleep(Duration::from_secs(delay)).await;
            }
            check!(id, business(&g, a, &format!("{dir}/{id}"), &expected[id]));
        }
        if provider == "fixture" {
            check!("unsupported-asset-and-revision", async {
                let before = calls().await?;
                ensure!(
                    select(&assets, "UNKNOWN", "R2").is_err()
                        && select(&assets, "SIM-100", "R9").is_err(),
                    "Unsupported selection accepted"
                );
                ensure!(
                    calls().await? == before,
                    "Unsupported selection called provider"
                );
                Ok(json!({}))
            });
            check!("duplicate-explain-restores-export", async {
                let before = calls().await?;
                let path = format!("{dir}/case-001");
                fs::remove_file(format!("{path}/answer.txt"))?;
                explain(&g, &assets[0], &path, None).await?;
                ensure!(calls().await? == before, "Duplicate completion");
                save("/work/restart.json", &json!({"path":path}))?;
                Ok(json!({}))
            });
            check!("retrieval-during-provider-outage", async {
                control("fail").await?;
                let before = calls().await?;
                let result = find(&g, &assets[0], &format!("{dir}/outage-find")).await;
                let after = calls().await?;
                control("reset").await?;
                result?;
                ensure!(
                    before == after,
                    "Retrieval-only preset called unavailable provider"
                );
                Ok(json!({"provider_calls":0}))
            });
            check!("completion-outage-stays-uncertain", async {
                let path = format!("{dir}/outage-explain");
                find(&g, &assets[0], &path).await?;
                control("fail").await?;
                let result = explain(&g, &assets[0], &path, None).await;
                control("reset").await?;
                ensure!(result.is_err(), "Provider outage completed");
                ensure!(
                    !std::path::Path::new(&format!("{path}/answer.txt")).exists(),
                    "Unverified export"
                );
                let before = calls().await?;
                ensure!(
                    explain(&g, &assets[0], &path, None).await.is_err(),
                    "Empty transcript was accepted"
                );
                ensure!(calls().await? == before, "Uncertain intent resubmitted");
                Ok(json!({}))
            });
            check!("capability-scope-denied", async {
                let token = mint(
                    "restricted-reader",
                    vec![g.scopes[&assets[0].id]
                        .retrieve
                        .split('@')
                        .next()
                        .unwrap()
                        .into()],
                    3600,
                )
                .await?;
                let c = client(&token, "restricted-reader")?;
                let before = calls().await?;
                let error = c
                    .sessions
                    .create(&g.scopes[&assets[1].id].retrieve)
                    .await
                    .unwrap_err();
                ensure!(
                    matches!(error, munarium_client::MunariumError::Forbidden { .. }),
                    "Expected typed Forbidden: {error}"
                );
                ensure!(calls().await? == before, "Denied access called provider");
                Ok(json!({}))
            });
            check!("expired-capability-and-same-uid-refresh", async {
                let reference = &g.scopes[&assets[0].id].retrieve;
                let refs = vec![reference.split('@').next().unwrap().into()];
                let token = mint("expiry-reader", refs.clone(), 1).await?;
                let c = client(&token, "expiry-reader")?;
                let session = c.sessions.create(reference).await?;
                tokio::time::sleep(Duration::from_secs(33)).await;
                let error = c
                    .sessions
                    .turn(&session.session_id, request(&assets[0], false))
                    .await
                    .unwrap_err();
                ensure!(
                    matches!(
                        error,
                        munarium_client::MunariumError::Unauthenticated { .. }
                    ),
                    "Expected typed Unauthenticated: {error}"
                );
                let fresh = mint("expiry-reader", refs, 3600).await?;
                let refreshed = client(&fresh, "expiry-reader")?
                    .sessions
                    .turn(&session.session_id, request(&assets[0], false))
                    .await?;
                ensure!(!refreshed.hits.is_empty(), "Refresh lost evidence");
                Ok(json!({"wait_seconds":33,"same_session":true}))
            });
            check!("lost-stream-reconciles-without-resubmit", async {
                let path = format!("{dir}/lost-stream");
                find(&g, &assets[0], &path).await?;
                control("drop").await?;
                let result =
                    explain(&g, &assets[0], &path, Some("http://provider-fixture:11435")).await;
                ensure!(result.is_err(), "Fault proxy did not lose terminal event");
                let before = calls().await?;
                let recovered = explain(&g, &assets[0], &path, None).await?;
                ensure!(
                    recovered["recovered"] == true && recovered.get("skipped").is_none(),
                    "Recovery invented live metadata"
                );
                ensure!(calls().await? == before, "Recovery repeated completion");
                Ok(json!({"provider_calls_on_recovery":0}))
            });
            check!("interactive-source-and-stale-selection", async {
                use tokio::{io::AsyncWriteExt, process::Command};
                let mut child = Command::new(std::env::current_exe()?)
                    .args(["tui", &format!("{dir}/ui")])
                    .stdin(std::process::Stdio::piped())
                    .stdout(std::process::Stdio::piped())
                    .spawn()?;
                child
                    .stdin
                    .take()
                    .unwrap()
                    .write_all(
                        b"find SIM-100 R1\nsource 1\nexport\nfind UNKNOWN R2\nexplain\nquit\n",
                    )
                    .await?;
                let output = child.wait_with_output().await?;
                let text = String::from_utf8(output.stdout)?;
                fs::write(format!("{dir}/terminal-session.txt"), &text)?;
                ensure!(
                    output.status.success()
                        && text.contains("HISTORICAL")
                        && text.contains("source_content_hash")
                        && text.contains("Unsupported asset/revision")
                        && text.contains("Find a revision first"),
                    "Terminal interaction failed"
                );
                Ok(json!({}))
            });
        }
    }
    report(dir, &rows)?;
    ensure!(
        rows.iter().all(|r| r["passed"] == true),
        "Qualification failed; preserved reports at {dir}"
    );
    Ok(())
}
