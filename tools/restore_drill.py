# SPDX-License-Identifier: Apache-2.0
"""Back up the local demo PostgreSQL database and verify an isolated restore.

Requires the supplied Compose layout. Creates disposable, ownership-labelled test
containers and keeps the private dump/evidence in .local. Does not alter source data.
Visitor SQLite backup is a separate procedure documented in backup-restore.md.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import secrets
import subprocess
import sys
import time
import urllib.request
import uuid
from setup import ROOT, settings


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", required=True, help="source Docker Compose project name")
    args = parser.parse_args()
    config = settings()
    purpose = "demo-restore-" + uuid.uuid4().hex[:10]
    directory = ROOT / ".local" / purpose
    directory.mkdir(parents=True)
    owned = []

    def run(*command):
        return subprocess.check_output(command, cwd=ROOT, text=True, encoding="utf-8", errors="replace").strip()

    source = run("docker", "compose", "-p", args.project, "ps", "-q", "postgres")
    assert source, "Source PostgreSQL container not found"
    info = json.loads(run("docker", "inspect", source))[0]
    assert info["Config"]["Labels"]["com.docker.compose.project"] == args.project
    dump = directory / "server.dump"
    with dump.open("wb") as stream:
        subprocess.run(["docker", "exec", source, "pg_dump", "-U", "demo", "-d", "demo", "-Fc"], stdout=stream, check=True)
    with dump.open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
    pg = purpose + "-pg"
    server = purpose + "-server"
    password = secrets.token_hex(32)
    pg_env = directory / "postgres.env"
    pg_env.write_text(f"POSTGRES_USER=demo\nPOSTGRES_DB=demo\nPOSTGRES_PASSWORD={password}\n")
    server_env = directory / "server.env"
    server_env.write_text("\n".join([
        "MUNARIUM_STORE=postgres", "MUNARIUM_SOURCE_STORE=pg", "MUNARIUM_RETRIEVAL_MODE=postgres",
        "MUNARIUM_AUTH_MODE=static", "MUNARIUM_STATIC_TOKENS=" + config["MUNARIUM_API_TOKEN"] + ":demo:rw," + config["MUNARIUM_MGMT_TOKEN"] + ":demo:mgmt",
        "MUNARIUM_TOKEN_SECRET=" + config["MUNARIUM_TOKEN_SECRET"],
        f"MUNARIUM_DATABASE_URL=postgres://demo:{password}@{pg}:5432/demo",
    ]) + "\n")
    for path in (pg_env, server_env, dump):
        path.chmod(0o600)
    network = purpose + "-network"
    created_network = False
    try:
        run("docker", "network", "create", "--label", "munarium.purpose=" + purpose, network)
        created_network = True
        for name, image, env, ports in [
            (pg, info["Config"]["Image"], pg_env, []),
            (server, config.get("MUNARIUM_IMAGE", "iokaio/munarium:1.1.1"), server_env, ["-p", "127.0.0.1::8080"]),
        ]:
            if name == server:
                run("docker", "cp", str(dump), pg + ":/tmp/server.dump")
                run("docker", "exec", pg, "pg_restore", "-U", "demo", "-d", "demo", "--no-owner", "--exit-on-error", "/tmp/server.dump")
            run("docker", "run", "-d", "--name", name, "--network", network,
                "--label", "munarium.purpose=" + purpose, "--env-file", str(env), *ports, image)
            owned.append(name)
            if name == pg:
                for attempt in range(60):
                    try:
                        run("docker", "exec", pg, "pg_isready", "-h", "127.0.0.1", "-U", "demo")
                        break
                    except subprocess.CalledProcessError:
                        if attempt == 59:
                            raise
                        time.sleep(1)
        base = "http://" + run("docker", "port", server, "8080/tcp").splitlines()[0]
        for attempt in range(120):
            try:
                with urllib.request.urlopen(base + "/readyz", timeout=3) as response:
                    assert response.status == 200
                break
            except OSError:
                if attempt == 119:
                    raise
                time.sleep(1)
        subprocess.run([sys.executable, str(ROOT / "tools/setup.py"), "verify"], cwd=ROOT,
                       env=config | {"MUNARIUM_BASE_URL": base}, check=True)
        record = {"status": "passed", "dump_sha256": digest, "dump_bytes": dump.stat().st_size,
                  "checked": "all bundled source counts and active indexes after PostgreSQL restore",
                  "server_image": config.get("MUNARIUM_IMAGE", "iokaio/munarium:1.1.1"),
                  "completedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())}
    finally:
        for name in reversed(owned):
            inspect = json.loads(run("docker", "inspect", name))[0]
            assert inspect["Config"]["Labels"].get("munarium.purpose") == purpose, "Container ownership changed"
            run("docker", "rm", "-f", "-v", name)
        if created_network:
            inspect = json.loads(run("docker", "network", "inspect", network))[0]
            assert inspect["Labels"].get("munarium.purpose") == purpose, "Network ownership changed"
            run("docker", "network", "rm", network)
    (directory / "evidence.json").write_text(json.dumps(record, indent=2) + "\n")
    print("Isolated database restore passed; private evidence:", directory)


if __name__ == "__main__":
    main()
