param([string]$AppDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$testOutput = Join-Path $PSScriptRoot 'output'
New-Item -ItemType Directory -Path $testOutput -Force | Out-Null
if (!$AppDirectory) { $AppDirectory = Join-Path $projectRoot 'dist\QuietPhotos' }
$testBin = Join-Path $projectRoot 'obj\tests'
New-Item -ItemType Directory -Path $testBin -Force | Out-Null
if ([IO.Path]::GetFullPath($AppDirectory) -ne [IO.Path]::GetFullPath($testBin)) {
    Copy-Item -LiteralPath (Join-Path $AppDirectory 'QuietPhotos.exe'),(Join-Path $AppDirectory 'QuietPhotos.exe.config') -Destination $testBin -Force
    if (Test-Path -LiteralPath (Join-Path $AppDirectory 'codecs')) { Copy-Item -LiteralPath (Join-Path $AppDirectory 'codecs') -Destination $testBin -Recurse -Force }
}
$testExe = Join-Path $testBin 'QuietPhotos.Tests.exe'
$compilerArgs = @('/nologo','/target:exe','/platform:x64','/optimize+',('/out:' + $testExe),('/reference:' + (Join-Path $testBin 'QuietPhotos.exe')))
foreach ($ref in @('System.dll','System.Core.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')) { $compilerArgs += '/reference:' + (Join-Path $framework $ref) }
$compilerArgs += Join-Path $PSScriptRoot 'TestRunner.cs'
& (Join-Path $framework 'csc.exe') @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $testExe $testOutput
if ($LASTEXITCODE -ne 0) { throw 'Application tests failed.' }
