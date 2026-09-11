# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param([ValidateSet('test','cloud','stop')][string]$Action='test',[string]$Project='records-wave2')
$ErrorActionPreference='Stop'
if ($Project -notmatch '^records-[a-z0-9-]+$') { throw 'Use an isolated records- project name.' }
if ($Action -ne 'stop') {
    $fixtureProfile = if ($env:DEMO_PROFILE) { $env:DEMO_PROFILE } else { 'default' }
    & (Join-Path $PSScriptRoot '../../tools/demo_preflight.ps1') -Demo 'records-intake' -Profile $fixtureProfile
}
Push-Location $PSScriptRoot
try {
    $endpoint=docker context inspect --format '{{.Endpoints.docker.Host}}'
    if ($LASTEXITCODE -ne 0 -or $endpoint -notmatch '^(npipe|unix)://') { throw 'Select a local Docker context.' }
    if ((docker info --format '{{.OSType}}') -ne 'linux') { throw 'Use Linux containers.' }
    function Invoke-RecordIntake([string[]]$Arguments) { & docker compose --env-file ../../.env.local.sample -p $Project -f compose.yaml @Arguments; if ($LASTEXITCODE -ne 0) { throw 'Records intake step failed; inspect retained artifacts.' } }
    Invoke-RecordIntake @('config','--quiet')
    if ($Action -eq 'stop') { Invoke-RecordIntake @('stop'); return }
    if ($Action -eq 'cloud') { Write-Output 'Not applicable: records intake has no model calls. Use test for the complete suite.'; return }
    $run=[guid]::NewGuid().ToString('N'); $report="/work/test/$run"; $local="../../artifacts/records-intake/test/$run"
    New-Item -ItemType Directory -Force -Path $local | Out-Null
    docker ps --format '{{.Names}} {{.Image}} {{.Status}}' | Set-Content "$local/containers-before.txt"
    git rev-parse HEAD | Set-Content "$local/demo-revision.txt"
    docker info --format '{{.OSType}} {{.Architecture}} {{.NCPU}} {{.MemTotal}}' | Set-Content "$local/host.txt"
    "PowerShell local.ps1 -Action $Action -Project $Project; $([Environment]::OSVersion)" | Set-Content "$local/command.txt"
    Invoke-RecordIntake @('build','tests')
    docker image inspect munarium-records-runner:local --format '{{.Id}} {{.Size}}' | Set-Content "$local/runner.txt"
    Invoke-RecordIntake @('run','--rm','--no-deps','-e',"RECORDS_REPORT_DIR=$report/unit",'unit','unit')
    Invoke-RecordIntake @('up','-d','server','faults')
    Invoke-RecordIntake @('run','--rm','--no-deps','generator')
    Invoke-RecordIntake @('run','--rm','--no-deps','-e',"RECORDS_RUN_ID=$run",'bootstrap')
    Invoke-RecordIntake @('run','--rm','--no-deps','-e',"RECORDS_REPORT_DIR=$report/controlled",'tests','controlled')
    Invoke-RecordIntake @('restart','server')
    Invoke-RecordIntake @('run','--rm','--no-deps','-e',"RECORDS_REPORT_DIR=$report/controlled",'tests','restarted')
    Invoke-RecordIntake @('run','--rm','--no-deps','-e',"RECORDS_REPORT_DIR=$report/sdk",'tests','qualify')
    Invoke-RecordIntake @('run','--rm','--no-deps','--entrypoint','python3','app','/app/render.py',"$report/controlled/case-001/status.txt","$report/application.png")
    Write-Output "Reports: artifacts/records-intake/test/$run"
} finally { Pop-Location }
