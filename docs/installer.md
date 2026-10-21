# Quiet Photos 1.1.1: icon and installer

The Windows MSI is `dist/QuietPhotos-Setup.msi`. Both public build scripts always produce this installer, the portable ZIP, and the unpacked portable app, replacing the previous release. The version is stored inside the MSI. Double-click it, choose whether to add a desktop shortcut, then select Install. Its default location is `%LOCALAPPDATA%\Programs\QuietPhotos`. It installs for the current Windows account and uses Windows Installer's standard registration, repair, rollback and uninstall machinery. After a successful interactive install, a companion offers optional removal of Microsoft Photos. Directory-assignment custom actions also support isolated tests.

Installed files are the viewer EXE, configuration, README, original ICO, five documentation files, and the `codecs` folder containing the decoder EXE, three runtime DLLs, XML configuration and bundled notices. The application is registered under the current user's `Software\Classes\Applications\QuietPhotos.exe` key with the same supported image extensions as the file picker. It does not write a default-file-association selection. The Start-menu shortcut is always included; the desktop shortcut is optional. Uninstall removes its files, registry values and shortcuts. Extra user files prevent the application directory from being removed; they are not deleted.

The MSI checks for x64 Windows and the .NET Framework 4.8 registry release marker. The release is unsigned; a signing certificate was not supplied. There is no downloader, updater or bundled framework. Keep the MSI if you want to use Repair later.

## Icon assets

- Editable vector source: `assets/QuietPhotos.xaml`.
- Windows ICO: `assets/icons/QuietPhotos.ico`, with 16, 20, 24, 32, 40, 48, 64, 128 and 256-pixel alpha images.
- PNG exports at those sizes plus 512 pixels.
- Regenerate with `tools/build-icon.ps1`; the normal app build runs this automatically.

The icon is a new vector design: a rounded navy tile, blue sky, teal mountain/landscape shapes and an amber sun. No Microsoft image or icon was copied. The same artwork is embedded in the executable's Windows resources, used as the window icon and empty-state mark, and supplied to installer shortcuts and the Installed apps entry.

## Verification

The application passed its 40 automated checks. MSI install/uninstall smoke tests check the installed executable's SHA-256 against the release build, icon installation, Start-menu and desktop shortcut targets, Windows Installer product registration, clean removal and preservation of an unrelated file placed in the install directory. The interactive test additionally drives the Photos choice and selects Keep. The native welcome dialog and Cancel behavior have also been checked.

The first sandboxed MSI run reached Windows Installer's transaction step but could not write its system-cache transaction file. Repeating the same test with normal system-cache access succeeded. This was a test-environment access limit; the package remains a per-user installer.

Standalone installer tests reuse `obj/installer-smoke` and `obj/installer-ui` for logs and captures instead of creating a new folder each time. These outputs are not packaged and the next successful build removes them with the other intermediates. Failed builds retain diagnostic outputs. Full MSI ICE validation and code-signature verification have not been performed; the Windows SDK validation tools and a signing certificate are not installed here.

Build with `build-release.ps1`. Run `packaging/test-installer.ps1` for the reversible install/uninstall test, and use Windows PowerShell for `packaging/test-installer-ui.ps1`. Don't run the smoke test against an already installed copy; it deliberately stops when that product is registered.

The installer follows Microsoft's documentation for [per-user installation](https://learn.microsoft.com/en-us/windows/win32/msi/installation-context), the [installer database](https://learn.microsoft.com/en-us/windows/win32/msi/using-the-installer-database) and [multi-size icon controls](https://learn.microsoft.com/en-us/windows/win32/msi/iconsize-control-attribute).

## Optional Microsoft Photos removal

The `OfferPhotosRemoval` custom action is in `InstallUISequence` at 1350, after successful `ExecuteAction`. Its condition requires a new interactive installation at full UI level. It is absent from `InstallExecuteSequence`, so silent and basic-UI deployment cannot trigger it. Repair, uninstall, failure and cancellation skip it. The helper's return code is ignored by MSI because Photos removal happens after the Quiet Photos transaction commits and must not roll back the working replacement.

The companion first performs a read-only current-account scan. **Keep Microsoft Photos** and an unchecked removal checkbox are the defaults. Escape and closing the window keep Photos. A successful current-account scan says whether Photos was found; detection errors are shown as unknown rather than absence. Other accounts require administrator access. The unchecked choice in the absent case explicitly offers to scan all accounts and remove Photos if present.

Selecting removal and pressing **Uninstall Photos…** starts a separate UAC-approved helper. It scans both installed and provisioned packages before making changes. Only `Microsoft.Windows.Photos_8wekyb3d8bbwe` with the exact Microsoft Photos package name/publisher identifier is selected. Bundle parents are processed before main packages; already removed children are skipped. It calls Windows' [Remove-AppxPackage with AllUsers](https://learn.microsoft.com/en-us/powershell/module/appx/remove-appxpackage) for existing accounts and [Remove-AppxProvisionedPackage](https://learn.microsoft.com/en-us/powershell/module/dism/remove-appxprovisionedpackage) for the future-account provisioned copy. Both scopes are queried again before reporting success.

UAC cancellation keeps Photos. Errors and partial removal are reported while Quiet Photos stays installed. Picture folders are not enumerated or deleted; Windows may remove Photos' own app settings/data. Uninstalling Quiet Photos does not reinstall Photos. Store, Windows updates or organization management can reinstall or block removal of the Microsoft app; this installer does not change those policies.

Ten mocked package tests cover identity filtering, current-account read-only detection, explicit consent, administrator gating, parent/child bundle handling, preservation of other apps, provisioning scope, partial failure and absence. They replace every package operation and never remove the real app. The full interactive MSI test selects Keep; real Microsoft Photos removal has deliberately not been exercised on this machine.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\packaging\test-photos-packages.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\packaging\test-installer.ps1 -FullUI
```

Version 1.0.2 also writes conventional DIB frames at small ICO sizes and PNG at 256 pixels so Windows Installer's legacy icon control can select its 48-pixel image correctly.
