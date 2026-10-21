Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'

function Test-PhotosAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try { return (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
    finally { $identity.Dispose() }
}

function Get-PhotosPackages([switch]$AllUsers) {
    $parameters = @{ Name = 'Microsoft.Windows.Photos'; PackageTypeFilter = @('Main','Bundle'); ErrorAction = 'Stop' }
    if ($AllUsers) { $parameters.AllUsers = $true }
    # Match Microsoft's exact package identity, not a display-name substring.
    @(Get-AppxPackage @parameters | Where-Object {
        $_.Name -ceq 'Microsoft.Windows.Photos' -and
        $_.PackageFamilyName -ceq 'Microsoft.Windows.Photos_8wekyb3d8bbwe' -and
        $_.PackageFullName -cmatch '^Microsoft\.Windows\.Photos_[0-9.]+_(?:x64|x86|arm64|arm|neutral)_[^_]*_8wekyb3d8bbwe$'
    } | Sort-Object -Property @{Expression='IsBundle';Descending=$true},PackageFullName -Unique)
}

function Get-PhotosProvisioning {
    @(Get-AppxProvisionedPackage -Online -ErrorAction Stop | Where-Object {
        $_.DisplayName -ceq 'Microsoft.Windows.Photos' -and
        $_.PackageName -cmatch '^Microsoft\.Windows\.Photos_[0-9.]+_(?:x64|x86|arm64|arm|neutral)_[^_]*_8wekyb3d8bbwe$'
    })
}

function Get-PhotosAccountScan {
    $packages = @(Get-PhotosPackages)
    [pscustomobject]@{ Found = $packages.Count -gt 0; Scope = 'CurrentAccount'; Count = $packages.Count }
}

function Invoke-PhotosMachineRemoval([switch]$Consent) {
    if (!$Consent) { throw 'Removing Microsoft Photos requires an explicit choice in setup.' }
    if (!(Test-PhotosAdministrator)) { throw 'Machine-wide Photos removal requires administrator approval.' }
    # Inventory both scopes before changing either, so a failed scan cannot look like absence.
    $packages = @(Get-PhotosPackages -AllUsers)
    $provisioned = @(Get-PhotosProvisioning)
    $failures = New-Object 'System.Collections.Generic.List[string]'
    foreach ($package in $packages) {
        try {
            # Removing a parent bundle may also remove a main package listed in the original inventory.
            $remaining = @(Get-PhotosPackages -AllUsers | Where-Object PackageFullName -CEQ $package.PackageFullName)
            if ($remaining.Count -gt 0) { Remove-AppxPackage -Package $package.PackageFullName -AllUsers -ErrorAction Stop }
        } catch { $failures.Add($_.Exception.Message) }
    }
    foreach ($package in $provisioned) {
        try { Remove-AppxProvisionedPackage -Online -PackageName $package.PackageName -ErrorAction Stop | Out-Null }
        catch { $failures.Add($_.Exception.Message) }
    }
    $remainingPackages = @(Get-PhotosPackages -AllUsers)
    $remainingProvisioning = @(Get-PhotosProvisioning)
    if ($remainingPackages.Count -gt 0 -or $remainingProvisioning.Count -gt 0) {
        $detail = if ($failures.Count -gt 0) { ($failures | Select-Object -Unique) -join "`n" } else { 'Windows still reports a Microsoft Photos package or provisioned copy.' }
        throw ('Microsoft Photos could not be completely removed. Quiet Photos remains installed.' + "`n`n" + $detail)
    }
    [pscustomobject]@{ Success = $true; MatchedPackages = $packages.Count; MatchedProvisioning = $provisioned.Count }
}
