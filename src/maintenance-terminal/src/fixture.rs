// SPDX-License-Identifier: Apache-2.0
//! Canned protocol endpoint and response-loss proxy. Neither has corpus/oracle mounts.
use anyhow::{ensure, Result};
use axum::{
    body::{to_bytes, Body},
    extract::{Request, State},
    http::StatusCode,
    response::{IntoResponse, Response},
    routing::any,
    Router,
};
use serde_json::{json, Value};
use std::sync::{
    atomic::{AtomicBool, AtomicU64, Ordering},
    Arc,
};
#[derive(Default)]
struct Control {
    fail: AtomicBool,
    drop: AtomicBool,
    calls: AtomicU64,
}
fn answer(prompt: &str) -> Result<String> {
    let evidence = prompt
        .split("EVIDENCE_START")
        .nth(1)
        .unwrap_or("")
        .split("EVIDENCE_END")
        .next()
        .unwrap_or("");
    let index = evidence
        .find("Procedure code:")
        .ok_or_else(|| anyhow::anyhow!("No procedure evidence"))?;
    let code = evidence[index + 15..]
        .trim()
        .split('.')
        .next()
        .unwrap()
        .trim();
    let before = &evidence[..index];
    let label = before
        .rsplit('[')
        .next()
        .unwrap_or("")
        .split(']')
        .next()
        .unwrap_or("");
    ensure!(
        label.contains('/') && !label.contains('\n'),
        "No source label"
    );
    let revision = if evidence.contains("revision R1.") {
        "R1"
    } else {
        "R2"
    };
    let status = if revision == "R1" {
        "historical"
    } else if code.ends_with("-REQUIRED") {
        "insufficient"
    } else {
        "available"
    };
    Ok(json!({"status":status,"revision":revision,"code":code,"explanation":format!("{status} evidence identifies {code} [{label}]."),"citations":[label]}).to_string())
}
async fn provider(State(s): State<Arc<Control>>, request: Request) -> Response {
    let path = request.uri().path().to_string();
    let (status, value) = match path.as_str() {
        "/api/tags" => (
            StatusCode::OK,
            json!({"models":[{"name":"maintenance-selected","model":"maintenance-selected"}]}),
        ),
        "/calls" => (
            StatusCode::OK,
            json!({"calls":s.calls.load(Ordering::SeqCst)}),
        ),
        "/fail" => {
            s.fail.store(true, Ordering::SeqCst);
            (StatusCode::OK, json!({"ok":true}))
        }
        "/drop" => {
            s.drop.store(true, Ordering::SeqCst);
            (StatusCode::OK, json!({"ok":true}))
        }
        "/reset" => {
            s.fail.store(false, Ordering::SeqCst);
            s.drop.store(false, Ordering::SeqCst);
            (StatusCode::OK, json!({"ok":true}))
        }
        "/api/chat" => {
            s.calls.fetch_add(1, Ordering::SeqCst);
            if s.fail.load(Ordering::SeqCst) {
                (
                    StatusCode::SERVICE_UNAVAILABLE,
                    json!({"error":"controlled outage"}),
                )
            } else {
                let bytes = to_bytes(request.into_body(), 1_000_000)
                    .await
                    .unwrap_or_default();
                let body: Value = serde_json::from_slice(&bytes).unwrap_or(Value::Null);
                let prompt = body["messages"]
                    .as_array()
                    .map(|m| {
                        m.iter()
                            .map(|v| v["content"].as_str().unwrap_or(""))
                            .collect::<Vec<_>>()
                            .join("\n")
                    })
                    .unwrap_or_default();
                match answer(&prompt) {
                    Ok(a) => (
                        StatusCode::OK,
                        json!({"model":body["model"],"done":true,"done_reason":"stop","message":{"role":"assistant","content":a},"prompt_eval_count":24,"eval_count":32}),
                    ),
                    Err(e) => (StatusCode::BAD_REQUEST, json!({"error":e.to_string()})),
                }
            }
        }
        _ => (StatusCode::NOT_FOUND, json!({"error":"unknown route"})),
    };
    (status, axum::Json(value)).into_response()
}
async fn proxy(State(s): State<Arc<Control>>, request: Request) -> Response {
    let path = request.uri().to_string();
    let (parts, body) = request.into_parts();
    let bytes = to_bytes(body, 1_000_000).await.unwrap_or_default();
    let mut forward = reqwest::Client::new()
        .request(parts.method, format!("http://server:8080{path}"))
        .body(bytes);
    for (name, value) in &parts.headers {
        if name != "host" && name != "content-length" {
            forward = forward.header(name, value);
        }
    }
    let result: Result<Response> = async {
        let response = forward.send().await?;
        let status = response.status();
        let content = response.headers().get("content-type").cloned();
        let bytes = response.bytes().await?;
        let is_stream = content
            .as_ref()
            .and_then(|c| c.to_str().ok())
            .unwrap_or("")
            .contains("text/event-stream");
        let data = if is_stream && s.drop.swap(false, Ordering::SeqCst) {
            // Let Server finish, then lose the terminal event in transit. The SDK must
            // observe an incomplete stream; recovery reads the durable transcript.
            String::from_utf8_lossy(&bytes)
                .split("event: done")
                .next()
                .unwrap_or("")
                .to_string()
                .into_bytes()
        } else {
            bytes.to_vec()
        };
        let mut result = Response::builder().status(status);
        if let Some(content) = content {
            result = result.header("content-type", content);
        }
        Ok(result.body(Body::from(data))?)
    }
    .await;
    result.unwrap_or_else(|e| (StatusCode::BAD_GATEWAY, e.to_string()).into_response())
}
pub async fn run() -> Result<()> {
    let state = Arc::new(Control::default());
    let provider = Router::new()
        .fallback(any(provider))
        .with_state(state.clone());
    let proxy = Router::new().fallback(any(proxy)).with_state(state);
    let a = tokio::net::TcpListener::bind("0.0.0.0:11434").await?;
    let b = tokio::net::TcpListener::bind("0.0.0.0:11435").await?;
    println!("Canned protocol fixture and lost-response proxy ready; no inference.");
    tokio::try_join!(axum::serve(a, provider), axum::serve(b, proxy))?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn historical_warning_does_not_change_selected_revision() {
        let prompt = "EVIDENCE_START [scope/chunk] Fictional asset SIM-100 revision R1.\nHISTORICAL evidence only. Never apply this revision to current equipment.\nProcedure code: AMBER-12. Old indicator. EVIDENCE_END";
        let value: Value = serde_json::from_str(&answer(prompt).unwrap()).unwrap();
        assert_eq!(value["revision"], "R1");
        assert_eq!(value["status"], "historical");
        assert_eq!(value["citations"], json!(["scope/chunk"]));
    }
}
