# RedWARP_WEB
[![F#](https://img.shields.io/badge/F%23-378BBA?style=for-the-badge&logo=fsharp&logoColor=white)](https://learn.microsoft.com/dotnet/fsharp/)
[![ASP.NET](https://img.shields.io/badge/ASP.NET-5C2D91?style=for-the-badge&logo=asp.net&logoColor=white)](https://dotnet.microsoft.com/apps/aspnet)
[![Cloudflare WARP](https://img.shields.io/badge/Cloudflare-WARP-FF6A00?style=for-the-badge&logo=cloudflare)](https://www.cloudflare.com/products/zero-trust/warp-client/)

**RedWARP_WEB** is a web-based generator for Cloudflare WARP WireGuard configurations, optimized for use with **AmneziaWG** clients. This project is a new branch/fork derived from the original [RedWARP_GUI](https://github.com/meizfl/RedWARP_GUI) project, reimagined as a lightweight, server-side web application built with **ASP.NET Core Minimal API** (.NET 10).

It allows users to easily generate customized `RedWARP.conf` files directly in the browser, without needing to install any tools locally. Perfect for quick, on-demand WARP configs with obfuscation features to improve compatibility in restricted networks.

## Features

- **One-click configuration generation** using the `wgcf` tool on the server side.
- Customizable options:
  - Endpoint (default: `51.38.153.32:5242`)
  - MTU (default: 1340)
  - IPv6 support (enable/disable)
  - DNS selection (Cloudflare, Google, Quad9, OpenDNS, or custom for IPv4/IPv6)
  - AmneziaWG or Wireguard-only mode
- Clean, responsive web UI with dark theme.
- Instant config preview and download as `RedWARP.conf`.
- Temporary session-based processing (files are deleted after generation for privacy).

## Why RedWARP?

Cloudflare WARP provides free, fast VPN access via WireGuard, but standard configs can be detected/blocked in some regions. **RedWARP** modifies the generated profile to include AmneziaWG-specific parameters, making it harder to detect while maintaining high performance.

This web version makes the process accessible to everyone — no CLI, no dependencies, just open the page and generate!

## Prerequisites for Deployment

- .NET 8+ SDK/runtime (tested on .NET 10)
- Linux/Windows/macOS server (tested on Linux)
- Pre-compiled `wgcf` binary for AMD64 (download from [ViRb3/wgcf releases](https://github.com/ViRb3/wgcf/releases))
  - Place it as `bin/wgcf_amd64` and make executable (`chmod +x bin/wgcf_amd64`)

## Quick Start

```bash
# Clone the repo
git clone https://github.com/yourusername/RedWARP_WEB.git
cd RedWARP_WEB

# Create bin folder and place wgcf binary
mkdir -p bin
# Download wgcf_amd64 into bin/ and chmod +x bin/wgcf_amd64

# Run the app
dotnet run
```

The app will start on `http://localhost:5000` (or configured port). Open it in your browser to use the generator.

For production: Use `dotnet publish` and deploy behind a reverse proxy (nginx, etc.) with HTTPS.

## Usage

1. Open the web page.
2. Adjust parameters as needed (enable AmneziaWG for obfuscation).
3. Click **Generate**.
4. Preview the config, then download `RedWARP.conf`.
5. Import into AmneziaWG (or any WireGuard client).

## Security & Privacy Notes

- All processing happens server-side in isolated temporary directories (deleted immediately after).
- No logs of generated keys or configs are kept.
- Free WARP accounts are limited; excessive use may trigger Cloudflare rate limits.

## Relation to RedWARP_GUI

This project started as a web adaptation of the original RedWARP_GUI desktop application, shifting the heavy lifting (wgcf execution) to the server for broader accessibility.
## RedWARP over Relay(Alternative Servers)
**Access point list:**
| Location     | IP             | WARP | ZeroTrust | Provider   | Access         | Speed | MTU|
|--------------|----------------|------|-----------|------------|-------------|-------| ------|
| Roubaix, FR  | 147.135.212.152| 5242 | 5241      | OVH Cloud  | Free-to-use | 3 GbE | 1340|
| Warsaw, PL   | 51.38.153.32   | 5242 | 5241      | OVH Cloud  | Free-to-use | 3 GbE | 1340|
| Frankfurt, DE| 51.38.107.252  | 5242 | 5241      | OVH Cloud  | Free-to-use | 3 GbE | 1340|
## License

GNU/GPL-3.0 license

## Credits

- Original concept and modifications inspired by various WARP+AmneziaWG generators.
- `wgcf` by [ViRb3](https://github.com/ViRb3/wgcf)
- UI built with plain HTML/CSS/JS for simplicity.

Feel free to open issues or PRs! 🚀

## ⭐ Star this project!

If you like it, hit that star button — it means the world! 🌟

## For donations
[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/V7V61YY60F)
