param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../bin/codecs'))
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$vendor = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../vendor/codecs'))
$framework = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$references = @('System.dll','System.Core.dll','System.Xml.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll',(Join-Path $framework 'netstandard.dll'),(Join-Path $vendor 'Magick.NET.Core.dll'),(Join-Path $vendor 'Magick.NET-Q8-x64.dll'))
$compilerArgs = @('/nologo','/target:exe','/platform:x64','/optimize+','/langversion:5',('/out:' + (Join-Path $OutputDirectory 'QuietPhotos.Decode.exe')))
foreach ($reference in $references) { $compilerArgs += '/reference:' + $reference }
$compilerArgs += Join-Path $PSScriptRoot 'DecodeWorker.cs'
& (Join-Path $framework 'csc.exe') @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Decode worker compilation failed.' }
foreach ($name in @('Magick.NET.Core.dll','Magick.NET-Q8-x64.dll','Magick.Native-Q8-x64.dll','Notice.txt')) { Copy-Item -LiteralPath (Join-Path $vendor $name) -Destination $OutputDirectory -Force }
# Initialize(path) requires the complete configuration set. Export the defaults at
# build time so running the worker never creates persistent configuration folders.
[void][Reflection.Assembly]::LoadFrom((Join-Path $vendor 'Magick.NET.Core.dll'))
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $vendor 'Magick.NET-Q8-x64.dll'))
$configuration=$assembly.GetType('ImageMagick.Configuration.ConfigurationFiles').GetProperty('Default').GetValue($null,$null)
foreach($file in $configuration.All){[IO.File]::WriteAllText((Join-Path $OutputDirectory $file.FileName),$file.Data)}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'policy.xml') -Destination $OutputDirectory -Force
Write-Host "Decode worker built: $OutputDirectory"
