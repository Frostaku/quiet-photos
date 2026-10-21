# Photos viewer: bounded REA findings

Inspected on 2026-10-21. This is a static feature investigation for an independent lightweight image viewer. No Microsoft implementation code or visual assets were copied into the application. Photos was not launched, and no native disassembler session was opened.

## Target identity

- Installed package: `Microsoft.Windows.Photos`, version `2026.11090.22001.0` (read with `Get-AppxPackage`).
- Selected artifact: `C:\Program Files\WindowsApps\Microsoft.Windows.Photos_2026.11090.22001.0_x64__8wekyb3d8bbwe\Lightbox.winmd`.
- SHA-256: `1a42615125fceeaf34f1c8ba1532c60adbe5e865a9feca89f9b1beee31192aaf`; 255,488 bytes.
- Module MVID: `bc68af66-88c3-4c9b-8da5-ede2cc0f4886`.
- REA reports complete Windows Runtime 1.4 metadata: 462 type definitions and 4,104 method definitions. These are declarations; sampled methods have absent bodies, and the member inventory returns zero call edges, field accesses, and loaded user strings.
- Identity evidence: `ev_f7ffba5d4cc43d1142cea888222d8714762ca1b29e3d6cc6b71b07abf0013d6c`.
- Member inventory evidence: `ev_2e41500685430e636158f3702fd30b732413a504d41a1e129ed57ed254e29634`.

## Observed declaration surface

| Area | Static observation | REA evidence |
| --- | --- | --- |
| Bottom controls | `BottomBarViewModel` declares zoom in/out, actual size, fit view, zoom percentage and slider state, filmstrip labels, favorite state, info pane and slideshow commands; dimensions/file type/file size text helpers. | `ev_c633ac878ec0695b2b76ce70349c56564d38a69b1a37103b46e039523d2763c4` |
| Image manipulation | `MediaView2Model` declares `Pan`, `ZoomAndPan`, `ZoomToActualSize`, `ZoomToFitView`, `ZoomDoubleTap`, `ZoomByStep`, `ZoomByScrollWheel`, `ZoomByInput`; transparency backdrop, image/video visibility and decoding state. | `ev_443f4ecad48075c8aeabfa69a57d0e4dbbf4647f8d1b28504d8f5e3c4b4b42b3` |
| Navigation | `NavigationControlViewModel` declares `NavigatePrevAsync`, `NavigateNextAsync`, previous/next enabled state and commands. | `ev_1e6092f1bfa94ebc51e45ccabf79656593cef613bf5960ef2723f7143ae53b60` |
| Title controls | `ViewerTitleBarViewModel` declares rename, rotate, edit, delete, favorite, share, info, save as, print, copy image, copy path, open with, resize and open file location. It also declares settings, AI Narrator, Clipchamp and other integration commands. | `ev_e64779ac6ca7544a68a405cd39e76de133fb31578cdd47bcc185de624a439933` |
| Content state | `ContentViewModel` declares info/overflow state, one-up/multiview, zoom/pan capabilities, loading state, slideshow navigation and video playback state. | `ev_94e109e3bc3429bfd99a1b1da31979b2b9776ba5d3d6c12b3613e20eff554ad1` |
| Viewer components | Types include `ViewerRootPage`, `ContentView`, `Filmstrip`, `FilmstripViewModel`, `MediaViewerPage`, `MediaView2`, `MediaView2Model`, `InfoPane`, `FullScreenViewModel`, `FileCollectionFromFilePathList`, `MediaItemCollection`, rotation/resize services, OCR and telemetry types. | `ev_9ff79f25b7988802b5ac77ae95707fc23e7fe03b5f0a7f83cb7b27a813af3223` |
| Dependencies | Declared references include Microsoft.UI.Xaml, ImageLib, ImageEditorLib, Slideshow, Photos.Models, SharedServices, Photos.Controls.Ocr, Photos.Telemetry and Photos.Authentication. | `ev_7acd457a794bf4b1996365e84cf29d119bb64349cc1c20909468559a03c128c7` |

The installed package also contains viewer XBF files including `Lightbox/ViewerRootPage.xbf`, `ViewerTitleBar.xbf`, `ContentView.xbf`, `BottomBar.xbf`, `Filmstrip.xbf` and `InfoPane.xbf`. This filesystem inventory is supplementary context, not decoded layout evidence. No XBF parser or runtime layout capture was used.

## Interpretation and implementation boundary

The declarations support a viewer organized around a root page, title controls, content/viewport, previous/next navigation, a bottom zoom bar, optional filmstrip and information pane. That architectural interpretation is an inference from names, signatures and declared relationships; it does not establish execution order or object lifetime.

For an independent compact viewer, local image opening, folder navigation, fit/actual size, wheel zoom and drag pan, rotation, filmstrip, fullscreen, basic metadata and clipboard operations are a reasonable core. Excluding accounts, gallery indexing, cloud integrations, AI and editing suites is a product scope choice. Their presence in metadata does not establish that they run during image opening or cause lag.

## Coverage and remaining unknowns

- Core control declarations: answered statically within this artifact.
- Component/dependency architecture: partially answered by declarations and references.
- Exact layout, icons, margins, theme values, keyboard bindings and runtime visibility: unresolved.
- Decode algorithms, cache limits, threading and startup work: unresolved; no method implementations were recovered.
- Photos startup time, memory use, latency and cause of reported lag: not measured. No comparative performance claim is supported.
- Image format and animation support: not established by this metadata inspection.
- Tokens are build-local and require the SHA-256 and MVID above; reference versions of `255.255.255.255` are metadata conventions, not measured dependency release versions.

The investigation produced a raw REA export with 11 retained records (6,417,553 bytes; SHA-256 `04ed8315df519f006a6358fdf971a4aa59eebc08f282db13dcfa472fd949ddf7`). That generated export is omitted from the source repository. This report retains the artifact identities, evidence IDs, conclusions and coverage limits. The unanswered questions above remain unresolved. No native session needs cleanup.
