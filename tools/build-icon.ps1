$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$output = Join-Path $projectRoot 'obj\tools'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$builder = Join-Path $output 'IconBuilder.exe'
$compilerArgs = @('/nologo','/target:exe','/optimize+',('/out:' + $builder))
foreach ($ref in @('System.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')) { $compilerArgs += '/reference:' + (Join-Path $framework $ref) }
$compilerArgs += Join-Path $PSScriptRoot 'IconBuilder.cs'
& (Join-Path $framework 'csc.exe') @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Icon builder failed.' }
& $builder (Join-Path $projectRoot 'assets\QuietPhotos.xaml') (Join-Path $projectRoot 'assets\icons')
if ($LASTEXITCODE -ne 0) { throw 'Icon rendering failed.' }
