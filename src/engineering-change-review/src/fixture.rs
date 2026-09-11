// SPDX-License-Identifier: Apache-2.0
use anyhow::Result;
use axum::{
    extract::State,
    http::StatusCode,
    routing::{get, post},
    Json, Router,
};
use serde_json::{json, Value};
use std::sync::{Arc, Mutex};
type StateData = Arc<Mutex<(String, u64)>>;
async fn mode(
    axum::extract::Path(value): axum::extract::Path<String>,
    State(state): State<StateData>,
) -> Json<Value> {
    state.lock().unwrap().0 = value;
    Json(json!({"ok":true}))
}
async fn chat(
    State(state): State<StateData>,
    Json(body): Json<Value>,
) -> (StatusCode, Json<Value>) {
    let mode = {
        let mut s = state.lock().unwrap();
        s.1 += 1;
        s.0.clone()
    };
    if mode == "unavailable" {
        return (
            StatusCode::SERVICE_UNAVAILABLE,
            Json(json!({"error":"Synthetic provider outage"})),
        );
    }
    let prompt = body["messages"]
        .as_array()
        .unwrap()
        .iter()
        .map(|m| m["content"].as_str().unwrap_or(""))
        .collect::<Vec<_>>()
        .join("\n");
    let evidence = prompt
        .split("EVIDENCE_START")
        .nth(1)
        .unwrap_or("")
        .split("EVIDENCE_END")
        .next()
        .unwrap_or("");
    let mut citations = vec![];
    for part in evidence.split('[').skip(1) {
        let label = part.split(']').next().unwrap_or("");
        if label.contains('/') && !label.contains(char::is_whitespace) {
            citations.push(label.to_string());
        }
    }
    let field = |prefix: &str| {
        evidence
            .lines()
            .find_map(|line| line.strip_prefix(prefix))
            .unwrap_or("")
            .to_string()
    };
    let migration = prompt.contains("\"migration_plan\":true");
    let answer = json!({"finding":if migration{"no_advisory"}else{"migration_review_missing"},"requirement_code":field("Requirement code: "),"requirement_quote":field("Requirement: "),"release_quote":field("Release: "),"advisory":true,"citations":if mode=="bad-citation"{vec!["unserved/requirement".into()]}else{citations}});
    (
        StatusCode::OK,
        Json(
            json!({"model":body["model"],"done":true,"done_reason":"stop","message":{"role":"assistant","content":answer.to_string()},"prompt_eval_count":150,"eval_count":120}),
        ),
    )
}
pub async fn run() -> Result<()> {
    let state = Arc::new(Mutex::new(("ok".into(), 0)));
    let app=Router::new().route("/api/tags",get(||async{Json(json!({"models":[{"name":"engineering-fixture","model":"engineering-fixture"}]}))})).route("/calls",get(|State(s):State<StateData>|async move{Json(json!({"calls":s.lock().unwrap().1}))})).route("/mode/{value}",get(mode)).route("/api/chat",post(chat)).with_state(state);
    axum::serve(tokio::net::TcpListener::bind("0.0.0.0:11434").await?, app).await?;
    Ok(())
}
pub async fn faults() -> Result<()> {
    let state = Arc::new(Mutex::new(("normal".into(), 0)));
    let app = Router::new()
        .route("/control/{value}", get(mode))
        .fallback(proxy)
        .with_state(state);
    axum::serve(tokio::net::TcpListener::bind("0.0.0.0:11435").await?, app).await?;
    Ok(())
}
async fn proxy(
    State(state): State<StateData>,
    request: axum::extract::Request,
) -> axum::response::Response {
    let (parts, body) = request.into_parts();
    let drop = state.lock().unwrap().0 == "drop-turn"
        && parts.method == "POST"
        && parts.uri.path().contains("/turns");
    let response = async {
        let bytes = axum::body::to_bytes(body, 1024 * 1024).await?;
        let mut req = reqwest::Client::new()
            .request(parts.method, format!("http://server:8080{}", parts.uri))
            .body(bytes);
        for (k, v) in &parts.headers {
            if k.as_str() != "host" && k.as_str() != "content-length" {
                req = req.header(k, v);
            }
        }
        let result = req.send().await?;
        let status = result.status();
        let content_type = result.headers().get("content-type").cloned();
        let bytes = result.bytes().await?;
        let mut response = axum::response::Response::builder().status(if drop {
            StatusCode::BAD_GATEWAY
        } else {
            status
        });
        if let Some(value) = content_type {
            response = response.header("content-type", value);
        }
        Ok::<_, anyhow::Error>(response.body(axum::body::Body::from(if drop {
            b"Synthetic lost turn response".to_vec()
        } else {
            bytes.to_vec()
        }))?)
    }
    .await;
    response.unwrap_or_else(|_| {
        axum::response::Response::builder()
            .status(502)
            .body(axum::body::Body::empty())
            .unwrap()
    })
}
