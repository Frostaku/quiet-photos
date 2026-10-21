param([switch]$Test)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
function Remove-BuildOutput([string]$RelativePath) {
    $target = [IO.Path]::GetFullPath((Join-Path $root $RelativePath))
    if (!$target.StartsWith($root + '\',[StringComparison]::OrdinalIgnoreCase) -or $target -eq $root) { throw ('Unsafe build cleanup path: ' + $target) }
    if (Test-Path -LiteralPath $target) {
        $resolved = (Resolve-Path -LiteralPath $target).Path
        if (!$resolved.StartsWith($root + '\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Build cleanup resolved outside the project.' }
        $links = @(Get-Item -LiteralPath $resolved) + @(Get-ChildItem -LiteralPath $resolved -Recurse -Force)
        if (@($links | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }).Count -gt 0) { throw ('Build cleanup refuses linked paths: ' + $resolved) }
        Remove-Item -LiteralPath $resolved -Recurse -Force -ErrorAction Stop
    }
}
# Stage both packages before touching the previously usable release.
$work = Join-Path $root 'obj\build'
Remove-BuildOutput 'obj\build'
$release = Join-Path $work 'app'
$stage = Join-Path $work 'dist'
& (Join-Path $root 'tools\compile-app.ps1') -OutputDirectory $release -Test:$Test
& (Join-Path $root 'packaging\build-installer.ps1') -AppDirectory $release -OutputDirectory $stage
$portable = Join-Path $stage 'QuietPhotos'
$portableDocs = Join-Path $portable 'docs'
New-Item -ItemType Directory -Path $portable,$portableDocs -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $release 'QuietPhotos.exe'),(Join-Path $release 'QuietPhotos.exe.config'),(Join-Path $PSScriptRoot 'README.md'),(Join-Path $PSScriptRoot 'assets\icons\QuietPhotos.ico') -Destination $portable
Copy-Item -LiteralPath (Join-Path $release 'codecs') -Destination $portable -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\rea-findings.md'),(Join-Path $PSScriptRoot 'docs\validation.md'),(Join-Path $PSScriptRoot 'docs\installer.md') -Destination $portableDocs
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\viewer-research.md') -Destination $portableDocs
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs\decoder-dependencies.md') -Destination $portableDocs
Compress-Archive -Path (Join-Path $portable '*') -DestinationPath (Join-Path $stage 'QuietPhotos-portable.zip') -Force
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
Copy-Item -Path (Join-Path $stage '*') -Destination $dist -Recurse -Force
# Retire only known generated versioned installers, preserving arbitrary files in dist.
foreach ($oldInstaller in (Get-ChildItem -LiteralPath $dist -File | Where-Object Name -Match '^QuietPhotos-(?:Setup-)?\d+\.\d+\.\d+(?:-x64)?\.msi$')) { Remove-BuildOutput ('dist\' + $oldInstaller.Name) }
Remove-BuildOutput 'obj'
if ($Test) { Remove-BuildOutput 'tests\output' }
foreach ($size in @(16,20,24,32,40,48,64,128,256)) { Remove-BuildOutput ('assets\icons\QuietPhotos-' + $size + '.png') }
Write-Host 'Updated dist\QuietPhotos\QuietPhotos.exe, dist\QuietPhotos-portable.zip and dist\QuietPhotos-Setup.msi. Temporary build files removed.'
