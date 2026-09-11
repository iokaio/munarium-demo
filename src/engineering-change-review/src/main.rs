// SPDX-License-Identifier: Apache-2.0
mod bootstrap;
mod data;
mod fixture;
mod qualification;
mod review;
use anyhow::Result;
#[tokio::main]
async fn main() {
    let args: Vec<String> = std::env::args().skip(1).collect();
    match run(&args).await {
        Ok(code) => std::process::exit(code),
        Err(error) => {
            eprintln!("{error:#}");
            std::process::exit(3);
        }
    }
}
async fn run(args: &[String]) -> Result<i32> {
    match args.first().map(String::as_str){Some("generate")=>data::generate(args.get(1).map(String::as_str).unwrap_or("/inputs"),args.get(2).map(String::as_str).unwrap_or("/oracle"))?,Some("bootstrap")=>bootstrap::run(&args[1],args.iter().any(|s|s=="--approve")).await?,Some("provider")=>fixture::run().await?,Some("faults")=>fixture::faults().await?,Some("review"|"recover"|"crash")=>{let packet=review::run(&args[1],&args[2],&args[3],args[0]=="recover",args[0]=="crash",None).await?;println!("{}",std::fs::read_to_string(format!("{}/review.md",args[1]))?);return Ok(review::exit_code(&packet));},Some("qualify")=>qualification::run(&args[1],&args[2]).await?,_=>anyhow::bail!("Use review WORK CHANGE_DIR COMPONENT, recover WORK CHANGE_DIR COMPONENT, or bootstrap PROVIDER --approve")};
    Ok(0)
}
