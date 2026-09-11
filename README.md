# Codex Beacon

<p align="center"><img src="Assets/app-icon.png" width="128" alt="Codex Beacon icon"></p>

<p align="center"><strong><a href="README.zh-CN.md">简体中文说明</a></strong></p>

[![Build](https://github.com/hurmuri/codex-beacon/actions/workflows/build.yml/badge.svg)](https://github.com/hurmuri/codex-beacon/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Windows](https://img.shields.io/badge/Windows-10%201809%2B-0078D4.svg)](https://github.com/hurmuri/codex-beacon)

Codex Beacon is a native, light-theme WinUI 3 control center for inspecting and managing the Codex desktop app, Codex CLI, OpenCodex Proxy, Codex Relay, Node.js/NVM/npm, and Tailscale on Windows.

Version `0.2.0` supports English and Simplified Chinese. It follows the Windows display language by default, and the language can be changed under Settings. The Codex desktop app and CLI are core features; OpenCodex Proxy and Codex Relay are independent optional modules. Installing an extension never changes the active Codex provider automatically.

## Upstream projects

- [Codex CLI · openai/codex](https://github.com/openai/codex)
- [OpenCodex · lidge-jun/opencodex](https://github.com/lidge-jun/opencodex)
- [Codex Relay · gronxb/codex-relay](https://github.com/gronxb/codex-relay)
- [NVM for Windows](https://github.com/coreybutler/nvm-windows)
- [Node.js](https://github.com/nodejs/node) and [npm CLI](https://github.com/npm/cli)
- [Tailscale](https://github.com/tailscale/tailscale)

## Implementation references

- [Wangnov/Codex-App-Manager](https://github.com/Wangnov/Codex-App-Manager): reference for Codex desktop version discovery and management.
- [v2fly/domain-list-community](https://github.com/v2fly/domain-list-community): reference for network-domain classification. Its rules are not bundled or read by the current release.

## Implemented

- Separate version, process, sign-in, and update detection for the Codex desktop app and Codex CLI.
- Local/latest version comparison for `@openai/codex`, `@bitkyc08/opencodex`, and `codex-relay` using the official npm registry.
- Three service controls per desktop row with state-driven install, sign-in, start, stop, restart, and upgrade actions.
- NVM for Windows detection, installed Node.js inventory, version installation, and active-version switching.
- Node.js ≥ 22.14.0 and npm prerequisite checks before npm-based modules can be installed.
- Evidence-based active proxy path built from `model_provider`, `base_url`, listener ownership, and observed TCP connections.
- Configured but inactive endpoints shown separately from the active data path.
- Machine public egress separated from remote endpoints contacted by Codex-related processes.
- Tailscale service, local node, Tailnet device, address, online, and last-seen status.
- Allowlisted bulk recovery that excludes the Codex desktop app, Codex Beacon, and unrelated Node.js processes.
- English and Simplified Chinese UI, diagnostics, action results, and instant in-app language switching.

## Download and run

Each GitHub Release provides two x64 builds:

| File | Best for | Runtime requirements |
| --- | --- | --- |
| `CodexBeacon-portable-win-x64.zip` | Recommended; extract and run | .NET 10 and Windows App SDK are included |
| `CodexBeacon-runtime-dependent-win-x64.zip` | Managed environments that already deploy the runtimes | [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/download/dotnet/10.0) + [Windows App Runtime 1.8 x64](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) |

Extract the complete directory, then run `CodexBeacon.exe`. Do not launch the executable directly inside the archive. Windows does not include .NET 10 or the required Windows App Runtime by default; choose the `portable` build when unsure.

Both builds require x64 Windows 10 1809 (build 17763) or later. Status collection uses Windows PowerShell 5.1, which is included with supported Windows versions. Unsigned community builds may trigger Windows SmartScreen. Network access is used for version checks, official installers, sign-in, and public-egress discovery.

Node.js, npm, NVM, Tailscale, OpenCodex, and Codex Relay are not prerequisites for launching Codex Beacon. They are required only for their corresponding management features. The UI detects missing dependencies and guides users through install → sign in → start. OpenCodex and Codex Relay require Node.js and npm; Relay requires Node.js 22.14.0 or newer.

The self-contained directory is currently about 214 MB and compresses to about 87 MB. Most of that size comes from the bundled .NET 10 runtime, WinUI 3 / Windows App SDK, DirectML, and ONNX Runtime components.

## Build from source

Building requires x64 Windows 10 1809 or later and the .NET 10 SDK.

```powershell
dotnet build .\CodexBeacon.csproj -c Release
dotnet run --project .\CodexBeacon.csproj
```

Create a self-contained directory:

```powershell
dotnet publish .\CodexBeacon.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o .\publish\win-x64
```

## Permissions and safety boundaries

- Routine inspection does not require administrator privileges.
- NVM switching, Windows service control, and global npm installation may require elevated permissions depending on how the local tools were installed. Codex Beacon reports the failure and never bypasses UAC silently.
- Tokens, authorization headers, passwords, cookies, and API keys are never read or displayed.
- A remote service IP is a process destination, not the machine's public egress IP. The UI displays them separately.
- The installed Codex desktop version comes from the local `OpenAI.Codex` MSIX/AppX package. The latest Windows package version comes from the Codex App Mirror manifest, which mirrors Microsoft Store product `9PLM9XGG6VKS`. The UI identifies that source, while installation and upgrades always open the official Microsoft Store page.

See [CONTRIBUTING.md](CONTRIBUTING.md), [DESIGN.md](DESIGN.md), [SECURITY.md](SECURITY.md), and the [MIT license](LICENSE).

## Local adaptation

The Settings page can override the `Codex Relay` and `opencodex-proxy` scheduled-task names. These listener ports are detected by default, but the active path only includes an endpoint supported by the selected provider and observed runtime evidence:

- OpenCodex Proxy: `127.0.0.1:10100`
- Codex Relay: `127.0.0.1:8787`
- Other providers: parsed dynamically from `%USERPROFILE%\.codex\config.toml`
