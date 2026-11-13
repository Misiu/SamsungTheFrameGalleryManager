# Samsung The Frame Gallery Manager

An open-source desktop app for managing the **art gallery of a Samsung The Frame TV** from your computer: find the TV on your network, browse your photos, delete them, show one on the screen, and upload new photos cropped to the screen size. No Samsung account, no subscription, no cloud.

> **Status: early prototype (v0.1.0).** It works end to end on a 2025 The Frame (Tizen), but it has been tried on a single TV. Expect rough edges. The user interface is currently in Polish.
>
> This is an independent project and is **not affiliated with or endorsed by Samsung**. "Samsung" and "The Frame" are trademarks of their owners.

## Features

- **Find your TV**: automatic discovery on the local network (SSDP), or add it by IP address.
- **Gallery**: thumbnails of your photos (optionally Art Store and factory images too, read-only).
- **Upload**: pick files or drag & drop them onto the gallery window, crop each one to the screen's 16:9 area, and the app sends a JPEG in the TV's native resolution (usually 3840×2160).
- **Right-click menu**: *Show on TV* and *Delete*. Deleting is limited to your own photos.
- **Self-healing connection**: the TV drops the Art Mode channel after about 100 s of inactivity; the app reconnects and retries on its own.

## Privacy and safety

- Talks **only to devices on your local network** (private IPv4 ranges); public addresses are rejected.
- The TV uses a self-signed certificate. Instead of disabling validation, the app **pins the certificate** (trust on first use) and refuses a different one later.
- The pairing token is stored **encrypted** (Windows DPAPI) in `%APPDATA%\FrameGallery\tvs.json`.
- No telemetry and no network traffic besides your TV. The only exception is the optional .NET runtime download that the bootstrapper offers (from Microsoft) when the runtime is missing.
- Diagnostic logs (`logs\log.txt` next to the executable) contain the raw protocol traffic with **secrets masked** (tokens, keys, SSID, MAC), so a log can be attached to a bug report.

## Getting started

Requirements: Windows 10/11 and the **.NET 10 Desktop Runtime**. The executable is wrapped by [DotnetRuntimeBootstrapper](https://github.com/Tyrrrz/DotnetRuntimeBootstrapper), which offers to install a missing runtime on first launch.

1. Make sure the computer and the TV are on the same network and the TV is on.
2. Start the app and click **Szukaj TV** (find TV), or **Dodaj po IP…** (add by IP).
3. Double-click the TV. On the first connection the TV asks you to **allow** the app; confirm with the remote.
4. Use the gallery. Right-click a photo for *Pokaż na TV* / *Usuń*.

If discovery finds nothing, the Windows firewall may be blocking the replies; add the TV by IP instead.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet build FrameGallery.slnx
dotnet test tests/FrameGallery.Tests
dotnet run --project src/FrameGallery.App
```

Packages come from nuget.org only (see `nuget.config`). The bootstrapper is generated on every build; to debug the app, build with `-p:GenerateBootstrapperOnBuild=false` because the bootstrapper host does not support debugging.

### Repository layout

| Path | What it is |
|---|---|
| `src/FrameGallery.Core` | Platform-independent protocol client: SSDP discovery, REST device info, WebSocket channels, Art Mode requests, certificate pinning, paired-TV store with pluggable secret protection. Targets `net8.0` and `net10.0`, no UI or native dependencies. |
| `src/FrameGallery.Imaging` | Load, crop and JPEG encoding on [SkiaSharp](https://github.com/mono/SkiaSharp), usable on Windows, macOS and Linux. |
| `src/FrameGallery.App` | WinForms prototype (Windows only). |
| `tools/FrameGallery.Probe` | Console diagnostics for the protocol. `scan`, `list` and `idle` only read from the TV; **`roundtrip` uploads and then deletes a test image, and `select` changes what the TV displays**. |
| `tests` | Unit tests (discovery, protocol parsing, crop math, store, log redaction, imaging). |

## Roadmap

- Cross-platform UI with [Avalonia](https://avaloniaui.net/) for Windows, macOS and Linux (replacing the WinForms prototype). Mobile is out of scope: Samsung already offers an official app.
- Portrait orientation, matte (passe-partout) selection, slideshow settings.
- Localization.

## How the protocol was learned

Samsung's Art Mode interface is **undocumented**, so it may change with TV firmware updates. This project is an independent implementation, based on observing the traffic of a real TV and on the work of the community, for example [xchwarze/samsung-tv-ws-api](https://github.com/xchwarze/samsung-tv-ws-api) and [NickWaterton/samsung-tv-ws-api](https://github.com/NickWaterton/samsung-tv-ws-api). Please check their licenses before reusing any of their code.

## Contributing

Issues and pull requests are welcome. For bug reports, please attach `logs\log.txt` (secrets are masked, but skim it before sharing) and mention the TV model and year. Reports from other Frame models are especially valuable.

## License

[MIT](LICENSE)
