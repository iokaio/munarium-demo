# SPDX-License-Identifier: Apache-2.0
"""Check source headers, retained notices, and independently hashed vendor assets."""
import hashlib
import json
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
CODE = {".cs", ".cshtml", ".cjs", ".js", ".css", ".py", ".ps1", ".psm1", ".psd1", ".yaml", ".yml"}
VENDORED = {
    "src/Demo.Web/wwwroot/css/bootstrap.min.css",
    "src/Demo.Web/wwwroot/css/materialdesignicons.min.css",
    "src/Demo.Web/wwwroot/js/bootstrap.bundle.min.js",
}
REQUIRED = ["LICENSE", "NOTICE", "THIRD_PARTY_NOTICES.md", "data/RIGHTS.md",
            "licenses/bootstrap-MIT.txt", "licenses/material-design-icons.txt",
            "licenses/popper-MIT.txt", "licenses/dotnet-MIT.txt", "licenses/sqlite.txt",
            "licenses/pyyaml-MIT.txt", "src/Demo.Web/packages.lock.json", "package-lock.json", "license_inventory.json"]


def main():
    errors = []
    for name in REQUIRED:
        if not (ROOT / name).is_file():
            errors.append("Missing " + name)
    paths = subprocess.check_output(["git", "-C", str(ROOT), "ls-files", "-co", "--exclude-standard", "-z"]).decode().split("\0")
    inventory = {entry["path"]: entry for entry in json.loads((ROOT / "license_inventory.json").read_text())["noncommentable_assets"]}
    for name in sorted(set(paths) - {""}):
        path = ROOT / name
        if not path.is_file() or name in VENDORED:
            continue
        if path.suffix.lower() in {".json", ".svg", ".zip", ".woff", ".woff2", ".ttf", ".eot", ".ico", ".png"} and name != "license_inventory.json":
            entry = inventory.get(name)
            if not entry or not entry.get("license") or not (ROOT / entry.get("notice", "missing")).is_file():
                errors.append("Missing asset license/notice: " + name)
        if path.suffix in CODE or path.name == "Dockerfile":
            if "SPDX-License-Identifier: Apache-2.0" not in "\n".join(path.read_text(encoding="utf-8-sig").splitlines()[:4]):
                errors.append("Missing Apache source header: " + name)
    for entry in json.loads((ROOT / "vendor/assets.json").read_text())["files"]:
        if hashlib.sha256((ROOT / entry["path"]).read_bytes()).hexdigest() != entry["sha256"]:
            errors.append("Vendor checksum mismatch: " + entry["path"])
    lock = json.loads((ROOT / "src/Demo.Web/packages.lock.json").read_text())
    notices = (ROOT / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8")
    for dependencies in lock["dependencies"].values():
        for name, package in dependencies.items():
            if name + "/" + package["resolved"] not in notices:
                errors.append("Dependency missing from notices: " + name)
    if errors:
        raise SystemExit("\n".join(errors))
    print("Source headers, dependency notices, licenses, and vendor hashes passed.")


if __name__ == "__main__":
    main()
