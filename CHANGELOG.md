﻿# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and releases use [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Fixed

- Closing the main window now hides Codex Beacon to the system tray. The background process and tray icon remain active until Exit is selected from the tray menu; internal language reloads and application updates still close cleanly.

- Moved OpenCodex to the second navigation position, added a persistent system tray icon with open and exit actions, and added a Windows startup setting that launches Codex Beacon directly into the tray.

- Reorganized OpenCodex model management by provider. Each provider now has its own catalog read action, model list, all-model visibility switch, and per-model visibility controls, while the UI documents OpenCodex's short-lived runtime catalog cache.

- Added a unified append-only application log window for action output, npm progress, launcher failures, and application errors. OpenCodex upgrades now stop the running proxy before npm replaces its bundled executable, stream each npm output line, and verify the installed package before reporting failure.

- Corrected the Download Sources layout so labels and full-width selectors align consistently, and rebuilt OpenCodex model visibility as a compact table with one header row and left-aligned switches.
  - Aligned OpenCodex model catalog with the live dashboard catalog by querying live runtime models directly instead of static configuration entries, and fixed selector resolution for native OpenAI models.

- Completed dependency and OpenCodex operational state:
  - Latest-version refresh now updates bound dependency rows immediately and uses selected Node/npm sources, GitHub, and WinGet fallbacks. NVM plus App Installer, WinGet, Microsoft Store source, Node.js, and npm all display current and latest values.
  - Download-source choices show both the provider name and full URL.
  - Removed the standalone Providers page. OpenCodex now shows whether its proxy is active for ChatGPT and Codex CLI, reports the active provider, and offers client-specific integration actions when inactive.
  - OpenCodex models now expose per-model visibility switches backed by `models enable/disable`; hidden models remain available for re-enabling.
  - OpenCodex install, start, upgrade, integration, and restart actions close all allowlisted ChatGPT/Codex processes, restart the proxy, wait for its listener, and reopen the ChatGPT client. Codex Relay now presents current and latest versions as separate fields.

- Clarified core product state and dependency/network workflows:
  - Codex Beacon now enforces one active app instance. Starting it again closes the previous window and force-stops it only when a graceful close does not finish, then launches the new instance. The single-file launcher performs the replacement before updating its extraction cache.
  - Codex CLI now shows an explicit signed-in, signed-out, or unverifiable account state; the login action is disabled after a verified sign-in. Login checks now follow the resolved npm CLI path instead of borrowing state from a different bundled executable.
  - ChatGPT desktop health is no longer derived from Codex CLI authentication, so an up-to-date running desktop client no longer appears as needing attention because the CLI is signed out.
  - NVM, Node.js, and npm runtime cards now show current and latest versions. The Node.js selector merges `nvm list` with the selected mirror catalog and uses one state-driven action to switch an installed version or install and switch a missing version.
  - Removed the duplicate NVM entry from the Windows prerequisite list. Node.js mirrors now include official, Tsinghua, Alibaba Cloud, Tencent Cloud, and Huawei Cloud choices; npm registries use the standard nrm preset list.
  - The Network page now distinguishes configured mode from the observed effective path and reports Windows proxy address, automatic/PAC state, environment proxy, and active TUN adapter evidence.

- Fixed single-file runtime packaging and Windows App Runtime missing dialog:
  - Resolved the 'Required components of the Windows App Runtime are missing Version 1.8' prompt in the Slim build by packaging with WindowsAppSDKSelfContained=true, bundling WinUI 3 native assets while maintaining framework-dependent .NET runtime.
  - Separated extraction cache targets between Portable (app-portable) and Slim (app-slim) with distinct payload content signatures, eliminating extraction collisions and file-lock conflicts when launching both variants.
- Enhanced version presentation and automatic update checks for ChatGPT Desktop and Codex CLI:
  - Restructured cards to display explicit, dedicated rows for 'Current Version' (当前版本) and 'Latest Version' (最新版本), accompanied by status badges ('Up to date' / 'Update available').
  - Enabled automatic background latest-version querying during status refresh with proxy support, plus added an on-card 'Check for updates' button for the desktop app.

- Fixed ChatGPT desktop client upgrade workflow from the Overview page:
  - Resolved an issue where clicking 'Store install/upgrade' failed to update the client because WinGet reports 'No available upgrade found' for msstore packages whose version metadata is marked as Unknown, or triggers '0x80073d02' when processes are active.
  - Dynamically routes desktop actions: buttons accurately display 'Store install', 'Store upgrade', or 'Open Store' according to current package and update availability.
  - Upgrades seamlessly launch the official Microsoft Store product details page ('ms-windows-store://pdp/?ProductId=9PLM9XGG6VKS') so the Store native engine safely handles downloading, process scheduling, and updating with clear user guidance.


### Added

- Canonical 9-page information architecture: Codex Overview, Dependencies, Codex Processes, Network & Proxy, Providers, OpenCodex, Tailscale, Codex Relay, and Settings.
- Pipeline asynchronous streaming refresh: local machine snapshot loads sub-second on startup, while network probes, public egress, latest remote version lookups, and OpenCodex model catalogs update concurrently in the background without UI freeze.
- Step-by-step Dependency management combining NVM, Node.js, and npm with in-card version switching, new version installation, and mirror configuration, alongside the Windows Store dependency chain (App Installer → WinGet → Store source).
- Seamless Network & Proxy workflow: configure proxy mode (System / TUN / Custom HTTP), run immediate connectivity tests with latency feedback, apply system environment proxy variables, and verify OpenAI public egress.
- Unified Providers management: highlights active Codex router, restores detection of system providers (OpenAI direct, local OpenCodex proxy on port 10100, and config.toml entries) with one-click activation and connectivity testing, alongside a secure custom provider vault supporting hidden-by-default password fields and local ACL-restricted storage.
- Enhanced software version management for OpenCodex, Tailscale, and Codex Relay: includes install status, current vs latest version summary, update checks, service start/stop/restart, and model testing.
- Project open-source metadata on the Settings page: repository URL, version badge, and MIT license details.

### Added

- Build version stamping: every real build increments `build/version.txt` and applies the result to the assembly metadata, so each artifact carries a distinct version. The launcher reads the same value instead of bumping it, keeping its payload stamp tied to the app it embeds.
- A configurable Node.js download mirror for `nvm install` (`NvmMirror` setting), with an automatic fallback to the official distribution host when the mirror fails.
- Per-component version evidence in the UI, such as the Codex App Mirror manifest that tracks Microsoft Store product `9PLM9XGG6VKS`.
- Streamed native tool progress plus a cancel button for long-running actions, and hard timeouts on both collection and actions so the window never appears frozen.
- Richer public egress details: country, region, city, ISP, AS number, hosting/proxy classification, and a trust score, sourced from `ip.net.coffee`.
- A "open full IP check" entry point on the Network page.
- In-app self-update against the official GitHub Releases channel: startup check with a window-top banner, manual check, streamed download with progress and transfer speed, SHA-256 verification against the release's `SHA256SUMS.txt`, skip-this-version, and an in-place replace-and-restart. The package type is detected at runtime (a self-contained build ships `coreclr.dll` next to the app), so portable installs fetch `CodexBeacon-portable.exe` and slim installs fetch `CodexBeacon-slim.exe`.
- A new "About & update" section on the Settings page showing the current version, detected package type, an auto-check toggle, and the full update status surface with release notes.
- The single-file launcher now hands its own path to the application (`--launcher`). The application runs from the extracted `app-portable` copy and cannot infer that path, but the updater needs it to replace the install package in place. Because the launcher exits before the app starts, that file is never locked, so the swap needs neither elevation nor a reboot.

### Changed

- Status collection now uses a semantic-key contract: the PowerShell scripts emit only resource keys and arguments joined by U+001F, and every user-visible sentence lives in `Strings/*/Resources.resw`.
- Codex CLI and desktop versions are detected per install source (npm global, `PATH`, or the copy bundled with the desktop app) rather than assuming a single installation.
- The Network page now shows only the OpenAI-view public egress and provider switching; the ambiguous proxy-chain, candidate-endpoint, and remote-connection views were removed, along with the Dashboard's "live data pipeline" block.
- Typography is centralised in `App.xaml` through a named type ramp; no control sets `FontSize` locally.
- `Localization.GetXUid` resolves strings that are normally bound through `x:Uid`. A dotted resw name such as `Foo.Content` compiles into the hierarchy `Resources/Foo/Content`, so the flat `Get` path can never reach it; code that needs a label whose text also changes at runtime now shares the very same entry instead of duplicating the string.
- Both READMEs described ZIP downloads that the release workflow has not produced since the move to single-file executables; the download tables now list the actual assets and explain the expand-on-first-run layout the updater relies on.

### Fixed

- Settings page right-side component clipping caused by unrestricted horizontal scroll measurement.
- Provider list omission of local OpenCodex proxy and existing config.toml providers.
- Slow blocking status collections by caching npm package resolution directly from local package manifests rather than invoking heavy node processes.
- `nvm install` could hang indefinitely on networks where `nodejs.org` is unreachable.
- Switching to the OpenCodex provider did not write `openai_base_url`, so traffic was never routed through the local proxy; the key is now injected when absent and removed when switching away.
- Provider switching could place `openai_base_url` inside a TOML table when `model_provider` appeared after a section header.
- Codex CLI install/upgrade was rejected with "Codex CLI is not a service" before it could reach the npm package step.
- The six per-component action buttons used `.Label` resource keys while rendering as `Button`, which resolves `.Content`, leaving them untranslated in English.
- `GeneralSettingsExpander` had no `x:Uid`, so the general settings header stayed Chinese in English.
- Sign-in state was always reported as unknown because `$ErrorActionPreference = 'SilentlyContinue'` swallowed the native `codex login status` output.
- The collector emitted non-ASCII sentinel characters that corrupted its JSON under a non-UTF-8 console code page.
- Language switching now genuinely changes the UI. Previously both the `ResourceLoader` and XAML `x:Uid` resolution followed the system language, so the language picker only persisted a setting. The app now drives `Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride`, which works without package identity and is honoured by MRT Core - the same path XAML `x:Uid` uses - plus an explicit `language` qualifier on its own resource context.

## [0.3.0] - 2026-09-11

### Added

- Dual-layer live data pipeline architecture: separate Model Dispatch Pipeline and Network Transport Proxy flows.
- Enriched public egress diagnostics: displaying flag emoji, country, region, city, ISP/organization, AS number, and residential/IDC line classification.
- Dedicated Tailscale management page with unwrapped multi-IP address display and status cards.
- Process management quick actions: one-click termination of all Codex/ChatGPT processes and clean app restart.
- Administrator privilege indicators (🛡️) on actions requiring UAC elevation, backed by comprehensive documentation.

### Changed

- Cleaned up Dashboard: removed blinking progress bar, removed Tailscale from home page to focus strictly on services and data pipelines.
- Streamlined Installation page: eliminated redundant expanders and crash-prone InfoBars; redesigned compact NVM and prerequisite cards.

## [0.2.1] - 2026-09-11

### Added

- Standard open-source "About" sections with project motivation and architectural highlights in English and Chinese READMEs.
- Strict verification rules in `AGENTS.md` requiring local packaging and live desktop execution before pushing or releasing.

### Fixed

- Unpackaged WinUI 3 launch crash caused by `ApplicationLanguages.PrimaryLanguageOverride` requiring package identity.
- Missing `resources.pri` in published output by adding automated MSBuild post-publish copy targets.

## [0.2.0] - 2026-09-11

### Added

- English and Simplified Chinese application resources, diagnostics, and action results.
- A display-language setting that follows Windows by default and switches immediately without restarting the process.
- An English default README with a prominent Simplified Chinese documentation link.

## [0.1.1] - 2026-09-11

### Changed

- Release both a self-contained portable build and a smaller runtime-dependent build.
- Publish SHA-256 checksums and explicit runtime guidance with each GitHub Release.

## [0.1.0] - 2026-09-11

### Added

- Native WinUI 3 light-theme control center with Windows Community Toolkit Settings controls.
- State-driven install, login, start, stop, restart, and upgrade actions.
- Codex desktop and Codex CLI version, account, process, and update detection.
- Optional OpenCodex Proxy and Codex Relay management without making either a core dependency.
- NVM for Windows, Node.js, and npm prerequisite and version management.
- Tailscale service, account, and Tailnet device status.
- Evidence-based provider route, public egress, remote connection, and inactive endpoint views.
- iOS AppIcon asset catalog and Windows application icon derived from one master artwork.

[Unreleased]: https://github.com/hurmuri/codex-beacon/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/hurmuri/codex-beacon/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/hurmuri/codex-beacon/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/hurmuri/codex-beacon/compare/v0.1.1...v0.2.0
[0.1.1]: https://github.com/hurmuri/codex-beacon/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/hurmuri/codex-beacon/releases/tag/v0.1.0
