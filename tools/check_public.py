# SPDX-License-Identifier: Apache-2.0
"""Scan public files and nested archives; optionally inspect every reachable Git object.

Findings report paths/rules, never matched values. Exact private estate inventories
belong in a separate operator audit, not this public policy.
"""
import argparse
import io
from pathlib import Path
import re
import subprocess
import zipfile
from urllib.parse import unquote_to_bytes

ROOT = Path(__file__).resolve().parents[1]
RULES = {
    "cloud-resource-host": re.compile(rb"(?i)\b[a-z0-9][a-z0-9.-]+\.(?:azurecontainerapps\.io|azurewebsites\.net|azurecr\.io|vault\.azure\.net|blob\.core\.windows\.net|postgres\.database\.azure\.com)\b"),
    "azure-resource-id": re.compile(rb"(?i)/subscriptions/[0-9a-f]{8}-[0-9a-f-]{27}/"),
    "private-workspace": re.compile(rb"(?i)[A-Z]:[\\/](?:code|users)[\\/][^\r\n\x00]{0,100}[\\/]munarium-(?:int|lab)[\\/]"),
    "private-key": re.compile(rb"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
}
EXAMPLE_HOSTS = {b"example.azurecr.io", b"example.azurecontainerapps.io"}
SKIP = {".git", ".local", "node_modules", "bin", "obj", "__pycache__", "artifacts"}
findings = []


def normalize(raw):
    for _ in range(3):
        previous = raw
        if re.search(rb"%[0-9a-fA-F]{2}", raw):
            raw = unquote_to_bytes(raw)
        raw = raw.replace(b"\\/", b"/")
        if b"\\u" in raw:
            raw = re.sub(rb"\\u([0-9a-fA-F]{4})", lambda m: chr(int(m[1], 16)).encode("utf-8", errors="replace"), raw)
        if raw == previous:
            break
    return raw


def redact(name):
    raw = normalize(name.encode())
    for pattern in RULES.values():
        raw = pattern.sub(b"[redacted]", raw)
    return raw.decode(errors="replace")


def scan(name, raw, depth=0):
    normalized = normalize(raw) if b"%" in raw or b"\\" in raw else raw
    for rule, pattern in RULES.items():
        if any(m.group().lower() not in EXAMPLE_HOSTS for m in pattern.finditer(normalized)):
            findings.append((name, rule))
    # Also inspect UTF-16 metadata and strings without printing contents.
    if b"\x00" in raw:
        for encoding in ("utf-16-le", "utf-16-be"):
            text = raw.decode(encoding, errors="ignore").encode("utf-8")
            for rule, pattern in RULES.items():
                if any(m.group().lower() not in EXAMPLE_HOSTS for m in pattern.finditer(text)):
                    findings.append((name, rule + "/utf16"))
    if raw.startswith(b"PK\x03\x04"):
        if depth >= 4:
            findings.append((name, "archive-depth-exceeded"))
            return
        try:
            with zipfile.ZipFile(io.BytesIO(raw)) as archive:
                for member in archive.infolist():
                    if member.file_size > 100_000_000:
                        findings.append((name, "archive-member-too-large"))
                        continue
                    scan(name + "!" + member.filename, member.filename.encode(), depth + 1)
                    if not member.is_dir():
                        scan(name + "!" + member.filename, archive.read(member), depth + 1)
        except (zipfile.BadZipFile, RuntimeError):
            findings.append((name, "unreadable-archive"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--history", action="store_true")
    args = parser.parse_args()
    # Git's inventory includes untracked public preparation files while honoring
    # ignored secrets, databases, extracted corpora, and local build output.
    paths = subprocess.check_output(["git", "-C", str(ROOT), "ls-files", "-co", "--exclude-standard", "-z"])
    for relative in sorted(set(paths.decode().split("\0")) - {""}):
        path = ROOT / relative
        if not path.is_file() or SKIP.intersection(path.relative_to(ROOT).parts):
            continue
        scan(relative, relative.encode())
        scan(relative, path.read_bytes())
    if args.history:
        objects = subprocess.check_output(["git", "-C", str(ROOT), "rev-list", "--objects", "--all"], text=True)
        process = subprocess.Popen(["git", "-C", str(ROOT), "cat-file", "--batch"], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
        try:
            for line in objects.splitlines():
                oid, _, name = line.partition(" ")
                process.stdin.write((oid + "\n").encode()); process.stdin.flush()
                header = process.stdout.readline().decode().split()
                raw = process.stdout.read(int(header[2])); process.stdout.read(1)
                scan("git:" + oid + ":" + name, name.encode())
                if header[1] in ("blob", "commit", "tag"):
                    scan("git:" + oid + ":" + name, raw)
        finally:
            process.stdin.close(); process.wait()
    for name, rule in sorted(set(findings)):
        print(f"{redact(name)}: {rule} (value redacted)")
    if findings:
        raise SystemExit(1)
    print("Public material scan passed" + (", including reachable Git history." if args.history else "."))


if __name__ == "__main__":
    main()
