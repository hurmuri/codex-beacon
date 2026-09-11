# Codex Beacon vNext product requirements

Status: approved product direction; implementation may lag behind this document.

Last updated: 2026-09-11.

This document is the canonical description of the vNext information architecture, functional boundaries, state model, download experience, and credential handling. `PRODUCT.md`, `DESIGN.md`, `UX-CONTRACT.md`, the README files, and the project website must remain consistent with it.

## Product boundary

Codex Beacon is a Windows-only native manager for the ChatGPT desktop package, the user-managed Codex CLI, their prerequisites and network path, and optional OpenCodex, Tailscale, and Codex Relay integrations.

- ChatGPT desktop and the user-managed Codex CLI are the product core.
- OpenCodex, Tailscale, and Codex Relay remain separate modules. Missing optional modules do not make the core unhealthy.
- OpenCodex owns its supported client integrations. Codex Beacon invokes OpenCodex's declared CLI/API capabilities instead of duplicating or reverse-engineering its internal configuration.
- Configured state is not proof of active state. Runtime health requires the relevant process, listener, route, or observed connection evidence.

## Navigation

The canonical navigation order is:

1. **Codex overview**
2. **Dependencies**
3. **Codex processes**
4. **Network**
5. **Providers**
6. **OpenCodex**
7. **Tailscale**
8. **Codex Relay**
9. **Settings / About & updates**

The desktop layout uses `NavigationView`. Pages preserve state while navigating. Network management is a dedicated page, not an overview subsection.

## Codex overview

The overview is intentionally limited to the two core products.

### ChatGPT desktop

Show:

- Microsoft Store/AppX package installation state;
- running and sign-in state when evidence is available;
- installed version, latest known version, source, and last check time;
- `Check for updates`, `Install`, `Upgrade`, and `Launch` actions enabled by state;
- a `Set up` link to Dependencies when App Installer, WinGet, or the Store source is unavailable.

The primary Store product is `9PLM9XGG6VKS`, whose installed package identity is currently `OpenAI.Codex`. Store IDs must be passed to WinGet as unique positional IDs rather than combined with exact-match filtering.

### Codex CLI

Show:

- the `codex` executable currently resolved for a normal user shell;
- its version, absolute path, and installation owner;
- the npm-managed `@openai/codex` version and latest registry version;
- installation, sign-in, update, and upgrade actions;
- a warning when the active executable and npm-managed package are different.

Other Codex executables bundled by ChatGPT, VS Code, Relay, or other tools belong under `Other installations`; they must not silently replace the user-managed CLI in the headline version.

## Dependencies

Dependencies are presented as two explicit prerequisite chains:

```text
App Installer -> WinGet -> msstore source -> ChatGPT desktop
NVM -> active Node.js -> npm -> npm Registry -> Codex CLI
```

Each step shows state, version, resolved path or endpoint, evidence time, failure reason, and the next valid action. A mutation is followed by a fresh authoritative probe.

Required capabilities:

- detect Microsoft Store registration, App Installer, WinGet, the `msstore` source, policy blocks, and required endpoint failures;
- open the visible official Store page for ChatGPT;
- use visible `winget install 9PLM9XGG6VKS --source msstore` as the non-Store-UI installation route;
- repair or install WinGet from Microsoft's official App Installer release channel when WinGet is absent;
- detect NVM for Windows, list installed Node.js versions, install a selected version, and switch the active version;
- configure and test a Node.js distribution mirror, with a visible official-source fallback;
- configure, test, and restore the npm Registry independently from the Node.js mirror;
- refresh process environment and explain when a terminal or the app must be reopened after PATH changes.

There is no general consumer offline-package fallback for ChatGPT. Store package download can require Microsoft Entra administrator authorization. The UI must not offer an unofficial mirror as an official installer.

## Codex processes

Preserve the existing process inventory and guarded recovery behavior. Continue to show role, PID, parent PID, executable version, architecture, path, and start time. Process termination uses the explicit allowlist and excludes Codex Beacon, ChatGPT desktop, and unrelated Node.js processes.

## Network

The Network page separates transport state from model-provider state.

### System transport

- DNS, TCP, and TLS reachability;
- WinINET and WinHTTP proxy state;
- supported environment-variable proxies;
- TUN adapter detection;
- configured custom HTTP proxy endpoint;
- machine public egress IP, provider, geography, and check time.

### Client checks

ChatGPT desktop and Codex CLI have separate checks. Results distinguish:

- internet unavailable;
- endpoint unreachable;
- endpoint reachable but authentication rejected;
- model or route not found;
- rate limited;
- proxy configured but not listening;
- listener present but not selected by the client;
- selected route active with observed connections;
- timeout or TLS failure.

A proxy route is active only when the selected provider supports it, the endpoint matches, listener ownership is known, and observed TCP connections support the conclusion. Public egress is never presented as a remote service IP.

## Providers

The Providers page supports:

- list, add, edit, duplicate, remove, and set active;
- provider name, base URL, adapter/API type, authentication mode, default model, and source;
- duplicate detection and endpoint validation;
- selectable connectivity-test model, defaulting to `gpt-5.4`;
- layered DNS/TCP/TLS, model-discovery, and minimal inference tests;
- latency and specific error classification;
- configuration backup and rollback around every mutation.

### Provider key input

- The operator may paste or type the raw provider key into a password-style field.
- The value is hidden by default and can be revealed only through an explicit press-and-hold or toggle control with an accessible name.
- When the operator saves the provider, Codex Beacon may store the raw key in `%USERPROFILE%\.CodexBeacon\providers.json` alongside that provider's non-secret configuration.
- `.CodexBeacon` is explicitly assigned the Windows `Hidden` attribute; the leading dot alone is not treated as the hiding mechanism.
- The credential file is plaintext by product decision and must be restricted to the current Windows user. It must never inherit access that makes it readable by other ordinary local users.
- The key must not be uploaded, logged, included in diagnostics, placed on a process command line, echoed in errors or status text, written to crash reports, or included in a default settings/diagnostic export.
- Loading an existing provider may populate the password control, but the value remains masked by default. Lists and summaries show only a neutral `Key saved` state, never a prefix, suffix, length, or fingerprint.
- Saving uses an atomic replace and preserves the current-user-only access rule. Removing a provider or selecting `Clear key` removes the persisted value.
- Connectivity tests pass the key through standard input, a protected local IPC body, or another non-command-line channel and redact downstream failures before they reach the UI.

## OpenCodex

OpenCodex has a dedicated page with:

- dependency, installation, stable/preview version, and update state;
- service install, start, stop, restart, and health;
- the OpenCodex scheduled-task/service name, listener port, startup behavior, and other OpenCodex-specific settings;
- configured providers and the current default;
- static and live model catalogs, selected models, and last synchronization time;
- selectable model connectivity tests;
- native Codex integration state, including absent, current, drifted, blocked, and pending synchronization;
- `doctor`, synchronization, and dashboard entry points.

Feature availability is driven by `opencodex capabilities --json`. Current integrations should prefer structured commands such as `health --json`, `provider list --json`, `models list --json`, live-model discovery, provider testing, and `integration native` rather than parsing private files.

## Tailscale

Keep Tailscale independent. Preserve package, Windows service, sign-in, local node, Tailnet device, address, online, and last-seen state. Tailscale absence does not degrade ChatGPT or Codex CLI health.

## Codex Relay

Keep Codex Relay independent and optional. Preserve installation, version, task/service, listener, start, stop, restart, update, login, and pairing state where supported. The Relay scheduled-task/service name, listener, startup behavior, and other Relay-specific settings live on this page. Relay-bundled Codex executables are inventory entries, not the managed default CLI.

## Settings / About & updates

Preserve the existing About and application-update experience, including the running Codex Beacon version, package type, release notes, manual update check, update availability, skipped version, download, verification, replacement eligibility, and restart flow.

This page owns only application-wide preferences and Codex Beacon itself, such as language, refresh interval, update channel/state, and About information. Component-specific configuration must live with the component it controls:

- Node.js mirror and npm Registry -> Dependencies;
- system, TUN, and custom HTTP proxy options -> Network;
- provider definitions, test model, key input, and saved-key lifecycle -> Providers;
- OpenCodex task/service name, port, startup, sync, and integration settings -> OpenCodex;
- Tailscale service options -> Tailscale;
- Codex Relay task/service name, listener, startup, login, and pairing settings -> Codex Relay.

The Settings page must not become a second inventory of component controls.

## Download experience

Every operation that downloads bytes must expose a consistent, non-blocking progress surface. This includes application self-update, ChatGPT/App Installer acquisition, NVM, Node.js, npm packages, OpenCodex, Codex Relay, Tailscale, and any downloaded metadata large enough to create a visible wait.

While the source reports total size, show:

- current stage (`Resolving`, `Connecting`, `Downloading`, `Verifying`, `Installing`, or `Finalizing`);
- downloaded bytes and total bytes;
- percentage and native progress bar;
- transfer speed;
- elapsed time and estimated time remaining when reliable;
- source host or officially labelled mirror;
- `Cancel` where cancellation is safe.

When total size is unknown, use an indeterminate native progress bar but still show stage, transferred bytes when available, elapsed time, and native tool output summarized into a user-facing status. Never invent a percentage. A stalled download must time out with retry and source-switch guidance. Verification and installation are separate stages and must not remain labelled as downloading.

## Shared operation and safety rules

- Mutations are single-flight per component; duplicate actions are disabled.
- Stale state may remain visible during refresh but must be marked as refreshing.
- Unknown, unchecked, blocked, unavailable, unhealthy, and healthy are distinct states.
- Long operations support cancellation when safe and always have a hard timeout.
- Administrative actions remain visible and use standard UAC; there is no silent elevation.
- Configuration changes are atomic, backed up, validated, and rolled back on failure.
- User-facing errors state what failed and the next recovery action.
- Diagnostics and logs remove credentials, authorization headers, cookies, tokens, and sensitive query parameters. Default exports exclude saved provider keys and the credential-bearing file.

## Documentation synchronization

When these requirements change, update all of the following in the same change:

- `PRODUCT-REQUIREMENTS.md`;
- `PRODUCT.md`;
- `DESIGN.md`;
- `UX-CONTRACT.md`;
- `README.md` and `README.zh-CN.md`;
- `docs/index.html` and its Chinese/English translation strings;
- `SECURITY.md` when credential, installer, update, logging, or diagnostic behavior changes.

Documentation must distinguish implemented behavior from planned vNext behavior. A requirement must not appear under an `Implemented` heading until the corresponding code and verification exist.
