param([switch]$FullUI)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$msi = Join-Path $projectRoot 'dist\QuietPhotos-Setup.msi'
$productCode = '{9AB8A696-7047-4A72-B46E-FB18A25D2DBE}'
$installer = New-Object -ComObject WindowsInstaller.Installer
if ($installer.ProductState($productCode) -ne -1) { throw 'This product is already registered. Test stopped to preserve the existing installation.' }
$photosBefore = @(Get-AppxPackage -Name Microsoft.Windows.Photos -PackageTypeFilter Main,Bundle | Select-Object -ExpandProperty PackageFullName | Sort-Object)
$testRoot = Join-Path $projectRoot 'obj\installer-smoke'
$resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
if (!$resolvedTestRoot.StartsWith([IO.Path]::GetFullPath($projectRoot).TrimEnd('\') + '\obj\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe installer test folder.' }
if (Test-Path -LiteralPath $resolvedTestRoot) { Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force -ErrorAction Stop }
$appDir = Join-Path $testRoot 'application'
$menuDir = Join-Path $testRoot 'start-menu'
$desktopDir = Join-Path $testRoot 'desktop'
New-Item -ItemType Directory -Path $testRoot,$menuDir,$desktopDir -Force | Out-Null
$log = Join-Path $testRoot 'install.log'
$uninstallLog = Join-Path $testRoot 'uninstall.log'
$ui = if ($FullUI) { '/qf' } else { '/qn' }
$arguments = @('/i',('"' + $msi + '"'),$ui,'ALLUSERS=2','MSIINSTALLPERUSER=1',('INSTALLDIR="' + $appDir + '\"'),('QPMENUROOT="' + $menuDir + '\"'),('QPDESKTOPROOT="' + $desktopDir + '\"'),'/l*v',('"' + $log + '"'))
$installed = $false
try {
    if (!$FullUI) { $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden }
    else {
        $framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
        Add-Type -Path (Join-Path $framework 'UIAutomationClient.dll')
        Add-Type -Path (Join-Path $framework 'UIAutomationTypes.dll')
        Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class TestPhotosUi { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l); }'
        function Find-Control($Root,[string]$Name) {
            $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty,$Name)
            $Root.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
        }
        function Click-Control($Control) {
            if (!$Control) { throw 'Expected setup control is missing.' }
            [void][TestPhotosUi]::PostMessage([IntPtr]$Control.Current.NativeWindowHandle,0xF5,[IntPtr]::Zero,[IntPtr]::Zero)
        }
        $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments -PassThru
        $timer = [Diagnostics.Stopwatch]::StartNew(); $stage = 0
        while (!$process.HasExited -and $timer.Elapsed.TotalSeconds -lt 60) {
            $process.Refresh()
            $root = if ($process.MainWindowHandle -ne [IntPtr]::Zero) { [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle) } else { $null }
            if ($stage -eq 0 -and $root) {
                $button = Find-Control $root 'Install'
                if ($button) { Click-Control $button; $stage = 1 }
            }
            elseif ($stage -eq 1) {
                $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty,'Quiet Photos Setup - Microsoft Photos')
                $choice = [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Children,$condition)
                if ($choice) {
                    $checkbox = Find-Control $choice 'Uninstall Microsoft Photos from this PC (all accounts)'
                    if (!$checkbox) { $checkbox = Find-Control $choice 'Scan all accounts and uninstall Microsoft Photos if found' }
                    if ($checkbox -and $checkbox.Current.IsEnabled) {
                        if ($photosBefore.Count -gt 0 -and !(Find-Control $choice 'Microsoft Photos was found in your Windows account.')) { throw 'The helper did not report the known Photos installation.' }
                        $checked = [TestPhotosUi]::SendMessage([IntPtr]$checkbox.Current.NativeWindowHandle,0xF0,[IntPtr]::Zero,[IntPtr]::Zero)
                        if ($checked -ne [IntPtr]::Zero) { throw 'Photos removal must be unchecked by default.' }
                        Click-Control (Find-Control $choice 'Keep Microsoft Photos'); $stage = 2
                        'PASS: Successful interactive install scanned Photos, left removal unchecked, and accepted Keep Photos.'
                    }
                }
            }
            elseif ($stage -eq 2 -and $root) {
                $button = Find-Control $root 'Finish'
                if ($button) { Click-Control $button; $stage = 3 }
            }
            Start-Sleep -Milliseconds 100
        }
        if (!$process.HasExited) { throw ('Interactive setup timed out at stage ' + $stage) }
        if ($stage -ne 3) { throw ('Interactive setup exited before completing Photos choice, stage ' + $stage) }
    }
    if ($process.ExitCode -ne 0) { throw ('Install returned ' + $process.ExitCode + '. Log: ' + $log) }
    $installed = $true
    $exe = Join-Path $appDir 'QuietPhotos.exe'
    if ((Get-FileHash -LiteralPath $exe).Hash -ne (Get-FileHash -LiteralPath (Join-Path $projectRoot 'dist\QuietPhotos\QuietPhotos.exe')).Hash) { throw 'Installed executable differs from release build.' }
    if (!(Test-Path -LiteralPath (Join-Path $appDir 'QuietPhotos.ico'))) { throw 'Installed icon is missing.' }
    $codecFiles = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'dist\QuietPhotos\codecs') -File)
    foreach ($codec in $codecFiles) {
        $installedCodec = Join-Path $appDir ('codecs\' + $codec.Name)
        if (!(Test-Path -LiteralPath $installedCodec) -or (Get-FileHash -LiteralPath $installedCodec).Hash -ne (Get-FileHash -LiteralPath $codec.FullName).Hash) { throw ('Installed decoder payload differs: ' + $codec.Name) }
    }
    if ($codecFiles.Count -lt 5) { throw 'Decoder payload is incomplete.' }
    $supportedTypes = Get-Item -LiteralPath 'HKCU:\Software\Classes\Applications\QuietPhotos.exe\SupportedTypes'
    foreach ($extension in @('.psd','.xcf','.webp','.heic','.dng','.kra')) {
        if ($supportedTypes.GetValueNames() -notcontains $extension) { throw ('Missing Open with type: ' + $extension) }
    }
    $menuShortcut = Join-Path $menuDir 'Quiet Photos\Quiet Photos.lnk'
    $desktopShortcut = Join-Path $desktopDir 'Quiet Photos.lnk'
    foreach ($shortcutPath in @($menuShortcut,$desktopShortcut)) {
        if (!(Test-Path -LiteralPath $shortcutPath)) { throw ('Missing shortcut: ' + $shortcutPath) }
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        if ($shortcut.TargetPath -ne $exe) { throw 'Shortcut points to an unexpected application.' }
    }
    if ($installer.ProductState($productCode) -ne 5) { throw 'Windows Installer did not register the product.' }
    $sentinel = Join-Path $appDir 'user-photo.txt'; [IO.File]::WriteAllText($sentinel,'An unrelated user file must survive uninstall.')
    'PASS: Installed payload, product registration, icon, Start-menu and desktop shortcuts.'
} finally {
    if ($installed -or $installer.ProductState($productCode) -eq 5) {
        $removeArgs = @('/x',$productCode,'/qn',('INSTALLDIR="' + $appDir + '\"'),('QPMENUROOT="' + $menuDir + '\"'),('QPDESKTOPROOT="' + $desktopDir + '\"'),'/l*v',('"' + $uninstallLog + '"'))
        $remove = Start-Process -FilePath 'msiexec.exe' -ArgumentList $removeArgs -Wait -PassThru -WindowStyle Hidden
        if ($remove.ExitCode -ne 0) { throw ('Uninstall returned ' + $remove.ExitCode + '. Log: ' + $uninstallLog) }
    }
}
if (Test-Path -LiteralPath (Join-Path $appDir 'QuietPhotos.exe')) { throw 'Uninstall left the executable.' }
foreach ($codec in $codecFiles) { if (Test-Path -LiteralPath (Join-Path $appDir ('codecs\' + $codec.Name))) { throw ('Uninstall left decoder: ' + $codec.Name) } }
if (Test-Path -LiteralPath $menuShortcut) { throw 'Uninstall left the Start-menu shortcut.' }
if (Test-Path -LiteralPath $desktopShortcut) { throw 'Uninstall left the desktop shortcut.' }
if (!(Test-Path -LiteralPath $sentinel)) { throw 'Uninstall removed an unrelated user file.' }
if ($installer.ProductState($productCode) -ne -1) { throw 'Uninstall left product registration.' }
$photosAfter = @(Get-AppxPackage -Name Microsoft.Windows.Photos -PackageTypeFilter Main,Bundle | Select-Object -ExpandProperty PackageFullName | Sort-Object)
if (@(Compare-Object $photosBefore $photosAfter).Count -ne 0) { throw 'Microsoft Photos registrations changed during a Keep/silent installer test.' }
'PASS: Real Microsoft Photos registrations were unchanged.'
'PASS: Uninstall removed app and shortcuts, and preserved an unrelated user file.'
Write-Host ('Installer logs: ' + $testRoot)
