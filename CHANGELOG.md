# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-11-13

### Changed

- The application is rewritten with [Avalonia](https://avaloniaui.net/) (`src/FrameGallery.Desktop`) and replaces the WinForms prototype. The release is the new app; the protocol client, the TV list file and the pairing are the same, so TVs you have already paired stay paired.
- Searching and opening a gallery show a spinner. In the gallery every picture appears at once as a tile with its own spinner and fades in when its thumbnail arrives.
- Adding a photo shows the crop window and then a progress bar, a real one while the file is sent and an indeterminate one while the TV saves it.
- After adding or deleting a photo the gallery updates its tiles itself instead of fetching the whole list from the TV again.
- The package is a framework-dependent publish for `win-x64` (about 28 MB, 12 MB zipped) that needs the .NET 10 Runtime, not the Desktop Runtime. The roughly 100 MB of native `.pdb` files of SkiaSharp and HarfBuzzSharp are not shipped. The Binternal merge of the earlier package is gone: the package has more files again, the libraries are not merged into one.

### Added

- A demo mode (`--demo` or `FRAMEGALLERY_DEMO=1`) with a fake TV and gallery, used for the screenshots in the README. It touches neither your TV list nor the network.
- Groundwork for macOS and Linux: Windows-only code (DPAPI) is guarded, the token file is readable by its owner only elsewhere, and the image code uses SkiaSharp. It has not been built or run on those systems yet and there are no releases for them.

## [0.1.1] - 2026-11-13

### Changed

- The release package is smaller and simpler: `Core` and `Localization` are merged into `FrameGallery.App.dll` (Binternal), debug symbols are embedded, translations are compiled into the main assembly, and the `.pdb`, `.deps.json` and `pl\` files are no longer shipped (4 files instead of 11).

## [0.1.0] - 2026-11-13

First public release: an early prototype for Windows, tried on a single 2025 The Frame.

### Added

- Discovery of Samsung TVs on the local network (SSDP), or adding a TV by its IP address.
- Pairing with the TV. The pairing token is stored encrypted (Windows DPAPI) and the TV's certificate is pinned on first use.
- Gallery of the photos on the TV with thumbnails; "Show all" also lists Art Store and factory images (read-only).
- Uploading photos by picking files or by drag and drop, with a crop dialog fixed to the screen's aspect ratio. The app sends a JPEG in the TV's native resolution.
- Right-click menu with "Show on TV" and "Delete" (only your own photos can be deleted).
- Automatic reconnection when the TV drops the idle Art Mode channel (after about 100 seconds).
- English and Polish interface. The language follows the system and falls back to English; `FRAMEGALLERY_LANG` forces one.
- Diagnostic log in `logs\log.txt` next to the executable, always in English, with secrets masked.
- Automatic installation of a missing .NET Desktop Runtime on first launch (DotnetRuntimeBootstrapper).
- Application icon, with the sizes 16 to 256 px, in the executable and in every window.

[Unreleased]: https://github.com/Misiu/SamsungTheFrameGalleryManager/compare/0.2.0...HEAD
[0.2.0]: https://github.com/Misiu/SamsungTheFrameGalleryManager/compare/0.1.1...0.2.0
[0.1.1]: https://github.com/Misiu/SamsungTheFrameGalleryManager/compare/0.1.0...0.1.1
[0.1.0]: https://github.com/Misiu/SamsungTheFrameGalleryManager/releases/tag/0.1.0
