param([switch]$CloseExisting)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$output = Join-Path $projectRoot 'obj\installer-ui'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
Add-Type -Path (Join-Path $framework 'UIAutomationClient.dll')
Add-Type -Path (Join-Path $framework 'UIAutomationTypes.dll')
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Runtime.InteropServices;
public static class InstallerCapture {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
  [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
  public static void Save(IntPtr handle, string path) {
    RECT r; if (!GetWindowRect(handle, out r)) throw new Exception("No installer window bounds.");
    using (var image = new Bitmap(r.Right-r.Left, r.Bottom-r.Top)) using (var graphics = Graphics.FromImage(image)) {
      IntPtr dc = graphics.GetHdc(); bool ok; try { ok=PrintWindow(handle, dc, 0); } finally { graphics.ReleaseHdc(dc); }
      if (!ok) throw new Exception("Installer window capture failed."); image.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
  }
}
'@
if ($CloseExisting) {
    Get-Process msiexec -ErrorAction SilentlyContinue | Where-Object MainWindowTitle -eq 'Quiet Photos Setup' | ForEach-Object {
        $setupRoot = [Windows.Automation.AutomationElement]::FromHandle($_.MainWindowHandle)
        $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty,'Cancel')
        $cancelButton = $setupRoot.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
        if ($cancelButton) { [void][InstallerCapture]::SendMessage([IntPtr]$cancelButton.Current.NativeWindowHandle, 0xF5, [IntPtr]::Zero, [IntPtr]::Zero); $null = $_.WaitForExit(5000) }
    }
    return
}
$msi = Join-Path $projectRoot 'dist\QuietPhotos-Setup.msi'
$process = Start-Process -FilePath 'msiexec.exe' -ArgumentList @('/i',('"' + $msi + '"'),'/l*v',('"' + (Join-Path $output 'ui.log') + '"')) -PassThru
$timer = [Diagnostics.Stopwatch]::StartNew()
$root = $null
try {
    while ($timer.Elapsed.TotalSeconds -lt 15 -and !$root) {
        $process.Refresh()
        if ($process.HasExited) { throw ('Installer exited with ' + $process.ExitCode) }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            $candidate = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty,'Install')
            $installButton = $candidate.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
            if ($installButton) { $root = $candidate }
        }
        if (!$root) { Start-Sleep -Milliseconds 80 }
    }
    if (!$root) { throw 'Welcome dialog did not appear.' }
    Start-Sleep -Milliseconds 350
    $process.Refresh()
    [InstallerCapture]::Save($process.MainWindowHandle,(Join-Path $output 'welcome.png'))
    $condition = New-Object Windows.Automation.PropertyCondition([Windows.Automation.AutomationElement]::NameProperty,'Cancel')
    $cancel = $root.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
    if (!$cancel) { throw 'Cancel control is missing.' }
    $handle = [IntPtr]$cancel.Current.NativeWindowHandle
    if ($handle -eq [IntPtr]::Zero) { throw 'Cancel has no native button handle.' }
    [void][InstallerCapture]::SendMessage($handle, 0xF5, [IntPtr]::Zero, [IntPtr]::Zero)
    if (!$process.WaitForExit(5000)) { throw 'Installer did not exit after Cancel.' }
    if ($process.ExitCode -ne 1602) { throw ('Cancel returned unexpected code ' + $process.ExitCode) }
    'PASS: Native welcome dialog renders, exposes Install/Cancel, and Cancel exits without installation.'
} finally {
    if (!$process.HasExited) { $null = $process.CloseMainWindow(); $null = $process.WaitForExit(2000) }
    $process.Dispose()
}
