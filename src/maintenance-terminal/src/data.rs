// SPDX-License-Identifier: Apache-2.0
use anyhow::{bail, ensure, Result};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use std::{collections::BTreeMap, fs, path::Path};

pub const REVISION: &str = "bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3";
#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct Asset {
    pub id: String,
    pub asset: String,
    pub revision: String,
    pub historical: bool,
}
pub fn hash(bytes: impl AsRef<[u8]>) -> String {
    hex::encode(Sha256::digest(bytes))
}
pub fn read(path: impl AsRef<Path>) -> Result<Value> {
    Ok(serde_json::from_slice(&fs::read(path)?)?)
}
pub fn save(path: impl AsRef<Path>, value: &impl Serialize) -> Result<()> {
    let path = path.as_ref();
    fs::create_dir_all(path.parent().unwrap())?;
    let temporary = path.with_extension("tmp");
    fs::write(&temporary, serde_json::to_vec_pretty(value)?)?;
    fs::rename(temporary, path)?;
    Ok(())
}
pub fn catalogue(root: &str) -> Result<Vec<Asset>> {
    Ok(serde_json::from_value(read(format!(
        "{root}/catalogue.json"
    ))?)?)
}
pub fn select<'a>(assets: &'a [Asset], asset: &str, revision: &str) -> Result<&'a Asset> {
    assets
        .iter()
        .find(|a| a.asset == asset && a.revision == revision)
        .ok_or_else(|| {
            anyhow::anyhow!(
                "Unsupported asset/revision: {asset} {revision}. No procedure available."
            )
        })
}
pub fn assignments(provider: &str) -> Result<Vec<&'static str>> {
    Ok(match provider {
        "fixture" => vec![
            "case-001", "case-002", "case-003", "case-004", "case-005", "case-006", "case-007",
            "case-008",
        ],
        "openai" => vec!["case-001", "case-005", "case-007"],
        "anthropic" => vec!["case-002", "case-004", "case-006"],
        "openrouter" => vec!["case-003", "case-008"],
        _ => bail!("Unknown provider"),
    })
}
pub fn generate(root: &str, oracle_root: &str) -> Result<()> {
    fs::create_dir_all(format!("{root}/documents"))?;
    let mut assets = Vec::new();
    let mut oracle = BTreeMap::new();
    // Independent business expectations: kept off the app, Server and fixture mounts.
    let expectations = [
        ("available", "BLUE-17"),
        ("historical", "AMBER-12"),
        ("available", "GREEN-23"),
        ("historical", "VIOLET-09"),
        ("insufficient", "INSPECTION-REQUIRED"),
        ("historical", "SILVER-08"),
        ("insufficient", "MANUAL-REQUIRED"),
        ("historical", "WHITE-04"),
    ];
    let procedures = [
        "Procedure code: BLUE-17. Record the blue display indicator in the maintenance log.",
        "Procedure code: AMBER-12. The retired manual recorded the amber display indicator.",
        "Procedure code: GREEN-23. Record the green display indicator in the maintenance log.",
        "Procedure code: VIOLET-09. The retired manual recorded the violet display indicator.",
        "Procedure code: INSPECTION-REQUIRED. The inspection record is missing. Request a current inspection; no actionable procedure is supported.",
        "Procedure code: SILVER-08. The retired manual recorded the silver display indicator.",
        "Procedure code: MANUAL-REQUIRED. No approved manual is available. Request the approved manual; no actionable procedure is supported.",
        "Procedure code: WHITE-04. The retired manual recorded the white display indicator.",
    ];
    for i in 0..8 {
        let asset = Asset {
            id: format!("case-{:03}", i + 1),
            asset: format!("SIM-{}", 100 + i / 2),
            revision: if i % 2 == 0 { "R2" } else { "R1" }.into(),
            historical: i % 2 == 1,
        };
        let heading = format!(
            "Synthetic seed 1742; logical date 2026-09-10. Fictional asset {} revision {}.",
            asset.asset, asset.revision
        );
        let mode = if asset.historical {
            "HISTORICAL evidence only. Never apply this revision to current equipment."
        } else {
            "CURRENT revision R2."
        };
        let manual = format!("{heading}\n{mode}\n{}\n", procedures[i]);
        let inspection = format!(
            "{heading}\nInspection note: {}.\n{mode}\n",
            if i == 4 {
                "missing; do not infer completion"
            } else {
                "synthetic display label reviewed on 2026-09-09"
            }
        );
        let revision = format!(
            "{heading}\nRevision notice: R2 supersedes R1. Selected {} is {}.\n",
            asset.revision,
            if asset.historical {
                "historical"
            } else {
                "current"
            }
        );
        for (kind, text) in [
            ("manual", manual),
            ("inspection", inspection),
            ("revision", revision),
        ] {
            fs::write(format!("{root}/documents/{}-{kind}.txt", asset.id), text)?;
        }
        oracle.insert(
            asset.id.clone(),
            json!({"status":expectations[i].0,"code":expectations[i].1,"revision":asset.revision}),
        );
        assets.push(asset);
    }
    save(format!("{root}/catalogue.json"), &assets)?;
    let mut files = BTreeMap::new();
    files.insert(
        "catalogue.json".to_string(),
        hash(fs::read(format!("{root}/catalogue.json"))?),
    );
    for entry in fs::read_dir(format!("{root}/documents"))? {
        let e = entry?;
        files.insert(
            format!("documents/{}", e.file_name().to_string_lossy()),
            hash(fs::read(e.path())?),
        );
    }
    save(
        format!("{root}/manifest.json"),
        &json!({"seed":1742,"logical_date":"2026-09-10","files":files}),
    )?;
    save(format!("{oracle_root}/expected.json"), &oracle)
}
pub fn verify(root: &str) -> Result<()> {
    let manifest = read(format!("{root}/manifest.json"))?;
    for (name, expected) in manifest["files"].as_object().unwrap() {
        ensure!(
            hash(fs::read(format!("{root}/{name}"))?) == expected.as_str().unwrap(),
            "Fixture hash mismatch: {name}"
        );
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn exact_revision_selection() {
        let a = vec![Asset {
            id: "x".into(),
            asset: "SIM-1".into(),
            revision: "R1".into(),
            historical: true,
        }];
        assert!(select(&a, "SIM-1", "R2").is_err());
        assert!(select(&a, "unknown", "R1").is_err());
        assert!(select(&a, "SIM-1", "R1").unwrap().historical);
    }
    #[test]
    fn provider_balance_and_partition() {
        let mut all = vec![];
        for (p, n) in [("openai", 3), ("anthropic", 3), ("openrouter", 2)] {
            let a = assignments(p).unwrap();
            assert_eq!(a.len(), n);
            all.extend(a);
        }
        all.sort();
        assert_eq!(all, assignments("fixture").unwrap());
        assert!(assignments("ollama").is_err());
    }
}
