$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PhotosPackages.ps1')
$script:checks = 0
function Check([bool]$Condition,[string]$Description) { if (!$Condition) { throw ('FAIL: ' + $Description) }; $script:checks++; Write-Host ('PASS: ' + $Description) }
function Package([string]$Name,[string]$Family,[string]$Full,[bool]$Bundle=$false) { [pscustomobject]@{ Name=$Name; PackageFamilyName=$Family; PackageFullName=$Full; IsBundle=$Bundle } }
$photosMain = Package 'Microsoft.Windows.Photos' 'Microsoft.Windows.Photos_8wekyb3d8bbwe' 'Microsoft.Windows.Photos_2026.1.0.0_x64__8wekyb3d8bbwe'
$photosBundle = Package 'Microsoft.Windows.Photos' 'Microsoft.Windows.Photos_8wekyb3d8bbwe' 'Microsoft.Windows.Photos_2026.1.0.0_neutral_~_8wekyb3d8bbwe' $true
$unrelated = Package 'Microsoft.WindowsCalculator' 'Microsoft.WindowsCalculator_8wekyb3d8bbwe' 'Microsoft.WindowsCalculator_2026.1.0.0_x64__8wekyb3d8bbwe'
$lookalike = Package 'Microsoft.Windows.Photos' 'Microsoft.Windows.Photos_otherpublisher' 'Microsoft.Windows.Photos_2026.1.0.0_x64__otherpublisher'
$provision = [pscustomobject]@{ DisplayName='Microsoft.Windows.Photos'; PackageName='Microsoft.Windows.Photos_2026.1.0.0_neutral_~_8wekyb3d8bbwe' }
$unrelatedProvision = [pscustomobject]@{ DisplayName='Microsoft.WindowsCalculator'; PackageName='Microsoft.WindowsCalculator_2026.1.0.0_neutral_~_8wekyb3d8bbwe' }
function Reset-State {
    $script:packages = @($photosMain,$photosBundle,$unrelated,$lookalike); $script:provisioning = @($provision,$unrelatedProvision)
    $script:removeCalls = @(); $script:queries = @(); $script:administrator = $true; $script:failRemoval = $false
}
# These mocks replace every Windows package operation. This test never removes a real package.
function Test-PhotosAdministrator { $script:administrator }
function Get-AppxPackage {
    [CmdletBinding()]param([string]$Name,[string[]]$PackageTypeFilter,[switch]$AllUsers)
    $script:queries += [pscustomobject]@{ AllUsers=$AllUsers.IsPresent; Name=$Name }
    $script:packages
}
function Get-AppxProvisionedPackage { [CmdletBinding()]param([switch]$Online); $script:provisioning }
function Remove-AppxPackage {
    [CmdletBinding()]param([string]$Package,[switch]$AllUsers)
    if ($script:failRemoval) { throw 'Photos is currently in use.' }
    if (!$AllUsers) { throw 'AllUsers is required.' }
    $script:removeCalls += $Package
    if ($Package -eq $photosBundle.PackageFullName) { $script:packages = @($script:packages | Where-Object PackageFamilyName -CNE 'Microsoft.Windows.Photos_8wekyb3d8bbwe') }
    else { $script:packages = @($script:packages | Where-Object PackageFullName -CNE $Package) }
}
function Remove-AppxProvisionedPackage {
    [CmdletBinding()]param([switch]$Online,[string]$PackageName)
    if (!$Online) { throw 'Online is required.' }
    $script:removeCalls += $PackageName
    $script:provisioning = @($script:provisioning | Where-Object PackageName -CNE $PackageName)
}
Reset-State
$scan = Get-PhotosAccountScan
Check ($scan.Found -and $scan.Count -eq 2) 'Scan matches only the Microsoft Photos family, including bundle and main package'
Check ($script:removeCalls.Count -eq 0 -and !$script:queries[0].AllUsers) 'Initial account scan is read-only and does not request administrator scope'
$denied = $false; try { Invoke-PhotosMachineRemoval | Out-Null } catch { $denied = $true }
Check ($denied -and $script:removeCalls.Count -eq 0) 'Removal requires explicit consent'
Reset-State; $script:administrator = $false
$denied = $false; try { Invoke-PhotosMachineRemoval -Consent | Out-Null } catch { $denied = $true }
Check ($denied -and $script:queries.Count -eq 0 -and $script:removeCalls.Count -eq 0) 'Missing administrator approval prevents machine scans and removal'
Reset-State
$result = Invoke-PhotosMachineRemoval -Consent
Check ($result.Success -and $script:removeCalls.Count -eq 2) 'Bundle removal and provisioning removal complete without removing the stale main package twice'
Check ($script:packages.Count -eq 2 -and $script:packages[0].Name -eq $unrelated.Name -and $script:packages[1].PackageFamilyName -eq $lookalike.PackageFamilyName) 'Unrelated apps and similarly named packages are preserved'
Check ($script:provisioning.Count -eq 1 -and $script:provisioning[0].DisplayName -eq $unrelatedProvision.DisplayName) 'Only Microsoft Photos provisioning is removed'
Check (@($script:queries | Where-Object { !$_.AllUsers }).Count -eq 0) 'Machine removal verifies packages across all accounts'
Reset-State; $script:failRemoval = $true
$failed = $false; try { Invoke-PhotosMachineRemoval -Consent | Out-Null } catch { $failed = $_.Exception.Message -match 'currently in use' }
Check $failed 'Failed or partial removal is reported rather than presented as success'
Reset-State; $script:packages = @($unrelated); $script:provisioning = @($unrelatedProvision)
$result = Invoke-PhotosMachineRemoval -Consent
Check ($result.Success -and $script:removeCalls.Count -eq 0) 'Absent Photos is a successful no-op'
Write-Host ('PASS: ' + $script:checks + ' mocked package checks. No real packages were modified.')
