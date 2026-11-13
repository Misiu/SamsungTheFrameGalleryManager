# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://github.com/Misiu/SamsungTheFrameGalleryManager/compare/0.1.1...HEAD
[0.1.1]: https://github.com/Misiu/SamsungTheFrameGalleryManager/compare/0.1.0...0.1.1
[0.1.0]: https://github.com/Misiu/SamsungTheFrameGalleryManager/releases/tag/0.1.0
