// SPDX-License-Identifier: Apache-2.0
mod bootstrap;
mod data;
mod faults;
mod journal;
mod qualification;
use anyhow::Result;
use std::{env, fs, time::Duration};
#[tokio::main]
async fn main() -> Result<()> {
    let args = env::args().collect::<Vec<_>>();
    let arg = |n: usize| {
        args.get(n)
            .map(String::as_str)
            .ok_or_else(|| anyhow::anyhow!("Missing argument {n}"))
    };
    match arg(1)? {
        "generate"=>data::generate(args.get(2).map(String::as_str).unwrap_or("/inputs"),args.get(3).map(String::as_str).unwrap_or("/oracle"))?,
        "bootstrap"=>bootstrap::run().await?,
        "faults"=>faults::run().await?,
        "scan"|"crash"=>journal::scan(arg(2)?,arg(3)?,&bootstrap::endpoint(),arg(1)?=="crash").await?,
        "daemon"=>loop {if let Err(error)=journal::scan(arg(2)?,arg(3)?,&bootstrap::endpoint(),false).await {eprintln!("{error:#}");}tokio::time::sleep(Duration::from_millis(500)).await;},
        "stage"=>{let shift=arg(2)?;anyhow::ensure!(bootstrap::versions()?.contains_key(shift),"Unknown shift");let file=format!("/inputs/{shift}-initial.ndjson");let target=format!("/work/manual/{shift}/arrivals.ndjson");if !std::path::Path::new(&target).exists(){data::write(&target,&fs::read_to_string(file)?)?;println!("Staged {target}");}},
        "close"=>journal::close_shift(arg(2)?,arg(3)?).await?,
        "brief"=>{let historical=args.get(4).is_some_and(|a|a=="historical");let budget=args.get(5).map(|s|s.parse()).transpose()?.unwrap_or(600);journal::brief(arg(2)?,arg(3)?,historical,budget).await?;let label=if historical{"historical"}else{"current"};print!("{}",fs::read_to_string(format!("{}/{label}-{}.txt",arg(2)?,arg(3)?))?);},
        "reconcile"=>journal::reconcile(arg(2)?,arg(3)?).await?,
        "qualify"=>qualification::run(arg(2)?,arg(3)?).await?,
        "cloud"=>println!("Not applicable: ledger composition uses no model inference or query expansion."),
        _=>anyhow::bail!("Commands: stage SHIFT, daemon/scan INPUT STATE, close STATE SHIFT, brief STATE SHIFT [historical|current] [BUDGET], reconcile STATE EVENT"),
    }
    Ok(())
}
