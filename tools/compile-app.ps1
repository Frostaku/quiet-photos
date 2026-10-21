param([switch]$Test, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework 4.x compiler was not found.' }
$output = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { Join-Path $projectRoot 'obj\build\app' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
& (Join-Path $projectRoot 'tools\build-icon.ps1')
$references = @('System.dll','System.Core.dll','System.Xaml.dll','System.Windows.Forms.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$compilerArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+','/warn:4',('/out:' + (Join-Path $output 'QuietPhotos.exe')),('/win32manifest:' + (Join-Path $projectRoot 'src\app.manifest')),('/resource:' + (Join-Path $projectRoot 'src\Theme.xaml') + ',QuietPhotos.Theme.xaml'))
$compilerArgs += '/win32icon:' + (Join-Path $projectRoot 'assets\icons\QuietPhotos.ico')
$compilerArgs += '/resource:' + (Join-Path $projectRoot 'assets\icons\QuietPhotos.ico') + ',QuietPhotos.Icon.ico'
$compilerArgs += '/resource:' + (Join-Path $projectRoot 'assets\QuietPhotos.xaml') + ',QuietPhotos.Icon.xaml'
foreach ($reference in $references) { $compilerArgs += '/reference:' + (Join-Path $framework $reference) }
$compilerArgs += Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\QuietPhotos.exe.config') -Destination $output
Write-Host ('Built ' + (Join-Path $output 'QuietPhotos.exe'))
& (Join-Path $projectRoot 'codecs\build-worker.ps1') -OutputDirectory (Join-Path $output 'codecs')
if ($Test) {
    & (Join-Path $projectRoot 'codecs\test-worker.ps1') -WorkerDirectory (Join-Path $output 'codecs')
    & (Join-Path $projectRoot 'tests\test.ps1') -AppDirectory $output
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
