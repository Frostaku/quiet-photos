# Viewer and project-format research

Reviewed 21 October 2026. This is a feature/design review of primary documentation and public repositories, alongside the original Quiet Photos source. It is not a benchmark of competing applications or evidence of their runtime performance. The README describes what the current build actually implements; suggestions below are not a support guarantee.

## Features worth adapting

| Reference | Documented behavior | Quiet Photos baseline | Useful addition |
| --- | --- | --- | --- |
| Apple Preview | Multiple images can share a window; page thumbnails and a contact sheet help navigate documents. | Folder navigation and a bounded filmstrip already cover quick image browsing. | Page/frame controls for TIFF and ICO; later, an on-demand contact sheet with a bounded thumbnail cache. |
| Apple Quick Look | Finder previews selected files and offers a button to open the file in its associated application. | Open, reveal in Explorer and copy path are present. | An explicit **Open with…** action hands the original document to GIMP, Photoshop or another editor. Avoid recursively launching Quiet Photos when it is the default viewer. |
| ImageGlass | Frame navigation, several fit modes, interpolation options, clipboard opening and codec extensibility supplement folder viewing. | Cursor-centered zoom, fit/100%, panning, thumbnails, export and metadata already exist. | First prioritize page/frame selection and clipboard opening. Nearest-neighbor display helps pixel art; fit-width helps tall images. |
| JPEGView | Minimal controls, high-quality resampling, broad raster/RAW support and portable plus installer distribution. | Compact UI, slideshow, portable ZIP and MSI already exist. | Broaden actual decoders while retaining predictable memory use. Evaluate optional nearest-neighbor/sharp resampling against the present WPF rendering. |

Sources: [Apple Preview windows](https://support.apple.com/en-au/guide/preview/prvw1085/mac), [Apple Preview page navigation](https://support.apple.com/en-au/guide/preview/prvw11470/mac), [Apple Quick Look](https://support.apple.com/guide/mac-help/preview-a-file-mchlgtd_prev01/mac), [ImageGlass features](https://imageglass.org/docs/features), [ImageGlass repository](https://github.com/d2phap/ImageGlass), [JPEGView repository](https://github.com/sylikc/jpegview).

These are independently implementable product ideas. This review does not copy competitors' code. Adding a decoder library is a separate dependency decision requiring its license and redistribution notices. Source availability alone does not remove those obligations.

## Keep the viewer fast

Useful priorities are wider static decoding, clear preview-quality labels, page selection and handoff to an editor. Keep expensive format work behind a decode interface, away from the UI thread. Bound decoded pixels, archive entries, decompressed bytes and thumbnail caches. Do not preload an entire folder of RAW or layered documents. Retain the existing cancellation/stale-result behavior.

Animation, PDF rendering, contact sheets, color management and RAW demosaicing have separate memory and correctness costs. Treat each as a measured feature rather than advertising every extension supported by a large backend. A still first frame and an animation player are different capabilities. A 256-pixel thumbnail and a full-resolution composite are different capabilities too.

## Read-only project previews

### Photoshop PSD and PSB

Adobe publishes both formats. Their final image-data section can contain the merged composite, separate from layers. Adobe states that disabling maximize compatibility can omit this composite. A previewer should prefer it when present and supported, otherwise try a thumbnail resource and explain its reduced resolution. Resources 1036 and the older 1033 contain a thumbnail header and image payload; account for the older BGR convention. The specification also covers compression and PSD/PSB size differences. Photoshop Cloud Documents (`.psdc`) are explicitly outside that specification.

Recommendation: decode the saved composite and use an explicitly labeled thumbnail fallback; do not silently render only the first layer as the whole document. Reject unsupported color/depth/compression combinations clearly. Previewing should not modify layers or save back to the project. [Adobe Photoshop file format specification](https://www.adobe.com/devnet-apps/photoshop/fileformatashtml/).

### GIMP XCF

GIMP publishes a living, source-derived specification and points to `app/xcf/` as the ultimate authority. XCF stores layers, channels and tiled pixel hierarchies, with version-dependent pointer widths, precisions, compression and blend/composite semantics. Its lower-resolution hierarchy records can be dummy structures without pixels; they are not a reliable ready-made thumbnail. A generic single flattened preview cannot be assumed.

Recommendation: use a tested XCF decoder/compositor or explicitly constrain a new reader to a documented subset. A first implementation supporting ordinary visible 8-bit layers should identify unsupported masks, modes, groups and newer versions instead of displaying an apparently complete but incorrect image. Keep a GIMP handoff available. Validate against exports from real GIMP files, including masks and blend modes, before claiming general XCF fidelity. [GIMP XCF specification](https://developer.gimp.org/core/standards/xcf/).

### Lightroom Classic

Lightroom Classic's `.lrcat` is a catalog database that refers to original photographs; it is not one image. The catalog stores organization and adjustments without altering the source photograph. Adobe notes that preview-cache storage changed in version 13.3. A catalog reader must therefore be a separate, version-aware browsing feature, not an extra raster extension. [Adobe catalog FAQ](https://helpx.adobe.com/in/lightroom-classic/desktop/technical-support/workflow-issues/catalog-issues/catalog-faq-lightroom.html).

Smart Previews use lossy DNG and reside in a sibling `Smart Previews.lrdata` store. Supporting a standalone DNG does not establish support for locating, associating and rendering every entry in that store. [Adobe Smart Previews](https://helpx.adobe.com/lightroom-classic/desktop/viewing-photos/lightroom-smart-previews.html).

XMP contains metadata and adjustment instructions. For proprietary RAW it is generally a sidecar; supported JPEG/TIFF/PSD/DNG files can carry it internally. Those instructions are not rendered image pixels, and reading them cannot promise the current Lightroom appearance. Adobe also documents additional ACR sidecars from Classic 15.0. An updated DNG preview or exported JPEG/TIFF is the practical way to show saved Lightroom rendering. [Adobe metadata workflow](https://helpx.adobe.com/lightroom-classic/desktop/organize-photos-in-lightroom-classic/metadata-basics-actions.html), [XMP specifications](https://developer.adobe.com/xmp/docs/xmp-specifications/).

Recommendation: preview supported source photographs/DNGs and any valid embedded preview, label that origin, and hand files to the user's editor. Defer `.lrcat`/`.lrdata` catalog integration until supplied catalog fixtures, version-specific evidence and read-only validation are available. Do not guess relationships by scanning arbitrary JPEG signatures inside catalog files.

## Evidence and open questions

- **Observed locally at review start:** WPF/.NET Framework implementation; WIC decoder path; first frame only; bounded caches; no bundled project-format reader. These establish the baseline above, not the result of subsequent changes.
- **Established by primary documentation:** the feature descriptions and file-format structures linked above.
- **Design inferences:** the priority order, bounded-decoder design and editor-handoff behavior are recommendations for this project.
- **Not established:** relative startup speed, pixel-equivalent output for all project files, proprietary Lightroom cache internals, Photoshop cloud format internals, or generic XCF compositing fidelity.
- **REA boundary:** the original Microsoft Photos REA evidence remains in [rea-findings.md](rea-findings.md). This review uses public specifications/source documentation and does not claim new REA binary or runtime evidence. Further reverse engineering would need a specific artifact or fixture and its version; published specs are sufficient for initial PSD/XCF readers.
