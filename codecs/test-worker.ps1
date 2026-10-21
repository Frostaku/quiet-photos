param([string]$WorkerDirectory=(Join-Path $PSScriptRoot '../bin/codecs'),[string]$FixtureDirectory=(Join-Path $PSScriptRoot '../tests/output/codecs'))
$ErrorActionPreference='Stop'
$WorkerDirectory=[IO.Path]::GetFullPath($WorkerDirectory)
& (Join-Path $PSScriptRoot 'build-worker.ps1') -OutputDirectory $WorkerDirectory
$framework=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319'
$runtime=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../tests/output/codec-test-runtime'))
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
foreach($name in @('Magick.NET.Core.dll','Magick.NET-Q8-x64.dll','Magick.Native-Q8-x64.dll')){Copy-Item -LiteralPath (Join-Path $WorkerDirectory $name) -Destination $runtime -Force}
$testExe=Join-Path $runtime 'WorkerTests.exe'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /langversion:5 "/out:$testExe" /r:System.dll /r:System.Core.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll "/r:$framework/netstandard.dll" "/r:$WorkerDirectory/Magick.NET.Core.dll" "/r:$WorkerDirectory/Magick.NET-Q8-x64.dll" (Join-Path $PSScriptRoot 'WorkerTests.cs')
if($LASTEXITCODE -ne 0){throw 'Worker tests failed to compile.'}
$fixtures=[IO.Path]::GetFullPath($FixtureDirectory)
if(Test-Path -LiteralPath $fixtures){
    $allowed=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../tests/output'))+[IO.Path]::DirectorySeparatorChar
    if(-not $fixtures.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe fixture path.'}
    Remove-Item -LiteralPath $fixtures -Recurse -Force
}
& $testExe (Join-Path $WorkerDirectory 'QuietPhotos.Decode.exe') $fixtures
$testExit=$LASTEXITCODE
Remove-Item -LiteralPath $testExe -Force
foreach($name in @('Magick.NET.Core.dll','Magick.NET-Q8-x64.dll','Magick.Native-Q8-x64.dll')){Remove-Item -LiteralPath (Join-Path $runtime $name) -Force}
Remove-Item -LiteralPath $runtime -Force
if($testExit -ne 0){throw 'Codec worker checks failed.'}
