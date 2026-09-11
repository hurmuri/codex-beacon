# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Stack

Windows desktop application using C#, .NET, and Windows App SDK (WinUI 3). The user explicitly selected WinUI 3.

## Users

Windows users who run Codex CLI, OpenCodex, and Codex Relay locally and need one reliable place to inspect and recover the toolchain.

## Product Purpose

Show whether the Codex toolchain is healthy, explain how traffic is routed, identify every related process and version, compare installed and official versions, manage installation, and safely start, stop, restart, or upgrade components.

## Positioning

The product combines package installation state, Windows process and scheduled-task state, proxy-chain discovery, upstream version checks, and recovery actions in one local-native control surface.

## Operating Context

- Runs locally on Windows alongside Codex desktop and CLI processes.
- Detects npm-installed `@openai/codex`, `@bitkyc08/opencodex`, and `codex-relay`.
- Recognizes the existing `Codex Relay` and `opencodex-proxy` scheduled tasks when present.
- Reads non-secret routing values from Codex configuration and environment variables.
- Administrative or destructive operations require explicit confirmation and report partial failures.

## Capabilities and Constraints

- Chinese interface with a permanently bright theme.
- Dashboard health summary for Codex CLI, OpenCodex, Codex Relay, and proxy endpoints.
- Proxy-chain visualization derived from process, environment, configuration, and listening-port evidence.
- Related-process inventory including PID, executable, command line, start time, architecture, and best-effort version.
- Installed-versus-latest comparison using official npm registry metadata.
- Install, upgrade, reinstall, start, stop, restart, and safe bulk termination workflows.
- Tailscale Windows service and local Tailnet device visibility, including node addresses and online state.
- NVM for Windows detection plus installed Node.js version inventory, installation, and active-version switching.
- Commands and task names are configurable so the app is not tied to one machine.
- Never displays or logs tokens, API keys, passwords, or authorization headers.

## Brand Commitments

Working product name: Codex Beacon. The interface is bright, calm, technical, and uses native Windows interaction conventions.

## Evidence on Hand

- Local Codex CLI 0.147.0 from `@openai/codex`.
- Local OpenCodex 2.49.0 from `@bitkyc08/opencodex`.
- Local Codex Relay 1.5.2 with a `Codex Relay` scheduled task.
- Local `opencodex-proxy` scheduled task and loopback proxy endpoints at ports 10100 and 51863.
- No logo or external brand asset was supplied; the product must not fabricate endorsements or affiliation.

## Product Principles

- Explain health, do not merely color it.
- Make recovery obvious while keeping destructive actions deliberate.
- Prefer evidence from the current machine over hard-coded assumptions.
- Treat secrets and unrelated processes as out of bounds.
- Leave an actionable audit trail for every management operation.

## Accessibility & Inclusion

Target WCAG 2.2 AA-equivalent desktop behavior: keyboard navigation, visible focus, accessible names, non-color status cues, scalable text, and reduced-motion compatibility.
