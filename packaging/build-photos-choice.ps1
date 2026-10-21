param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$OutputDirectory) { $OutputDirectory = Join-Path $projectRoot 'obj\installer' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+',('/out:' + (Join-Path $OutputDirectory 'PhotosChoice.exe')))
foreach ($reference in @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll')) { $compilerArgs += '/reference:' + (Join-Path $framework $reference) }
$compilerArgs += '/win32manifest:' + (Join-Path $projectRoot 'src\app.manifest')
$compilerArgs += '/win32icon:' + (Join-Path $projectRoot 'assets\icons\QuietPhotos.ico')
$compilerArgs += '/resource:' + (Join-Path $projectRoot 'assets\icons\QuietPhotos.ico') + ',QuietPhotos.Icon.ico'
$compilerArgs += '/resource:' + (Join-Path $PSScriptRoot 'PhotosPackages.ps1') + ',QuietPhotos.PhotosPackages.ps1'
$compilerArgs += Join-Path $PSScriptRoot 'PhotosChoice.cs'
& (Join-Path $framework 'csc.exe') @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Photos choice companion build failed.' }
