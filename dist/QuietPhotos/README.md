# Quiet Photos

A compact Windows image viewer, built from scratch after a bounded REA inspection of Microsoft Photos' shipped Lightbox metadata. The viewer has its own implementation and interface. The [REA report](docs/rea-findings.md) records the evidence and what remains unknown.

This repository contains the source, build tools, and the latest ready-to-run release in `dist`: the Windows installer, portable ZIP, and unpacked executable with its supporting files. Intermediate builds, test outputs and raw investigation exports are excluded from Git. To rebuild the release, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

## Run

Every successful `build.ps1` or `build-release.ps1` run updates the same three deliverables: `dist\QuietPhotos\` (portable app), `dist\QuietPhotos-portable.zip`, and `dist\QuietPhotos-Setup.msi`. The installer contains the version internally. Existing generated versioned installers are retired after a successful build. Intermediate build folders and extra icon exports are removed; `-Test` also removes its generated fixtures/screenshots after passing. Failed builds retain diagnostic files and leave the previous release untouched until both new packages have been built.

Open **`dist\QuietPhotos-Setup.msi`** to install, or extract **`dist\QuietPhotos-portable.zip`** and run the executable inside. Requires Windows 10/11 x64 with .NET Framework 4.8. Extended image decoders are bundled; no codec download or image editor installation is required.

The MSI installs for your Windows account under `%LOCALAPPDATA%\Programs\QuietPhotos`, adds a Start-menu shortcut, and offers an optional desktop shortcut. Remove it through Windows **Settings → Apps → Installed apps → Quiet Photos → Uninstall**. Running the MSI again also offers Repair and Remove. It registers the app for Open with without replacing your chosen default viewer. Uninstall removes installed files and shortcuts and preserves unrelated files you put in the application folder.

After a successful interactive install, setup scans for Microsoft Photos in the current Windows account and offers an **unchecked** option to uninstall it from the PC. Keeping Photos is the default. Choosing removal requests Windows administrator approval, scans all accounts, removes only Microsoft's exact Photos package family, and removes its provisioned copy for future accounts. If it is absent from the current account or the scan fails, setup explains that and lets you request an administrator scan/removal or keep it. No Photos removal runs during silent installation, repair, uninstall, failed installation, or setup cancellation. Your picture files are not targeted; Windows may remove Photos app settings. Uninstalling Quiet Photos does not reinstall Microsoft Photos.

The app includes an original photo-inspired blue/teal landscape icon with an amber sun. The editable vector is `assets\QuietPhotos.xaml`; the multi-resolution Windows icon and a 512-pixel preview are in `assets\icons`. The build regenerates the other PNG sizes. The icon is embedded in the EXE, window/taskbar and empty state, and used by installer shortcuts and Installed apps.

Open an image with the toolbar, drag an image/folder onto the window, or pass a path:

```powershell
.\dist\QuietPhotos\QuietPhotos.exe 'C:\Pictures\photo.jpg'
.\dist\QuietPhotos\QuietPhotos.exe 'C:\Pictures'
```

For double-click opening, use Windows **Open with → Choose another app**, browse to the executable, and select it for the file type. Keep the complete portable folder, including its `.config` file and `codecs` subfolder, together at a stable location.

## Included

- Dark viewing canvas with native window resizing and a file information panel.
- Previous/next and first/last image navigation with Windows natural filename ordering.
- Wheel zoom around the pointer, drag to pan, fit, and actual device pixels at 100%.
- Automatic EXIF orientation, including mirrored images.
- Clockwise/counterclockwise rotation; original files stay untouched.
- Full-resolution clipboard copy and rotated export to PNG, JPEG, TIFF, or BMP.
- A bounded filmstrip, fullscreen, four-second slideshow, and selectable transparency backgrounds.
- Camera/date metadata where available, copy path, and reveal in Explorer.
- Manual page/frame selection for multi-image files, fit-to-width, and optional sharp pixel scaling.
- Read-only previews of supported Photoshop/GIMP/Krita/OpenRaster documents, with **Open original with editor…** using Windows' application chooser.

Version **1.1.0** adapts these workflow ideas after reviewing Apple Preview/Quick Look, ImageGlass and JPEGView. The [comparison and format research](docs/viewer-research.md) links primary sources, explains the choices, and records useful future work such as contact sheets and clipboard opening. Competitors' code was not copied and their performance was not benchmarked.

Version **1.1.1** fixes the white icon gutter in the More options menu. **Transparency background** now shows its current mode; click it or press **B** to cycle Dark → Checkerboard → Light. This changes the background underneath transparent image pixels (for example, a transparent PNG), without changing opaque pixels or the surrounding dark canvas. The background is a viewing aid and is not saved into exported images.

The app contains no gallery database, recursive indexing, cloud/account integration, AI models, telemetry, updater, network client, or background service. Closing the window exits the app. Opening cloud-placeholder files can still cause Windows/OneDrive to retrieve those files.

## Keyboard

| Action | Shortcut |
| --- | --- |
| Open image / folder | Ctrl+O / Ctrl+Shift+O |
| Previous / next | Left / Right, Page Up / Page Down |
| First / last | Home / End |
| Zoom | Mouse wheel, + / − |
| Fit / actual pixels | 0 / 1, or double-click |
| Fit to width / toggle sharp pixels | W / P |
| Previous / next page or frame | [ / ] |
| Pan | Drag image |
| Rotate right / left | Ctrl+R / Ctrl+Shift+R |
| Copy image / path | Ctrl+C / Ctrl+Shift+C |
| Save a copy | Ctrl+Shift+S |
| Reveal in Explorer | Ctrl+E |
| Open original with editor | Ctrl+Shift+E |
| Information / filmstrip | I / T |
| Transparency background: dark / checker / light | B |
| Slideshow / fullscreen | F5 / F11 |
| Stop slideshow, then exit fullscreen, then close info | Esc |
| Shortcut help | F1 |

## Performance design

Foreground decoding runs away from the UI thread. Rapid selections cancel queued work and discard stale results. Cancellation does not interrupt an active Windows decoder; an extended-decoder process can be terminated. Fit previews have a maximum edge of 2560 pixels, with full resolution requested on zoom or export. Cached previews/full images share a 96 MiB pixel budget and at most six entries. The filmstrip displays at most 11 neighboring files and caches at most 64 thumbnails, each with a 144-pixel maximum edge. Filmstrip decoding begins after the foreground image has loaded and had time to paint.

The cache budget is **not** a process-memory cap: the current image, codec buffers, GPU textures, WPF, and exports also need memory. Windows full-resolution loads have a 120-megapixel limit. Extended formats use the bundled Magick.NET Q8 x64 worker on demand, adding approximately 27 MB of decoder DLLs on disk without loading them into the viewer. Its limits are listed below. See [validation and measurement](docs/validation.md) for machine-specific results. Photos was not benchmarked, so there is no verified relative speed claim.

## Supported formats

The file picker, folder browser and installer Open with registration use the same extension list. A listed extension does not guarantee every format version, compression scheme, camera or malformed file. Unsupported files show a recoverable error.

| Family | Extensions | Preview behavior / verification |
| --- | --- | --- |
| Standard raster | `.jpg .jpeg .jpe .jfif .png .bmp .dib .gif .tif .tiff .ico` | Windows codecs for the initial frame; generated JPEG/PNG/BMP/GIF/TIFF/ICO fixtures exercised. Additional pages/frames use the bundled worker. |
| JPEG XR / HD Photo | `.jxr .wdp .hdp` | Windows WIC path; availability/variants depend on Windows codecs. No dedicated fixture yet. |
| WebP, AVIF, JPEG XL | `.webp .avif .jxl` | Bundled decoding; generated fixtures tested. No Store codec required. |
| HEIF / HEIC | `.heic .heif .hif` | Bundled reader reports available; real HEIC files and device-specific variants have not yet been tested. |
| Animated PNG | `.apng` | PNG still preview; playback and APNG frame disposal are not implemented. |
| JPEG 2000 | `.jp2 .j2k` | Bundled decoder; JP2 fixture tested. |
| Other raster | `.tga .targa .pcx .qoi .ppm .pgm .pbm .pnm .pam` | Bundled decoder; TGA/PCX/QOI/PPM generated fixtures tested; other Netpbm variants use the same reader family. |
| Texture / HDR | `.dds .hdr .exr` | Generated fixtures tested; displays an 8-bit preview, without HDR display output or guaranteed tone mapping. |
| Restricted SVG | `.svg .svgz` | Rasterizes simple SVG, including gzip-compressed SVGZ; generated fixtures tested. Scripts, embedded images, style sheets, DTDs, external references and `url()` paints (including gradient references) are rejected. Complex SVGs can fail. |
| Photoshop | `.psd .psb` | Read-only saved composite; generated fixtures and full-resolution decoding tested. Layers cannot be edited. Requires a compatible saved composite; no thumbnail fallback or Photoshop Cloud Document support. |
| GIMP | `.xcf` | Best-effort flattened layer rendering; a handcrafted two-layer 8-bit XCF v0 fixture tested. Newer precision, masks, groups, blend modes and effects may differ or fail; not a guarantee of GIMP-identical output. |
| OpenRaster / Krita | `.ora .kra` | Reads the embedded `mergedimage.png`, without extracting or editing layers; generated ZIP fixtures tested. Projects without that saved preview cannot be shown. |
| Camera RAW / DNG | `.dng .cr2 .cr3 .nef .arw .orf .raf .rw2 .pef .srw` | Bundled LibRaw reader; read capability present, but real camera fixtures have not been exercised. Camera/version support varies. Lightroom/Camera Raw develop adjustments are not applied. |
| XMP sidecar | `.xmp` | Explicit opening resolves an adjacent matching RAW/JPEG/TIFF/PNG source and labels it as an unadjusted source preview. XMP itself contains instructions, not pixels. Sidecars are omitted from automatic folder browsing to avoid duplicate photos. |

Project documents, RAW files and XMP previews disable **Save a copy**. Viewing, zooming, selecting a page, copying displayed pixels and opening the original in an editor do not rewrite the source. For an accurate saved Lightroom appearance, open a JPEG/TIFF exported by Lightroom. `.lrcat` catalogs and `.lrdata` cache folders are not supported image formats. XMP settings, masks, AI edits and proprietary cache relationships are not interpreted. See the [project-format investigation](docs/viewer-research.md#read-only-project-previews).

The worker handles up to 64 pages/frames; navigation is manual, with no automatic animation playback. Extended inputs are limited to 512 MiB, 100 megapixels and 40,000 pixels per dimension; an ORA/KRA merged entry is limited to 128 MiB and SVG to 8 MiB of XML. The worker has resource limits (512 MiB pixel cache, no disk pixel cache, two threads), a 30-second parent timeout and a 64 MiB PNG transfer limit. Some large images therefore work in fit view but fail at full resolution. These are resource controls, not an operating-system sandbox or total process-memory guarantee. Extended formats currently require local paths; copy network-share files locally first.

The Q8 decoder produces 8-bit display pixels. High-bit-depth/HDR precision, embedded color profiles and editor-specific rendering are not preserved by this preview path; color-critical proofing should use the original editor. The [dependency manifest](docs/decoder-dependencies.md) records the pinned runtime and notices. Its licenses are distributed in `codecs/Notice.txt`.

## Feature limits

This version recreates the local preview workflow. It does not include Photos' full editor, video playback, printing, favorites database, library management, or shell thumbnail/preview-pane extension. It doesn't change Windows file associations automatically.

Save a copy re-encodes the oriented full-resolution pixels and applies the current view rotation. Camera/EXIF/IPTC metadata, profiles, animation, and extra pages are not preserved in the export. JPEG output uses quality 95 and has no alpha channel; choose PNG for transparency. Existing copies are replaced by a completed same-directory temporary file. The selected source filename cannot be overwritten through the save dialog.

## Build and verify

The build uses the C# compiler shipped with Windows .NET Framework and the pinned decoder DLLs checked into `vendor/codecs`. No SDK, package restore, or network access is needed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-release.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\benchmark.ps1 -ImagePath 'C:\Pictures\photo.jpg'
```

An SDK-style `src\QuietPhotos.csproj` is also provided for IDEs with the .NET Framework 4.8 targeting pack; its build target also copies/builds the decoder. Automated tests generate local fixtures, exercise the decoder and live WPF window, and write results under `tests\output`. The benchmark starts three app processes, waits until the copy-image control is enabled, records memory, and closes each process. Its timings include process startup, UI initialization, image loading, and automation polling; they are warm-cache observations rather than controlled cold-start measurements.

`build.ps1` and `build-release.ps1` both produce the complete release. They stage compilation and packaging under `obj\build`, produce the MSI using Windows Installer's database API and the built-in cabinet tool, publish both packages, and clean temporary outputs. No third-party installer compiler is required. `packaging\test-installer.ps1` reuses the single `obj\installer-smoke` folder for its logs, checks hashes and shortcuts, and verifies that an unrelated file survives removal. This test temporarily registers the app with Windows Installer; don't run it while the same MSI product is installed for normal use. Windows Installer needs its normal system-cache access even when the application's files are directed to a test folder. See [installer notes](docs/installer.md).

## Source map

| File | Responsibility |
| --- | --- |
| `src\ImageLoader.cs` | Windows codecs, EXIF, cache and decode queues |
| `src\ExtendedDecoder.cs` | Bounded worker protocol, cancellation and XMP companion resolution |
| `codecs\DecodeWorker.cs` | Bundled decoder allowlist, layered/container previews and resource controls |
| `vendor\codecs` | Pinned third-party runtime DLLs and redistribution notices |
| `src\ImageSurface.cs` | Rendering, device-pixel zoom, pan, view rotation |
| `src\ViewerWindow.cs` | UI, folder navigation, actions and shortcuts |
| `src\Theme.xaml` | Embedded UI styling |
| `tests\TestRunner.cs` | Decode/export tests, request races and WPF UI checks |
