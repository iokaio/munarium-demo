# SPDX-License-Identifier: Apache-2.0
# Start or resume a caller-selected index run against an operator-supplied Server.
# Use -PauseEach to inspect every cutover; otherwise this script approves the
# selected run automatically. A client timeout is not proof that work continues.
# Inspect run/step state before starting a replacement run. The bundled history
# runbook uses collectionMajor execution to bound work between approval gates.
param(
    [string]$Runbook,
    [string]$RunId,
    [switch]$PauseEach,
    [string]$BaseUrl = $(if ($env:MUNARIUM_BASE_URL) { $env:MUNARIUM_BASE_URL } else { 'http://localhost:8080' }),
    [string]$Uid = $(if ($env:MUNARIUM_UID) { $env:MUNARIUM_UID } else { 'bulk-loader' }),
    [int]$TimeoutSec = 590,
    # Give up waiting for a run that never leaves `running`. 0 = wait forever,
    # which is what this script did unconditionally until 2026-09-04.
    [int]$MaxWaitMinutes = 120
)
#
# .EXITCODE
#   0  the run finished
#   1  the run failed
#   4  the run was still going at -MaxWaitMinutes; re-attach with -RunId

$ErrorActionPreference = 'Stop'
if (-not $env:MUNARIUM_TOKEN) { throw 'set MUNARIUM_TOKEN (rw token)' }
if (-not $Runbook -and -not $RunId) { throw 'pass -Runbook <name> or -RunId <run-id>' }
$H = @{ Authorization = "Bearer $($env:MUNARIUM_TOKEN)"; 'X-Munarium-Uid' = $Uid }
$B = $BaseUrl.TrimEnd('/')

function Get-Run([string]$id) {
    Invoke-RestMethod "$B/v1/runs/$id" -Headers $H -TimeoutSec 60
}

if (-not $RunId) {
    Write-Host "starting run of '$Runbook' ..."
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $run = Invoke-RestMethod -Method Post "$B/v1/runbooks/$Runbook/runs" -Headers $H -TimeoutSec $TimeoutSec
    } catch [System.Net.WebException], [System.Threading.Tasks.TaskCanceledException] {
        Write-Warning "run request timed out client-side after $TimeoutSec s."
        Write-Warning 'The in-flight step is almost certainly DEAD, not still running: a client'
        Write-Warning 'disconnect drops the server-side request future and wedges that step at'
        Write-Warning "'running'. Check its updated_at; if it is not advancing, the run is stuck"
        Write-Warning 'and the corpus is too large to build inside one request (see header note).'
        throw
    }
    $sw.Stop()
    $RunId = $run.run_id
    Write-Host ("run {0} -> {1} after {2:n1}s" -f $RunId, $run.state, $sw.Elapsed.TotalSeconds)
} else {
    $run = Get-Run $RunId
    Write-Host ("resuming run {0} (state: {1})" -f $RunId, $run.state)
}

# A DEADLINE on the poll. The header documents that a client disconnect can
# wedge a step at `running` with no resume path — and until 2026-09-04 the
# `running` branch below just slept and re-polled forever, so the script hung on
# the exact condition it warns about instead of reporting it. -MaxWaitMinutes 0
# restores the old unbounded wait for a genuinely long build.
$pollDeadline = if ($MaxWaitMinutes -gt 0) { (Get-Date).AddMinutes($MaxWaitMinutes) } else { [datetime]::MaxValue }

while ($true) {
    if ((Get-Date) -gt $pollDeadline) {
        Write-Host ''
        Write-Host ("run {0} is still '{1}' after {2} minute(s)." -f $RunId, $(if ($run) { $run.state } else { 'unknown' }), $MaxWaitMinutes) -ForegroundColor Red
        Write-Host -Object 'Inspect the running step before retrying; a disconnected request may have stopped execution.' -ForegroundColor Yellow
        Write-Host ("  Re-attach later with:  .\drive-run.ps1 -RunId {0}" -f $RunId) -ForegroundColor Yellow
        exit 4
    }
    $run = Get-Run $RunId
    switch ($run.state) {
        'done' {
            Write-Host "run $RunId DONE" -ForegroundColor Green
            $run.steps | ForEach-Object { Write-Host ("  {0,3}  {1,-28} {2}" -f $_.ordinal, $_.name, $_.state) }
            return
        }
        'failed' {
            Write-Host "run $RunId FAILED" -ForegroundColor Red
            $run.steps | Where-Object { $_.state -ne 'done' } | ForEach-Object {
                Write-Host ("  {0,3}  {1,-28} {2}  {3}" -f $_.ordinal, $_.name, $_.state, $_.detail)
            }
            exit 1
        }
        'awaiting_approval' {
            $step = $run.steps | Where-Object { $_.state -eq 'awaiting_approval' } | Select-Object -First 1
            if (-not $step) { throw "run awaiting approval but no step reports it: $($run | ConvertTo-Json -Depth 4)" }
            if ($PauseEach) {
                $answer = Read-Host ("approve step {0} ({1})? [y/N/q]" -f $step.ordinal, $step.name)
                if ($answer -eq 'q') { Write-Host "stopping; resume with -RunId $RunId"; return }
                if ($answer -ne 'y') {
                    # Declining re-polls and re-prompts. Without the pause that
                    # is a tight loop on the console, which reads as a hang.
                    Start-Sleep -Seconds 5
                    continue
                }
            }
            Write-Host ("approving step {0} ({1}) ..." -f $step.ordinal, $step.name)
            $sw = [System.Diagnostics.Stopwatch]::StartNew()
            try {
                Invoke-RestMethod -Method Post "$B/v1/runs/$RunId/steps/$($step.ordinal)/approve" -Headers $H -TimeoutSec $TimeoutSec | Out-Null
                $sw.Stop()
                Write-Host ("  step {0} approved+executed in {1:n1}s" -f $step.ordinal, $sw.Elapsed.TotalSeconds)
            } catch {
                $sw.Stop()
                Write-Warning ("  approve {0} errored/timed out client-side after {1:n1}s: {2}" -f $step.ordinal, $sw.Elapsed.TotalSeconds, $_.Exception.Message)
                Write-Warning '  the server-side step may still be running; re-polling in 30s'
                Start-Sleep -Seconds 30
            }
        }
        'running' {
            Start-Sleep -Seconds 5
        }
        default {
            Write-Host ("run {0} state: {1}" -f $RunId, $run.state)
            Start-Sleep -Seconds 5
        }
    }
}
