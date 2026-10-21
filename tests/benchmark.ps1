param([string]$ImagePath)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (!$ImagePath) { $ImagePath = Join-Path $PSScriptRoot 'output\fixtures\Coast 10.png' }
$ImagePath = (Resolve-Path -LiteralPath $ImagePath).Path
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
Add-Type -Path (Join-Path $framework 'UIAutomationClient.dll')
Add-Type -Path (Join-Path $framework 'UIAutomationTypes.dll')
$measurements = @()
for ($runNumber = 1; $runNumber -le 3; $runNumber++) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath (Join-Path $projectRoot 'dist\QuietPhotos\QuietPhotos.exe') -ArgumentList ('"' + $ImagePath + '"') -PassThru
    try {
        $ready = $false
        while ($timer.ElapsedMilliseconds -lt 15000 -and !$ready) {
            $process.Refresh()
            if ($process.HasExited) { throw 'Viewer exited before image loaded.' }
            if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
                $element = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
                $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, 'Copy image (Ctrl+C)')
                $button = $element.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
                $ready = $null -ne $button -and $button.Current.IsEnabled
            }
            if (!$ready) { Start-Sleep -Milliseconds 20 }
        }
        if (!$ready) { throw 'Timed out waiting for loaded image.' }
        $timer.Stop()
        Start-Sleep -Milliseconds 600
        $process.Refresh()
        $measurements += [pscustomobject]@{ Run = $runNumber; ReadyMilliseconds = $timer.ElapsedMilliseconds; WorkingSetMiB = [Math]::Round($process.WorkingSet64 / 1MB, 1); PrivateMiB = [Math]::Round($process.PrivateMemorySize64 / 1MB, 1) }
    }
    finally {
        if (!$process.HasExited) { $null = $process.CloseMainWindow(); if (!$process.WaitForExit(5000)) { $process.Kill() } }
        $process.Dispose()
    }
}
$measurements | Format-Table
$measurements | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'output\benchmark.json') -Encoding UTF8
