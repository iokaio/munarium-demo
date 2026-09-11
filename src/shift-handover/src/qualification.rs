// SPDX-License-Identifier: Apache-2.0
use crate::{bootstrap::*, data::*, journal::*};
use anyhow::{ensure, Result};
use munarium_client::{dto, MunariumError};
use serde_json::{json, Value};
use std::{
    fs,
    time::{Duration, Instant},
};
fn report(dir: &str, rows: &[Value]) -> Result<()> {
    let failed = rows.iter().filter(|r| r["passed"] != true).count();
    save(
        format!("{dir}/quality.json"),
        &json!({"tests":rows.len(),"failed":failed,"cases":rows,"model_calls":0,"client_revision":REVISION}),
    )?;
    let escape = |s: &str| {
        s.replace('&', "&amp;")
            .replace('<', "&lt;")
            .replace('>', "&gt;")
            .replace('"', "&quot;")
    };
    let mut xml = format!(
        "<testsuite name=\"shift-handover\" tests=\"{}\" failures=\"{failed}\" skipped=\"0\">",
        rows.len()
    );
    for row in rows {
        xml.push_str(&format!(
            "<testcase name=\"{}\">",
            escape(row["name"].as_str().unwrap())
        ));
        if row["passed"] != true {
            xml.push_str(&format!(
                "<failure>{}</failure>",
                escape(row["error"].as_str().unwrap())
            ));
        }
        xml.push_str("</testcase>");
    }
    xml.push_str("</testsuite>");
    write(format!("{dir}/tests.xml"), &xml)
}
async fn control(mode: &str) -> Result<()> {
    reqwest::get(format!("http://faults:11435/control/{mode}"))
        .await?
        .error_for_status()?;
    Ok(())
}
fn input(dir: &str, shift: &str, later: bool) -> Result<String> {
    let path = format!("{dir}/arrivals.ndjson");
    let mut text = fs::read_to_string(format!("/inputs/{shift}-initial.ndjson"))?;
    if later {
        text.push_str(&fs::read_to_string(format!(
            "/inputs/{shift}-later.ndjson"
        ))?);
    }
    write(&path, &text)?;
    Ok(path)
}
async fn case(dir: &str, shift: &str, expected: &Value) -> Result<Value> {
    let file = input(dir, shift, false)?;
    scan(&file, dir, &endpoint(), false).await?;
    close_shift(dir, shift).await?;
    let before = brief(dir, shift, true, 600).await?;
    ensure!(
        before["promises"]["promises"][0]["status"] == expected["prior_promise"],
        "Pinned promise is not open"
    );
    input(dir, shift, true)?;
    scan(&file, dir, &endpoint(), false).await?;
    let historical = brief(dir, shift, true, 600).await?;
    let current = brief(dir, shift, false, 600).await?;
    ensure!(
        same_pinned_evidence(&historical, &before),
        "Historical view changed after later events"
    );
    ensure!(
        current["facts"]["facts"][0]["value"] == expected["current_status"],
        "Current status mismatch"
    );
    ensure!(
        historical["facts"]["facts"][0]["value"] == expected["prior_status"],
        "Prior status mismatch"
    );
    ensure!(
        current["promises"]["promises"][0]["status"] == expected["current_promise"],
        "Fulfilled promise mismatch"
    );
    ensure!(
        current["anchors"]["anchors"][0]["locked_value"] == expected["milestone"],
        "Inspection anchor mismatch"
    );
    let pin = historical["as_of_seq"].as_u64().unwrap();
    ensure!(
        pin > 0
            && historical["facts"]["as_of_seq"] == pin
            && historical["context"]["as_of_seq"] == pin,
        "Inconsistent historical pins"
    );
    ensure!(
        current["context"]["estimated_tokens"].as_u64().unwrap() <= 600,
        "Context exceeded budget"
    );
    let saved = fs::read(format!("{dir}/journal.json"))?;
    let head = reader()?.query.head(versions()?[shift].as_str()).await?;
    scan(&file, dir, &endpoint(), false).await?;
    ensure!(
        saved == fs::read(format!("{dir}/journal.json"))?
            && head == reader()?.query.head(versions()?[shift].as_str()).await?,
        "Duplicate arrival wrote again"
    );
    Ok(
        json!({"shift":shift,"prior_pin":pin,"current_pin":current["as_of_seq"],"old_commitment":"open","current_commitment":"fulfilled","context_hash":historical["context"]["content_hash"]}),
    )
}
pub async fn run(kind: &str, dir: &str) -> Result<()> {
    ready().await?;
    fs::create_dir_all(dir)?;
    save(
        format!("{dir}/manifest.json"),
        &read("/inputs/manifest.json")?,
    )?;
    let mut rows = vec![];
    let report_dir = if kind == "restarted" {
        format!("{dir}/restarted")
    } else {
        dir.to_owned()
    };
    macro_rules! check {($name:expr,$body:expr)=>{{let now=Instant::now();let result:Result<Value>=$body.await;let row=match result{Ok(details)=>json!({"name":$name,"passed":true,"seconds":now.elapsed().as_secs_f64(),"details":details}),Err(error)=>json!({"name":$name,"passed":false,"seconds":now.elapsed().as_secs_f64(),"error":format!("{error:#}")})};println!("{} {} {}",if row["passed"]==true{"PASS"}else{"FAIL"},$name,row.get("error").unwrap_or(&Value::Null));rows.push(row);report(&report_dir,&rows)?;}};}
    if kind == "restarted" {
        check!("historical-pin-after-server-restart", async {
            let checkpoint = read(format!("{dir}/restart.json"))?;
            let path = checkpoint["path"].as_str().unwrap();
            let before = read(format!("{path}/historical-shift-008.json"))?;
            let after = brief(path, "shift-008", true, 600).await?;
            ensure!(
                same_pinned_evidence(&before, &after),
                "Restart changed pinned view"
            );
            Ok(json!({"pin":after["as_of_seq"]}))
        });
    } else {
        ensure!(
            kind == "controlled",
            "Only keyless ledger qualification applies"
        );
        let expected = read("/oracle/expected.json")?;
        for shift in expected.as_object().unwrap().keys() {
            check!(
                shift,
                case(&format!("{dir}/{shift}"), shift, &expected[shift])
            );
        }
        check!("unreviewed-and-partial-arrivals", async {
            let path = format!("{dir}/pending");
            let file = format!("{path}/arrivals.ndjson");
            let mut event = events("/inputs/shift-001-initial.ndjson")?.remove(0);
            event.id = "pending-review".into();
            event.reviewed = false;
            event.reviewer.clear();
            let bytes = serde_json::to_string(&event)?;
            let version = &versions()?["shift-001"];
            let head = reader()?.query.head(version).await?;
            write(&file, &bytes)?;
            scan(&file, &path, &endpoint(), false).await?;
            ensure!(
                journal(&path)?["events"].as_object().unwrap().is_empty(),
                "Partial event processed"
            );
            write(&file, &(bytes + "\n"))?;
            scan(&file, &path, &endpoint(), false).await?;
            ensure!(
                journal(&path)?["events"]["pending-review"]["status"] == "review_required",
                "Unreviewed event not held"
            );
            ensure!(
                reader()?.query.head(version).await? == head,
                "Unreviewed event wrote to ledger"
            );
            Ok(json!({"writes":0}))
        });
        check!("changed-event-id-is-rejected", async {
            let path = format!("{dir}/shift-002");
            let file = format!("{path}/arrivals.ndjson");
            let original = fs::read_to_string(&file)?;
            write(
                &file,
                &original.replace("awaiting follow-up", "altered payload"),
            )?;
            ensure!(
                scan(&file, &path, &endpoint(), false).await.is_err(),
                "Changed event accepted"
            );
            write(&file, &original)?;
            let saved = journal(&path)?;
            let mut changed = saved.clone();
            changed["events"]["shift-002-status"]["version"] = json!("different-version");
            save(format!("{path}/journal.json"), &changed)?;
            ensure!(
                scan(&file, &path, &endpoint(), false).await.is_err(),
                "Changed catalogue accepted"
            );
            save(format!("{path}/journal.json"), &saved)?;
            Ok(json!({"rejected":true,"version_drift_rejected":true}))
        });
        check!("read-only-and-invalid-credentials", async {
            let versions = versions()?;
            let req: dto::ProposeClaimRequest = serde_json::from_value(
                json!({"claim_type":"fact","subject":"station_001","key":"status","value":"unauthorized"}),
            )?;
            ensure!(
                matches!(
                    reader()?
                        .commands
                        .propose_claim(&versions["shift-001"], req, None)
                        .await,
                    Err(MunariumError::Forbidden { .. })
                ),
                "Read-only identity wrote a claim"
            );
            let bad = client_at(&endpoint(), "invalid-shift-token", "shift-reader")?;
            ensure!(
                matches!(
                    bad.query.head(&versions["shift-001"]).await,
                    Err(MunariumError::Unauthenticated { .. })
                ),
                "Invalid credential accepted"
            );
            Ok(json!({"denied":true}))
        });
        check!("lost-claim-response-recovery", async {
            let path = format!("{dir}/lost");
            let file = format!("{path}/arrivals.ndjson");
            let mut event = events("/inputs/shift-003-initial.ndjson")?.remove(0);
            event.id = "lost-note".into();
            event.key = "handover_note".into();
            event.value = "Reviewed office follow-up note".into();
            write(&file, &(serde_json::to_string(&event)? + "\n"))?;
            control("drop").await?;
            let result = scan(&file, &path, "http://faults:11435", false).await;
            control("normal").await?;
            ensure!(result.is_err(), "Fault did not interrupt response");
            ensure!(
                scan(&file, &path, &endpoint(), false).await.is_err(),
                "Uncertain command replayed"
            );
            reconcile(&path, &event.id).await?;
            ensure!(
                journal(&path)?["events"][&event.id]["recovered"] == true,
                "Lost claim not recovered"
            );
            Ok(json!({"recovered":true}))
        });
        check!("dependency-outage-keeps-checkpoint", async {
            let path = format!("{dir}/offline");
            let file = input(&path, "shift-004", false)?;
            control("outage").await?;
            let result = scan(&file, &path, "http://faults:11435", false).await;
            control("normal").await?;
            ensure!(result.is_err(), "Outage unexpectedly passed");
            ensure!(
                journal(&path)?["events"].as_object().unwrap().is_empty(),
                "Unsubmitted event marked complete"
            );
            Ok(json!({"no_write":true}))
        });
        check!("daemon-kill-and-restart-replay", async {
            let path = format!("{dir}/daemon");
            let file = format!("{path}/arrivals.ndjson");
            let mut event = events("/inputs/shift-005-initial.ndjson")?.remove(0);
            event.id = "daemon-note".into();
            event.key = "handover_note".into();
            write(&file, &(serde_json::to_string(&event)? + "\n"))?;
            let mut child = std::process::Command::new(std::env::current_exe()?)
                .args(["daemon", &file, &path])
                .stdout(std::process::Stdio::null())
                .stderr(std::process::Stdio::null())
                .spawn()?;
            for _ in 0..40 {
                if journal(&path)?["events"][&event.id]["status"] == "complete" {
                    break;
                }
                tokio::time::sleep(Duration::from_millis(250)).await;
            }
            child.kill()?;
            child.wait()?;
            ensure!(
                journal(&path)?["events"][&event.id]["status"] == "complete",
                "Daemon never checkpointed"
            );
            let head = reader()?.query.head(&versions()?["shift-005"]).await?;
            scan(&file, &path, &endpoint(), false).await?;
            ensure!(
                head == reader()?.query.head(&versions()?["shift-005"]).await?,
                "Restart duplicated event"
            );
            Ok(json!({"head":head}))
        });
        check!("process-crash-before-receipt", async {
            let path = format!("{dir}/crash");
            let file = format!("{path}/arrivals.ndjson");
            let mut event = events("/inputs/shift-006-initial.ndjson")?.remove(0);
            event.id = "crash-note".into();
            event.key = "handover_note".into();
            write(&file, &(serde_json::to_string(&event)? + "\n"))?;
            let status = std::process::Command::new(std::env::current_exe()?)
                .args(["crash", &file, &path])
                .status()?;
            ensure!(status.code() == Some(71), "Expected process crash");
            ensure!(
                journal(&path)?["events"][&event.id]["status"] == "uncertain",
                "Intent lost at crash"
            );
            reconcile(&path, &event.id).await?;
            ensure!(
                journal(&path)?["events"][&event.id]["recovered"] == true,
                "Crash recovery failed"
            );
            Ok(json!({"recovered":true}))
        });
        check!("small-context-budget-preserves-pin", async {
            let path = format!("{dir}/shift-007");
            let prior = read(format!("{path}/historical-shift-007.json"))?;
            let brief = brief(&path, "shift-007", true, 64).await?;
            ensure!(
                brief["context"]["estimated_tokens"].as_u64().unwrap()
                    < prior["context"]["estimated_tokens"].as_u64().unwrap()
                    && brief["budget_exceeded"] == true
                    && brief["context"]["text"]
                        .as_str()
                        .unwrap()
                        .contains("Open promises")
                    && brief["context"]["text"]
                        .as_str()
                        .unwrap()
                        .contains("Locked details"),
                "Small budget must reduce content and report protected-section overflow"
            );
            ensure!(
                brief["context"]["as_of_seq"] == brief["as_of_seq"],
                "Budget changed historical pin"
            );
            Ok(json!({"tokens":brief["context"]["estimated_tokens"]}))
        });
        save(
            format!("{dir}/restart.json"),
            &json!({"path":format!("{dir}/shift-008")}),
        )?;
    }
    ensure!(
        !rows.is_empty() && rows.iter().all(|r| r["passed"] == true),
        "Qualification failed; retained reports at {dir}"
    );
    Ok(())
}

fn same_pinned_evidence(left: &Value, right: &Value) -> bool {
    let mut left = left.clone();
    let mut right = right.clone();
    // Server reports current head metadata even on historical reads. Preserve it in raw exports,
    // but compare the pinned facts and all other evidence independently of that observation.
    left["facts"].as_object_mut().unwrap().remove("head_seq");
    right["facts"].as_object_mut().unwrap().remove("head_seq");
    left == right
}
