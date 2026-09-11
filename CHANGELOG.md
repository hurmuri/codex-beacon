# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and releases use [Semantic Versioning](https://semver.org/).

## [Unreleased]

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
