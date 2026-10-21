# Validation — 21 October 2026

The Windows x64 build runs **199 decoder checks and 60 viewer/integration checks** using `build.ps1 -Test`. Version 1.1.1 adds rendered transparency/menu checks; decoder checks were last rerun for 1.1.0 and that code is unchanged.

Checks cover natural filename order, reduced preview dimensions, file-handle release, preview and thumbnail reuse, cancellation, full-resolution decoding, cache accounting, all eight EXIF orientations with corner-pixel comparisons, rotated full-resolution export, replacement of an existing copy, preservation of source bytes, opening/scanning folders, information-panel behavior, fullscreen entry/exit, zoom/fit/rotation, selectable backgrounds, rapid competing requests, damaged-file errors and subsequent navigation recovery.

The extended tests add a real helper-process protocol roundtrip, full-resolution PSD preview, read-only project UI, unchanged project bytes, TIFF page navigation, fit-to-width, nearest-neighbor scaling, XMP source resolution, cache reuse/invalidation and clear missing-source errors. Worker fixtures cover AVIF, JPEG XL, WebP, JPEG 2000, EXR/HDR, DDS, QOI, standard raster, PSD/PSB, a handcrafted two-layer XCF v0 with pixel-level alpha-composite checks, ORA/KRA saved previews, SVG/SVGZ, and GIF/TIFF pages. Negative cases cover malformed data, external/active SVG, renamed MVG input, ambiguous/missing archive previews and oversized inputs. The worker tests verify source hashes remain unchanged.

Transparency checks open a PNG with an opaque region and an alpha region, press B through the live window, and sample rendered pixels in all three modes. They verify transparent pixels change, opaque pixels and the surrounding canvas stay unchanged, and source bytes are preserved. The actual More options popup is rendered to check the left edge is dark and its label reports the current mode. Screenshots are generated for visual inspection.

Visual verification used rendered images of the empty state, loaded viewer and information panel in `tests/output`. The test exercises the actual WPF window and its buttons; the renderer captures application content rather than the OS title bar. Fixtures are generated/handcrafted, not a representative editor/camera corpus. HEIC and RAW readers report available, but real HEIC/RAW samples were not exercised. Tests do not verify every format variant, complex GIMP/Photoshop rendering fidelity, clipboard interoperability with other programs, live drag/drop input or every monitor/DPI configuration. The IDE project hook is provided but cannot be exercised here because a .NET SDK is not installed.

The MSI smoke test checks every bundled decoder/configuration/notice file against portable payload hashes, Open with registration for extended types, shortcut targets, product registration and uninstall cleanup. It preserves an unrelated test file and checks real Microsoft Photos registrations remain unchanged. Ten additional mocked package-removal checks exercise consent and package scoping without uninstalling Microsoft Photos.

## Historical measurements on this machine

These measurements describe the original viewer build before the icon, installer and extended-decoder additions; they are historical observations, not fresh measurements of version 1.1.0.

The standalone process benchmark opened a generated **6000 × 4000 PNG** from a folder containing three valid images and one damaged file. It waited until the copy-image button became enabled, then sampled process memory 600 ms later. Timings include process startup, WPF initialization, foreground decoding, UI Automation queries and polling. File contents and filesystem caches were warm; this is not a cold-start benchmark.

| Run | Process start to loaded control | Working set | Private bytes |
| --- | ---: | ---: | ---: |
| 1 | 1039 ms | 245.5 MiB | 224.3 MiB |
| 2 | 889 ms | 245.7 MiB | 223.9 MiB |
| 3 | 829 ms | 246.1 MiB | 224.5 MiB |

Median observed startup-to-loaded-control was **889 ms**. An isolated foreground preview decode in the final test run took **77 ms**, producing a 2560-pixel maximum-edge bitmap from the 24-megapixel source. These synthetic fixtures are flat illustrations and compress readily; timings do not predict every camera image, slow disk, cloud placeholder or codec.

The complete correctness test harness used approximately 577 MiB at its final sample because it also held generated fixtures, full-resolution decodes, exports and rendered screenshots. That figure describes the test process, not ordinary viewer startup.

The original pre-icon executable was approximately 64 KiB and used the machine's installed .NET Framework/WPF and Windows codecs. Current builds also embed the custom icon resources. Disk size is not a RAM measurement. The 96 MiB decoded-image cache limit excludes the displayed image, transient codec buffers, rendering resources, framework runtime and thumbnails. The measured working sets above are the relevant standalone process observations for that original scenario.

Microsoft Photos was not launched or measured. The REA investigation established declarations in shipped metadata; it did not establish Photos' decode pipeline, cache policy, startup cost, runtime layout, or the cause of lag. No comparative speed or memory claim is made.

## Reproduce

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1 -Test
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\benchmark.ps1 -ImagePath 'C:\Pictures\photo.jpg'
```

Tests write results and screenshots under `tests/output`; a successful complete `build.ps1 -Test` removes those temporary files after publishing both packages. Standalone test or benchmark runs reuse that folder, overwriting previous results. The benchmark requires an image path if no generated fixture remains. It opens and closes three instances of this viewer and does not interact with Photos.
