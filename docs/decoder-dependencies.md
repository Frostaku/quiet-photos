# Bundled decoder dependencies

Quiet Photos 1.1.0 includes unmodified **Magick.NET-Q8-x64 14.17.2** and **Magick.NET.Core 14.17.2**, using their .NET Standard 2.0 managed assemblies and Windows x64 native library. The upstream notice identifies ImageMagick **7.1.2-32**, built 27 September 2026. This is a pinned, offline-build dependency, not a package downloaded when users open a picture.

The viewer's ordinary Windows-codec path does not load these assemblies. The short-lived `codecs/QuietPhotos.Decode.exe` helper loads them for extended formats and additional pages. The three DLLs occupy 27,269,648 bytes (about 26 MiB); configuration, notices and the helper add a small amount. Keep the entire codecs folder with the portable EXE. The MSI packages the same files.

## Provenance

Official source: [Magick.NET](https://github.com/dlemstra/Magick.NET). Original package downloads:

| Package | SHA-256 of downloaded package |
| --- | --- |
| [magick.net-q8-x64.14.17.2.nupkg](https://api.nuget.org/v3-flatcontainer/magick.net-q8-x64/14.17.2/magick.net-q8-x64.14.17.2.nupkg) | `FEFE99147A5F3B664B930A484E2EFE72909DCD1E4EAE306829141447B0302C7A` |
| [magick.net.core.14.17.2.nupkg](https://api.nuget.org/v3-flatcontainer/magick.net.core/14.17.2/magick.net.core.14.17.2.nupkg) | `016D675549098F126D3566C07E7030D48F9EFB3B2A7F51EDD010386750A54F5C` |

Retained build inputs in the repository's `vendor/codecs`:

| File | SHA-256 |
| --- | --- |
| `Magick.Native-Q8-x64.dll` | `E73B69325859131F8AE0F909533210932D05AD8CB931A3DDA92EE56F1233654A` |
| `Magick.NET-Q8-x64.dll` | `D4062E5BB9E0B9AAD219FADCF8D3BB1DDF217E994AFB549161BC1A69B2E1BBB3` |
| `Magick.NET.Core.dll` | `B470B89EB7769C60946F74A07443958B9BF0A139000CF3CB9A06E59851AC0D92` |
| `Notice.txt` | Original combined upstream copyright/license notices |

The downloaded packages, XML API documentation, and alternate framework builds are not needed to rebuild this application and are not retained. The worker build exports the upstream default runtime configuration from these managed assemblies and replaces `policy.xml` with this project's restricted decoding policy.

## Redistribution notices

Magick.NET is copyright Dirk Lemstra, licensed under Apache License 2.0. The bundled `Notice.txt` contains the full Apache 2.0 text, ImageMagick license, and notices/licenses for native dependencies. The build copies that complete, unmodified file into both distributions and the installed app at `codecs/Notice.txt`; retain it when redistributing the decoder. These third-party licenses cover their respective components, not an automatic license grant for this application's own source. No competitor application code is included.

## Scope

The installed library has more capabilities than Quiet Photos exposes. The worker uses an explicit extension/coder allowlist, rejects active/external SVG content, disables external delegates and filters, and bounds archives, input sizes, pixels, frames and time. This does not establish support for every library format. PDF/PostScript and external delegate tools such as Ghostscript are not bundled or launched. See the README's tested-format matrix and limitations before claiming support for a specific file or camera.
