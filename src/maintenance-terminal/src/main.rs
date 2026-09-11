// SPDX-License-Identifier: Apache-2.0
mod bootstrap;
mod data;
mod fixture;
mod qualification;
mod terminal;
use anyhow::{bail, Result};
#[tokio::main]
async fn main() -> Result<()> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    let args: Vec<&str> = args.iter().map(String::as_str).collect();
    match args.as_slice() {
        ["generate"]=>data::generate("/inputs","/oracle"),
        ["generate",inputs,oracle]=>data::generate(inputs,oracle),
        ["provider"]=>fixture::run().await,
        ["bootstrap",provider,"--approve"]=>bootstrap::run(provider,true).await,
        ["qualify",kind,dir]=>qualification::run(kind,dir).await,
        ["tui",dir]=>terminal::tui(&bootstrap::grant()?,dir).await,
        [action,asset,revision,dir] if ["find","explain","reconcile"].contains(action)=>{
            let assets=data::catalogue("/inputs")?;let a=data::select(&assets,asset,revision)?;let g=bootstrap::grant()?;
            let value=match *action {"find"=>terminal::find(&g,a,dir).await?,"explain"=>terminal::explain(&g,a,dir,None).await?,_=>terminal::reconcile(&g,a,dir).await?};
            println!("{}",if *action=="find" {terminal::display(a,&value)} else {std::fs::read_to_string(format!("{dir}/answer.txt"))?});Ok(())
        },
        _=>bail!("Usage: tui DIRECTORY | find/explain/reconcile ASSET REVISION DIRECTORY | generate | bootstrap PROVIDER --approve | qualify KIND DIRECTORY"),
    }
}
