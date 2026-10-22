# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Stack

Windows desktop application using C#, .NET, and Windows App SDK (WinUI 3). The user explicitly selected WinUI 3.

## Users

Windows users who run Codex CLI locally and need one reliable place to inspect and recover the toolchain.

## Product Purpose

Show whether the Codex toolchain is healthy, explain how traffic is routed, identify every related process and version, compare installed and official versions, manage installation, and safely start, stop, restart, or upgrade components.

## Positioning

The product combines package installation state, Windows process state, effective network route discovery, upstream version checks, and recovery actions in one local-native control surface.

## Operating Context

- Runs locally on Windows alongside Codex desktop and CLI processes.
- Detects npm-installed `@openai/codex`.
- Reads non-secret routing values from Codex configuration and environment variables.
- Administrative or destructive operations require explicit confirmation and report partial failures.

## Capabilities and Constraints

- Bilingual English and Simplified Chinese interface that follows the Windows display language by default, with a permanently bright theme.
- Dashboard health summary for Codex CLI, the ChatGPT desktop app, and proxy endpoints.
- Effective network route evidence derived from the configured proxy mode, the Windows system proxy state, the automatic/PAC configuration, the detected TUN adapter, and the user environment proxy.
- Related-process inventory including PID, executable, command line, start time, architecture, and best-effort version.
- Installed-versus-latest comparison using official npm registry metadata.
- Install, upgrade, reinstall, start, stop, restart, and safe bulk termination workflows. Termination uses an explicit path allowlist and always excludes Codex Beacon itself.
- Tailscale Windows service and local Tailnet device visibility, including node addresses and online state.
- NVM for Windows detection plus installed Node.js version inventory, installation, and active-version switching.
- Never displays or logs tokens, API keys, passwords, or authorization headers.
- A single append-only application log collects action output, installer progress, and application errors, with secrets redacted before a line is written.

## Brand Commitments

Working product name: Codex Beacon. The interface is bright, calm, technical, and uses native Windows interaction conventions.

## Evidence on Hand

- Local Codex CLI 0.147.0 from `@openai/codex`.
- No logo or external brand asset was supplied; the product must not fabricate endorsements or affiliation.

## Product Principles

- Explain health, do not merely color it.
- Make recovery obvious while keeping destructive actions deliberate.
- Prefer evidence from the current machine over hard-coded assumptions.
- Treat secrets and unrelated processes as out of bounds.
- Leave an actionable audit trail for every management operation.

## vNext product direction

The approved vNext structure is defined in [PRODUCT-REQUIREMENTS.md](PRODUCT-REQUIREMENTS.md). Its canonical navigation is Codex overview, Dependencies, Codex processes, Network, Tailscale, and Settings / About & updates.

The overview narrows to ChatGPT desktop and the user-managed Codex CLI. Dependency repair, network transport, and each optional module move to dedicated pages.

All byte-download operations must show an honest native progress experience including stage, transferred and total bytes when known, percentage, speed, elapsed time, and safe cancellation. Codex Beacon stores no provider keys and no other secret. Existing About and application-update features remain, while each component's task names and settings move to that component's management page.

## Accessibility & Inclusion

Target WCAG 2.2 AA-equivalent desktop behavior: keyboard navigation, visible focus, accessible names, non-color status cues, scalable text, and reduced-motion compatibility.
