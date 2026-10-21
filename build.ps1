param([switch]$Test)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-release.ps1') -Test:$Test
