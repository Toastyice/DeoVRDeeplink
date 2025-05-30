# DeoVR Deeplink Proxy Plugin for Jellyfin

> [!CAUTION]
> All movies are exposed UNAUTHENTICATED from DeoVRDeeplink/json/videoID/response.json

A plugin for Jellyfin that enables secure, expiring, signed video stream URLs for use with [DeoVR](https://deovr.com/) and other clients needing quick access to individual media files without exposing your Jellyfin credentials.

## Features
- **UI Changes:** adds a Play in DeoVR button
- **Secure signed links:** Temporary, HMAC-signed links for proxying video streams.
- **Expiry enforcement:** Links are only valid for a short time window.
- **Chunked proxy streaming:** Efficient forwarding without direct Jellyfin API exposure.
- **DeoVR-compatible JSON responses:** Works seamlessly with [DeoVR](https://deovr.com/).
- **Embedded client JS and icon resources.**

---

## Getting Started

### Prerequisites

- [Jellyfin Media Server](https://jellyfin.org/)
- .NET 8.0 SDK or later (for building)
- DeoVR for testing client integration (optional)

### Installation

1. **Build the plugin:**

    ```bash
    dotnet build -c Release
    ```

2. **Copy the plugin DLL**  
    Place the resulting `.dll` (and dependencies) in your Jellyfin plugins directory (typically `Jellyfin/Server/plugins`).

3. **Restart Jellyfin.**  
    The plugin will be loaded automatically.

### Configuration

- In the Jellyfin dashboard, configure:
  - **Proxy Secret:** _(A strong random string used for signing proxy URLs)_.
  - **Jellyfin API Key:** _(A user with sufficient privileges to stream media from the server)_.

### Usage

1. **DeoVR Integration:**  
    Click the Open in DeoVR button

## Security

- Streams are protected with expiring, HMAC-signed tokens.
- Links cannot be forged or reused after expiry.
- Secret is never sent to the client.
- Change expiry time in `BuildVideoResponse()` (`AddMinutes(5)`).
- **RECOMMENDED:** Always use HTTPS to avoid leaking signed URLs.

See [plugin source comments](./DeoVrDeeplinkController.cs) and [security notes](#security).

---

## Advanced

- **Jellyfin port detection:**  
    The plugin automatically detects Jellyfin’s HTTP/HTTPS port from the server configuration—no need to hardcode.
- **ClientScript & Icon endpoints:**  
  - `/DeoVRDeeplink/ClientScript`
  - `/DeoVRDeeplink/Icon`

---

## Development

- Fork and clone this repository.
- Build with your preferred .NET IDE or `dotnet` CLI.
- Contributions and PRs welcome!

---

## Credits
- This plugin was inspired by a lack of a proper VR player that supports Jellyfin
- [Jellyfin Media Server](https://jellyfin.org/)
- [DeoVR](https://deovr.com/)
- [InPlayerEpisodePreview (Heavily inspired the way the UI is edited)](https://github.com/Namo2/InPlayerEpisodePreview)
---

**Happy streaming!**
