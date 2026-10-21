$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PhotosPackages.ps1')
Get-PhotosAccountScan | ConvertTo-Json -Compress
