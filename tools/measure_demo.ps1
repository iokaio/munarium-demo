# SPDX-License-Identifier: Apache-2.0
[CmdletBinding()]
param([Parameter(Mandatory)][string]$Demo,[Parameter(Mandatory)][string]$Project,[Alias('Profile')][ValidateSet('default','heldout','stress')][string]$FixtureProfile='default')
$ErrorActionPreference='Stop'
$repoPath=Split-Path $PSScriptRoot -Parent
if ($Demo -notmatch '^[a-z][a-z0-9-]+$' -or $Project -notmatch '^[a-z][a-z0-9-]+$') { throw 'Invalid demo or project name' }
$entry=Join-Path $repoPath "src/$Demo/local.ps1"
if (-not (Test-Path -LiteralPath $entry)) { throw 'Unknown demo' }
if (@(& docker ps -a --filter "label=com.docker.compose.project=$Project" --format '{{.ID}}').Count) { throw 'Use a fresh project for measurement' }
$run=[guid]::NewGuid().ToString('N')
$report=Join-Path $repoPath "artifacts/$Demo/measurements/$run"
New-Item -ItemType Directory -Force -Path $report | Out-Null
$signal=Join-Path $report 'running'
[IO.File]::WriteAllText($signal,'running')
$started=[DateTimeOffset]::UtcNow
$previousProfile=$env:DEMO_PROFILE
$env:DEMO_PROFILE=$FixtureProfile
$monitor=Start-Job -ScriptBlock {
    while (Test-Path -LiteralPath $using:signal) {
        $ids=@(& docker ps --filter "label=com.docker.compose.project=$using:Project" --format '{{.ID}}')
        if ($ids.Count) {
            $stamp=[DateTimeOffset]::UtcNow.ToString('o')
            foreach ($line in (& docker stats --no-stream --format '{{json .}}' @ids 2>$null)) {
                try { @{utc=$stamp;stats=($line | ConvertFrom-Json -ErrorAction Stop)} | ConvertTo-Json -Compress | Add-Content -LiteralPath (Join-Path $using:report 'samples.jsonl') -Encoding utf8 -ErrorAction Stop }
                catch { "$stamp $($_.Exception.Message)" | Add-Content -LiteralPath (Join-Path $using:report 'sampling-errors.log') -Encoding utf8 }
            }
        }
        Start-Sleep -Seconds 2
    }
}
$passed=$false
try {
    & $entry -Action test -Project $Project *>&1 | Tee-Object -FilePath (Join-Path $report 'workflow.log')
    $passed=$true
} finally {
    Remove-Item -LiteralPath $signal -Force
    $monitor | Wait-Job -Timeout 10 | Out-Null
    $monitor | Stop-Job
    $monitor | Remove-Job -Force
    $env:DEMO_PROFILE=$previousProfile
    $images=@(& docker ps -a --filter "label=com.docker.compose.project=$Project" --format '{{.Image}}' | Sort-Object -Unique)
    $imageData=@(foreach ($name in $images) { & docker image inspect $name --format '{{json .}}' | ConvertFrom-Json | Select-Object Id,Size,Architecture,Os })
    $volumeData=@(foreach ($volume in (& docker volume ls --filter "label=com.docker.compose.project=$Project" --format '{{.Name}}')) {
        $size=& docker run --rm --network none --mount "type=volume,source=$volume,target=/data,readonly" python:3.12-slim@sha256:78387bc3881b8273120a12ebe6c1ab22b018ccc2c9adf565ae1ac9b536e184ea du -sb /data
        if ($LASTEXITCODE -ne 0) { throw 'Volume measurement failed' }
        @{name=$volume;logical_bytes=[long](($size -split '\s+')[0])}
    })
    @{demo=$Demo;project=$Project;profile=$FixtureProfile;passed=$passed;started_utc=$started.ToString('o');elapsed_seconds=([DateTimeOffset]::UtcNow-$started).TotalSeconds;images=$imageData;volumes=$volumeData;sampling='docker stats approximately every 3 seconds; short-lived peaks may be missed';download_measurement='Cold downloads are not measured by this cached-workflow runner; image sizes are unpacked local bytes'} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $report 'run.json') -Encoding utf8
    & docker run --rm --network none --mount "type=bind,source=$PSScriptRoot,target=/tools,readonly" --mount "type=bind,source=$report,target=/report" python:3.12-slim@sha256:78387bc3881b8273120a12ebe6c1ab22b018ccc2c9adf565ae1ac9b536e184ea python /tools/summarize_demo.py /report
    if ($LASTEXITCODE -ne 0) { throw 'Resource summary failed; inspect retained samples' }
    Write-Output "Measurement report: artifacts/$Demo/measurements/$run"
}
