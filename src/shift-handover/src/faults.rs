// SPDX-License-Identifier: Apache-2.0
use axum::{
    body::{to_bytes, Body},
    extract::State,
    http::{Request, Response, StatusCode},
    routing::any,
    Router,
};
use std::sync::{
    atomic::{AtomicU8, Ordering},
    Arc,
};
pub async fn run() -> anyhow::Result<()> {
    let mode = Arc::new(AtomicU8::new(0));
    let app = Router::new().fallback(any(forward)).with_state(mode);
    axum::serve(tokio::net::TcpListener::bind("0.0.0.0:11435").await?, app).await?;
    Ok(())
}
async fn forward(
    State(mode): State<Arc<AtomicU8>>,
    req: Request<Body>,
) -> Result<Response<Body>, StatusCode> {
    let path = req.uri().path().to_string();
    if let Some(action) = path.strip_prefix("/control/") {
        mode.store(
            match action {
                "drop" => 1,
                "outage" => 2,
                _ => 0,
            },
            Ordering::SeqCst,
        );
        return Ok(Response::new(Body::from("{}")));
    }
    if mode.load(Ordering::SeqCst) == 2 {
        return Ok(Response::builder()
            .status(502)
            .body(Body::from("Controlled dependency outage"))
            .unwrap());
    }
    let (parts, body) = req.into_parts();
    let bytes = to_bytes(body, 1_048_576)
        .await
        .map_err(|_| StatusCode::BAD_REQUEST)?;
    let mut request = reqwest::Client::new()
        .request(
            parts.method.clone(),
            format!("http://server:8080{}", parts.uri),
        )
        .body(bytes);
    for name in [
        "authorization",
        "x-munarium-uid",
        "content-type",
        "accept",
        "idempotency-key",
    ] {
        if let Some(value) = parts.headers.get(name) {
            request = request.header(name, value);
        }
    }
    let upstream = request.send().await.map_err(|_| StatusCode::BAD_GATEWAY)?;
    let status = upstream.status();
    let content = upstream.headers().get("content-type").cloned();
    let bytes = upstream
        .bytes()
        .await
        .map_err(|_| StatusCode::BAD_GATEWAY)?;
    if mode.load(Ordering::SeqCst) == 1 && parts.method == "POST" && path.ends_with("/claims") {
        return Ok(Response::builder()
            .status(502)
            .header("content-type", "application/problem+json")
            .body(Body::from(
                r#"{"type":"about:blank","title":"Controlled lost claim response","status":502}"#,
            ))
            .unwrap());
    }
    let mut response = Response::builder().status(status);
    if let Some(content) = content {
        response = response.header("content-type", content);
    }
    Ok(response.body(Body::from(bytes)).unwrap())
}
