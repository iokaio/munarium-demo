# SPDX-License-Identifier: Apache-2.0
#Requires -Version 7
[CmdletBinding()]
param([int]$HttpPort = 5310, [int]$HttpsPort = 7310)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'Sync-Assets.ps1')
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet watch --project (Join-Path $PSScriptRoot 'src/Demo.Web') run --launch-profile https --urls "https://localhost:$HttpsPort;http://localhost:$HttpPort"
if ($LASTEXITCODE -ne 0) { throw 'dotnet watch failed' }
