# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Demo,[ValidateSet('default','heldout','stress')][string]$Profile='default')
$ErrorActionPreference='Stop'
if ($Demo -notmatch '^[a-z][a-z0-9-]+$') { throw 'Invalid demo name' }
$repoPath = Split-Path $PSScriptRoot -Parent
if (-not (Test-Path -LiteralPath (Join-Path $repoPath "src/$Demo/compose.yaml"))) { throw 'Unknown demo' }
if ($Profile -ne 'default' -and -not (Test-Path -LiteralPath (Join-Path $repoPath "src/$Demo/fixture-profiles.json"))) { throw 'This demo has not implemented named fixture profiles yet' }
& docker compose version --short
if ($LASTEXITCODE -ne 0) { throw 'Docker Compose is required' }
$endpoint = & docker context inspect --format '{{.Endpoints.docker.Host}}'
if ($LASTEXITCODE -ne 0 -or $endpoint -notmatch '^(npipe|unix)://') { throw 'Select a local Docker context' }
if ((& docker info --format '{{.OSType}}') -ne 'linux') { throw 'Use Linux containers' }
$reportsPath=Join-Path $repoPath "artifacts/$Demo/preflight"
New-Item -ItemType Directory -Force -Path $reportsPath | Out-Null
$reportId=[guid]::NewGuid().ToString('N')
& docker run --rm --network none --mount "type=bind,source=$PSScriptRoot,target=/tools,readonly" --mount "type=bind,source=$reportsPath,target=/reports" 'python:3.12-slim@sha256:78387bc3881b8273120a12ebe6c1ab22b018ccc2c9adf565ae1ac9b536e184ea' python /tools/demo_preflight.py $Demo $Profile "/reports/$reportId.json"
if ($LASTEXITCODE -ne 0) { throw "Capacity check failed; inspect artifacts/$Demo/preflight/$reportId.json" }
