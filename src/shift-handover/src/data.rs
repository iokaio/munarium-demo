// SPDX-License-Identifier: Apache-2.0
use anyhow::{ensure, Result};
use serde::{Deserialize, Serialize};
use serde_json::{json, Value};
use sha2::{Digest, Sha256};
use std::{collections::BTreeMap, fs, io::Write, path::Path};
pub const REVISION: &str = "bb6e92a72a3944cff4d4bf0c1b470afcf3f4dfb3";
pub fn hash(bytes: impl AsRef<[u8]>) -> String {
    hex::encode(Sha256::digest(bytes.as_ref()))
}
pub fn read(path: impl AsRef<Path>) -> Result<Value> {
    Ok(serde_json::from_slice(&fs::read(path)?)?)
}
pub fn write(path: impl AsRef<Path>, text: &str) -> Result<()> {
    let path = path.as_ref();
    let parent = path.parent().unwrap();
    fs::create_dir_all(parent)?;
    let temp = parent.join(format!(".shift-{}.tmp", uuid::Uuid::new_v4()));
    let mut file = fs::OpenOptions::new()
        .create_new(true)
        .write(true)
        .open(&temp)?;
    file.write_all(text.as_bytes())?;
    file.sync_all()?;
    drop(file);
    fs::rename(temp, path)?;
    Ok(())
}
pub fn save(path: impl AsRef<Path>, value: &impl Serialize) -> Result<()> {
    write(path, &(serde_json::to_string_pretty(value)? + "\n"))
}
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Event {
    pub id: String,
    pub shift: String,
    pub kind: String,
    pub subject: String,
    pub key: String,
    pub value: String,
    pub reviewed: bool,
    pub reviewer: String,
}
impl Event {
    pub fn validate(&self) -> Result<()> {
        ensure!(
            !self.id.is_empty()
                && self.id.len() <= 80
                && self
                    .id
                    .bytes()
                    .all(|c| c.is_ascii_alphanumeric() || c == b'-'),
            "Invalid event ID"
        );
        ensure!(
            self.shift.starts_with("shift-")
                && self.shift.len() == 9
                && self.shift[6..].bytes().all(|c| c.is_ascii_digit()),
            "Invalid shift"
        );
        ensure!(
            self.subject == format!("station_{}", &self.shift[6..]),
            "Event belongs to another station"
        );
        ensure!(
            matches!(
                self.kind.as_str(),
                "fact" | "update" | "anchor" | "promise" | "fulfill"
            ),
            "Unknown event kind"
        );
        ensure!(
            !self.key.is_empty() && !self.value.is_empty() && self.value.len() <= 200,
            "Invalid event fields"
        );
        ensure!(
            !self.reviewed || !self.reviewer.trim().is_empty(),
            "Reviewed event needs reviewer attribution"
        );
        Ok(())
    }
}
pub fn events(path: impl AsRef<Path>) -> Result<Vec<Event>> {
    let bytes = fs::read(path)?;
    ensure!(
        bytes.len() <= 1_048_576,
        "Input exceeds one MiB tutorial limit"
    );
    let end = bytes
        .iter()
        .rposition(|b| *b == b'\n')
        .map(|i| i + 1)
        .unwrap_or(0);
    let text = std::str::from_utf8(&bytes[..end])?;
    let mut seen = BTreeMap::new();
    let mut rows = vec![];
    for line in text.lines().filter(|l| !l.trim().is_empty()) {
        let e: Event = serde_json::from_str(line)?;
        e.validate()?;
        let fingerprint = hash(serde_json::to_vec(&e)?);
        if let Some(previous) = seen.insert(e.id.clone(), fingerprint.clone()) {
            ensure!(
                previous == fingerprint,
                "Duplicate event ID with different bytes"
            );
        } else {
            rows.push(e);
        }
    }
    Ok(rows)
}
pub fn generate(inputs: &str, oracle: &str) -> Result<()> {
    let mut expected = BTreeMap::new();
    let mut hashes = BTreeMap::new();
    let milestones = [
        "badge desk checked",
        "supply cupboard checked",
        "meeting room checked",
        "display station checked",
        "visitor log checked",
        "training desk checked",
        "delivery desk checked",
        "archive desk checked",
    ];
    for (i, milestone) in milestones.iter().enumerate() {
        let id = format!("shift-{:03}", i + 1);
        let subject = format!("station_{:03}", i + 1);
        let event = |suffix: &str, kind: &str, key: &str, value: &str| Event {
            id: format!("{id}-{suffix}"),
            shift: id.clone(),
            kind: kind.into(),
            subject: subject.clone(),
            key: key.into(),
            value: value.into(),
            reviewed: true,
            reviewer: "synthetic-supervisor".into(),
        };
        let initial = vec![
            event("status", "fact", "status", "awaiting follow-up"),
            event("inspection", "anchor", "inspection", milestone),
            event(
                "commitment",
                "promise",
                "follow-up",
                "Check the next shift's office handover — équipe fictive",
            ),
        ];
        let later = vec![
            event("update", "update", "status", "follow-up reviewed"),
            event("fulfilled", "fulfill", "follow-up", "review confirmed"),
        ];
        for (suffix, rows) in [("initial", initial), ("later", later)] {
            let name = format!("{id}-{suffix}.ndjson");
            let text = rows
                .iter()
                .map(serde_json::to_string)
                .collect::<std::result::Result<Vec<_>, _>>()?
                .join("\n")
                + "\n";
            write(format!("{inputs}/{name}"), &text)?;
            hashes.insert(name, hash(text));
        }
        expected.insert(id,json!({"prior_status":"awaiting follow-up","current_status":"follow-up reviewed","promise_key":"follow-up","prior_promise":"open","current_promise":"fulfilled","milestone":milestone}));
    }
    save(
        format!("{inputs}/manifest.json"),
        &json!({"seed":7091,"generator_version":1,"template_revision":"office-shifts-1","logical_date":"2026-09-11","timezone":"UTC","locale":"invariant","shifts":8,"files":hashes}),
    )?;
    save(format!("{oracle}/expected.json"), &expected)
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn incomplete_lines_are_not_events() {
        let root = std::env::temp_dir().join(uuid::Uuid::new_v4().to_string());
        fs::create_dir_all(&root).unwrap();
        write(root.join("arrivals"), "{\"unfinished\":").unwrap();
        assert!(events(root.join("arrivals")).unwrap().is_empty());
    }
    #[test]
    fn malformed_complete_lines_fail_closed() {
        let root = std::env::temp_dir().join(uuid::Uuid::new_v4().to_string());
        write(root.join("arrivals"), "invalid\n").unwrap();
        assert!(events(root.join("arrivals")).is_err());
    }
    #[test]
    fn fixtures_have_eight_independent_reviewed_shifts() {
        let root = std::env::temp_dir().join(uuid::Uuid::new_v4().to_string());
        generate(
            root.to_str().unwrap(),
            root.join("oracle").to_str().unwrap(),
        )
        .unwrap();
        for i in 1..=8 {
            let rows = events(root.join(format!("shift-{i:03}-initial.ndjson"))).unwrap();
            assert_eq!(rows.len(), 3);
            assert!(rows.iter().all(|e| e.reviewed));
        }
    }
}
