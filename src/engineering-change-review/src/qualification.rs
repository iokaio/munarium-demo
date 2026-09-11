// SPDX-License-Identifier: Apache-2.0
use crate::{bootstrap::*, data::*, review};
use anyhow::{ensure, Result};
use serde_json::{json, Value};
use std::{
    fs,
    path::Path,
    time::{Duration, Instant},
};
async fn get(url: &str) -> Result<Value> {
    Ok(reqwest::get(url).await?.error_for_status()?.json().await?)
}
async fn mode(value: &str) -> Result<()> {
    get(&format!("http://provider-fixture:11434/mode/{value}")).await?;
    Ok(())
}
async fn fault(value: &str) -> Result<()> {
    get(&format!("http://faults:11435/control/{value}")).await?;
    Ok(())
}
async fn calls() -> Result<u64> {
    Ok(get("http://provider-fixture:11434/calls").await?["calls"]
        .as_u64()
        .unwrap())
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
        &json!({"tests":rows.len(),"failed":failures,"passed":rows.len()-failures,"skipped":0,"cases":rows,"client_revision":REVISION}),
    )?;
    let mut text = format!(
        "<testsuite name=\"engineering\" tests=\"{}\" failures=\"{failures}\" skipped=\"0\">",
        rows.len()
    );
    for row in rows {
        text.push_str(&format!(
            "<testcase name=\"{}\" time=\"{}\">",
            xml(row["name"].as_str().unwrap()),
            row["seconds"]
        ));
        if row["passed"] != true {
            text.push_str(&format!(
                "<failure>{}</failure>",
                xml(row["error"].as_str().unwrap_or("failed"))
            ));
        }
        text.push_str("</testcase>");
    }
    text.push_str("</testsuite>");
    write(format!("{dir}/tests.xml"), text.as_bytes())
}
async fn child(args: &[&str]) -> Result<std::process::Output> {
    Ok(tokio::process::Command::new(std::env::current_exe()?)
        .args(args)
        .output()
        .await?)
}
async fn business(dir: &str, n: usize, expected: &Value) -> Result<Value> {
    let id = format!("case-{n:03}");
    let input = format!("/inputs/changes/{id}");
    let component = expected["component"].as_str().unwrap();
    let path = format!("{dir}/{id}");
    ensure!(
        !Path::new(&format!("{path}/journal.json")).exists(),
        "Cloud case reused prior work"
    );
    let packet = review::run(&path, &input, component, false, false, None).await?;
    ensure!(
        packet["analysis_status"] == "verified",
        "Analysis failed: {}",
        packet["error"]
    );
    ensure!(
        packet["suggestion"]["finding"] == expected["finding"]
            && packet["suggestion"]["requirement_code"] == expected["code"]
            && packet["deterministic_failures"] == expected["deterministic"],
        "Independent oracle mismatch"
    );
    ensure!(
        packet["advisory_only"] == true
            && !serde_json::to_string(&packet)?.contains("RESTRICTED_ENGINEERING_SENTINEL"),
        "Privileged evidence leaked"
    );
    let output = child(&["review", &path, &input, component]).await?;
    ensure!(
        output.status.code() == Some(review::exit_code(&packet)),
        "CI executable returned the wrong status"
    );
    ensure!(
        read(format!("{path}/review.json"))? == packet,
        "Saved packet changed on duplicate invocation"
    );
    Ok(
        json!({"case":id,"exit_code":packet["exit_code"],"completion":packet["evidence"]["completion"],"input_hash":packet["input_hash"]}),
    )
}
pub async fn run(kind: &str, dir: &str) -> Result<()> {
    let g = grant()?;
    ready(&ops(false)?).await?;
    fs::create_dir_all(dir)?;
    save(
        format!("{dir}/manifest.json"),
        &read("/inputs/manifest.json")?,
    )?;
    let mut rows = vec![];
    macro_rules! check {($name:expr,$body:expr)=>{{let now=Instant::now();let outcome:Result<Value>=$body.await;let row=match outcome{Ok(value)=>json!({"name":$name,"passed":true,"seconds":now.elapsed().as_secs_f64(),"details":value}),Err(error)=>json!({"name":$name,"passed":false,"seconds":now.elapsed().as_secs_f64(),"error":format!("{error:#}")})};println!("{} {} {}",if row["passed"]==true{"PASS"}else{"FAIL"},$name,row.get("error").unwrap_or(&Value::Null));rows.push(row);report(dir,&rows)?;}};}
    if kind == "restarted" {
        check!("crash-recovery-after-server-restart", async {
            let root = Path::new(dir)
                .parent()
                .unwrap()
                .join("controlled/restart.json");
            let saved = read(root)?;
            let before = calls().await?;
            let packet = review::run(
                saved["path"].as_str().unwrap(),
                "/inputs/changes/case-001",
                "billing",
                true,
                false,
                None,
            )
            .await?;
            ensure!(
                packet["analysis_status"] == "verified" && packet["recovered"] == true,
                "Crash transcript not recovered"
            );
            ensure!(
                packet["evidence"].get("skipped").is_none(),
                "Recovery fabricated live skipped fields"
            );
            ensure!(calls().await? == before, "Recovery resubmitted a paid turn");
            Ok(json!({"new_provider_calls":0}))
        });
    } else {
        let provider = kind.strip_prefix("cloud-").unwrap_or("fixture");
        ensure!(provider == g.provider, "Bootstrap provider mismatch");
        save(
            format!("{dir}/usage-before.json"),
            &ops(true)?.reports.usage(Default::default()).await?,
        )?;
        let expected = read("/oracle/expected.json")?;
        let cases = if provider == "fixture" {
            expected
                .as_object()
                .unwrap()
                .keys()
                .map(|id| id[5..].parse::<usize>())
                .collect::<std::result::Result<Vec<_>, _>>()?
        } else {
            assignments(provider)?
        };
        for n in cases {
            if provider == "openrouter" {
                println!("Pacing independent OpenRouter case for 60 seconds");
                tokio::time::sleep(Duration::from_secs(60)).await;
            }
            let id = format!("case-{n:03}");
            check!(&id, business(dir, n, &expected[&id]));
        }
        if provider == "fixture" {
            check!("untrusted-component-cannot-select-admin", async {
                let before = calls().await?;
                ensure!(
                    review::run(
                        &format!("{dir}/denied"),
                        "/inputs/changes/case-001",
                        "security-admin",
                        false,
                        false,
                        None
                    )
                    .await
                    .is_err(),
                    "Unknown component was accepted"
                );
                ensure!(
                    calls().await? == before,
                    "Unknown component called a provider"
                );
                Ok(json!({}))
            });
            check!("query-credential-cannot-administer", async {
                let api = client_at(&endpoint(), &g.token, &g.uid)?;
                let error = api
                    .runbooks
                    .apply_shape(&fs::read_to_string("/app/shapes/documents.yaml")?, None)
                    .await
                    .unwrap_err();
                ensure!(
                    matches!(error, munarium_client::MunariumError::Forbidden { .. }),
                    "Wrong admin denial"
                );
                Ok(json!({}))
            });
            check!("restricted-runbook-and-wrong-uid-denied", async {
                let admin = format!("{}-security-admin@1", g.config.trim_end_matches("-model"));
                let error = client_at(&endpoint(), &g.token, &g.uid)?
                    .sessions
                    .create(&admin)
                    .await
                    .unwrap_err();
                ensure!(
                    matches!(error, munarium_client::MunariumError::Forbidden { .. }),
                    "Privileged runbook was exposed"
                );
                let error = client_at(&endpoint(), &g.token, "wrong-ci-uid")?
                    .sessions
                    .create(&g.scopes["billing"])
                    .await
                    .unwrap_err();
                ensure!(
                    matches!(
                        error,
                        munarium_client::MunariumError::Forbidden { .. }
                            | munarium_client::MunariumError::Unauthenticated { .. }
                    ),
                    "Wrong identity was accepted"
                );
                Ok(json!({}))
            });
            check!("expired-capability", async {
                let token = mint(
                    "expiry-ci",
                    vec![g.scopes["billing"].split('@').next().unwrap().into()],
                    1,
                )
                .await?;
                tokio::time::sleep(Duration::from_secs(33)).await;
                let error = client_at(&endpoint(), &token, "expiry-ci")?
                    .sessions
                    .create(&g.scopes["billing"])
                    .await
                    .unwrap_err();
                ensure!(
                    matches!(
                        error,
                        munarium_client::MunariumError::Unauthenticated { .. }
                    ),
                    "Expired capability accepted"
                );
                Ok(json!({}))
            });
            check!("lost-turn-recovery-without-replay", async {
                let path = format!("{dir}/lost");
                fault("drop-turn").await?;
                let first = review::run(
                    &path,
                    "/inputs/changes/case-001",
                    "billing",
                    false,
                    false,
                    Some("http://faults:11435"),
                )
                .await;
                fault("normal").await?;
                ensure!(
                    first?["analysis_status"] == "unavailable",
                    "Lost response accepted"
                );
                let before = calls().await?;
                let uncertain = review::run(
                    &path,
                    "/inputs/changes/case-001",
                    "billing",
                    false,
                    false,
                    None,
                )
                .await?;
                ensure!(uncertain["exit_code"] == 3, "Uncertain work passed CI");
                let recovered = review::run(
                    &path,
                    "/inputs/changes/case-001",
                    "billing",
                    true,
                    false,
                    None,
                )
                .await?;
                ensure!(
                    recovered["analysis_status"] == "verified" && recovered["recovered"] == true,
                    "Recovery failed"
                );
                ensure!(calls().await? == before, "Recovery resubmitted");
                Ok(json!({}))
            });
            check!("provider-outage-remains-unavailable", async {
                let path = format!("{dir}/outage");
                mode("unavailable").await?;
                let result = review::run(
                    &path,
                    "/inputs/changes/case-001",
                    "billing",
                    false,
                    false,
                    None,
                )
                .await;
                mode("ok").await?;
                ensure!(result?["exit_code"] == 3, "Outage passed CI");
                let before = calls().await?;
                let recovered = review::run(
                    &path,
                    "/inputs/changes/case-001",
                    "billing",
                    true,
                    false,
                    None,
                )
                .await?;
                ensure!(
                    recovered["analysis_status"] == "unavailable" && calls().await? == before,
                    "Empty transcript replayed or accepted"
                );
                Ok(json!({}))
            });
            check!("unserved-citation-is-unverified", async {
                mode("bad-citation").await?;
                let result = review::run(
                    &format!("{dir}/bad-citation"),
                    "/inputs/changes/case-001",
                    "billing",
                    false,
                    false,
                    None,
                )
                .await;
                mode("ok").await?;
                ensure!(
                    result?["analysis_status"] == "unverified",
                    "Ungrounded finding accepted"
                );
                Ok(json!({}))
            });
            check!("same-diff-different-trusted-component", async {
                let source = read(format!("{dir}/case-006/review.json"))?;
                let alternate = review::run(
                    &format!("{dir}/alternate"),
                    "/inputs/changes/case-006",
                    "billing",
                    false,
                    false,
                    None,
                )
                .await?;
                ensure!(
                    alternate["analysis_status"] == "verified"
                        && alternate["suggestion"]["requirement_code"]
                            == expected["case-001"]["code"],
                    "Trusted scope not applied"
                );
                ensure!(
                    alternate["input_hash"] == source["input_hash"]
                        && alternate["runbook"] != source["runbook"],
                    "Same diff was not independently scoped"
                );
                ensure!(
                    !serde_json::to_string(&alternate)?.contains("RESTRICTED_ENGINEERING_SENTINEL"),
                    "Injected instruction widened access"
                );
                ensure!(
                    review::run(
                        &format!("{dir}/case-006"),
                        "/inputs/changes/case-006",
                        "billing",
                        false,
                        false,
                        None
                    )
                    .await
                    .is_err(),
                    "Changed scope reused journal"
                );
                Ok(json!({}))
            });
            check!("exclusive-work-directory", async {
                let path = format!("{dir}/leased");
                let _lease = lock(&path)?;
                let before = calls().await?;
                ensure!(
                    review::run(
                        &path,
                        "/inputs/changes/case-001",
                        "billing",
                        false,
                        false,
                        None
                    )
                    .await
                    .is_err(),
                    "Concurrent worker acquired lease"
                );
                ensure!(
                    calls().await? == before,
                    "Contending worker called provider"
                );
                Ok(json!({}))
            });
            check!("save-crashed-process-for-restart", async {
                let path = format!("{dir}/crashed");
                let output =
                    child(&["crash", &path, "/inputs/changes/case-001", "billing"]).await?;
                ensure!(
                    output.status.code() == Some(71),
                    "Process did not crash at completion checkpoint"
                );
                save(format!("{dir}/restart.json"), &json!({"path":path}))?;
                Ok(json!({}))
            });
        }
        save(
            format!("{dir}/usage-after.json"),
            &ops(true)?.reports.usage(Default::default()).await?,
        )?;
    }
    ensure!(
        !rows.is_empty() && rows.iter().all(|r| r["passed"] == true),
        "Qualification failed; inspect {dir}"
    );
    Ok(())
}
