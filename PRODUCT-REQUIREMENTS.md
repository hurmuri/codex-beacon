# Codex Beacon vNext product requirements

Status: approved product direction; implementation may lag behind this document.

Last updated: 2026-09-11.

This document is the canonical description of the vNext information architecture, functional boundaries, state model, download experience, and credential handling. `PRODUCT.md`, `DESIGN.md`, `UX-CONTRACT.md`, the README files, and the project website must remain consistent with it.

## Product boundary

Codex Beacon is a Windows-only native manager for the ChatGPT desktop package, the user-managed Codex CLI, their prerequisites and network path, and optional Tailscale integration.

- ChatGPT desktop and the user-managed Codex CLI are the product core.
- Tailscale remains a separate module. A missing optional module does not make the core unhealthy.
- Configured state is not proof of active state. Runtime health requires the relevant process, listener, route, or observed connection evidence.

## Navigation

The canonical navigation order is:

1. **Codex overview**
2. **Dependencies**
3. **Codex processes**
4. **Network**
5. **Tailscale**
6. **Settings / About & updates**

The desktop layout uses `NavigationView`. Pages preserve state while navigating. Network management is a dedicated page, not an overview subsection.

## Codex overview

The overview is intentionally limited to the two core products.

### ChatGPT desktop

Show:

- Microsoft Store/AppX package installation state;
- running and sign-in state when evidence is available;
- installed version, latest known version, source, and last check time;
- `Check for updates`, `Start`, `Stop`, `Restart`, and `Store install/upgrade` actions enabled by state;
- a `Manage dependencies` shortcut to Dependencies, plus a store-channel status line that reports whether App Installer, WinGet, and the Store source are available.

The primary Store product is `9PLM9XGG6VKS`, whose installed package identity is currently `OpenAI.Codex`. Store IDs must be passed to WinGet as unique positional IDs rather than combined with exact-match filtering.

### Codex CLI

Show:

- the `codex` executable currently resolved for a normal user shell;
- its version, absolute path, and installation owner;
- the npm-managed `@openai/codex` version and latest registry version;
- installation, sign-in, update, and upgrade actions;
- a signed-in, signed-out, or unverifiable account state, with the login action disabled after a verified sign-in.

The install source is reported explicitly, because several copies can coexist on one machine: a global npm install, a `codex` command on `PATH`, or the copy bundled with the desktop app. The headline version belongs to the executable resolved for the user; the other sources are not allowed to silently replace it.

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
- configure a Node.js distribution mirror, with a visible fallback to the official host when the mirror fails;
- configure the npm Registry independently from the Node.js mirror, and apply it through `--registry` when installing or upgrading npm packages;
- show the current Node.js mirror and npm Registry in use, and the source of every version lookup.

The Node.js version selector merges the versions NVM already has installed with the versions the selected mirror offers. Choosing an installed version switches the active runtime; choosing a missing one installs it through the mirror and then switches to it. The npm Registry is passed to npm as `--registry` for Codex CLI installs and upgrades. Saving the dependency settings also writes the same value to the system-wide `npm config`, so both paths resolve to the same registry.

There is no general consumer offline-package fallback for ChatGPT. Store package download can require Microsoft Entra administrator authorization. The UI must not offer an unofficial mirror as an official installer.

## Codex processes

Preserve the existing process inventory and guarded recovery behavior. Continue to show role, PID, parent PID, executable version, architecture, path, and start time. Process termination uses the explicit allowlist and excludes Codex Beacon, ChatGPT desktop, and unrelated Node.js processes.

## Network

The Network page owns transport state: proxy mode, reachability, and public egress.

### System transport

- DNS, TCP, and TLS reachability;
- WinINET proxy state, including the manual proxy address, automatic detection, and PAC URL;
- supported environment-variable proxies;
- TUN adapter detection;
- configured custom HTTP proxy endpoint;
- machine public egress IP, egress provider, geography, and check time.

### Client checks

ChatGPT desktop and Codex CLI have separate checks. Each result carries the HTTP status code when the endpoint answered, or the transport-level failure reason when it did not, so the following situations stay distinguishable:

- internet unavailable;
- endpoint unreachable;
- endpoint reachable but authentication rejected;
- model or route not found;
- rate limited;
- name resolution failure, including a proxy name that cannot be resolved;
- connection failure, including a refused or unreachable proxy;
- timeout or TLS failure.

The effective route is reported from evidence: the configured proxy mode is compared against the Windows system proxy state, the automatic/PAC configuration, the detected TUN adapter, and the user environment proxy. Public egress is never presented as a remote service IP.

## Tailscale

Keep Tailscale independent. Preserve package, Windows service, sign-in, local node, Tailnet device, address, online, and last-seen state. Tailscale absence does not degrade ChatGPT or Codex CLI health.

## Settings / About & updates

Preserve the existing About and application-update experience, including the running Codex Beacon version, package type, release notes, manual update check, update availability, skipped version, download, verification, replacement eligibility, and restart flow.

This page owns only application-wide preferences and Codex Beacon itself: display language, refresh interval, Windows startup, update channel/state, and About information. Component-specific configuration must live with the component it controls:

- Node.js mirror and npm Registry -> Dependencies;
- system, TUN, and custom HTTP proxy options -> Network;
- Tailscale service options -> Tailscale;

The Settings page must not become a second inventory of component controls.

## Application log

All action output, npm and installer progress, launcher messages, and application errors are written to one append-only log file in the current user's local application data directory. The application exposes that file through a dedicated log window with a read-only text view, a copy-all action, and an open-folder action. Secrets are redacted before a line is written, and the log never contains API keys, authorization headers, cookies, or tokens.

## Window lifetime and startup

Closing the main window hides it to the system tray; the process keeps running and the tray icon remains until Exit is chosen. The Windows startup setting registers Codex Beacon with the `--background` flag, so it starts into the tray after sign-in without showing a window. Only one Codex Beacon instance runs at a time: starting it again replaces the previous instance.

## Download experience

Every operation that downloads bytes must expose a consistent, non-blocking progress surface. This includes application self-update, ChatGPT/App Installer acquisition, NVM, Node.js, npm packages, Tailscale, and any downloaded metadata large enough to create a visible wait.

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
- Configuration changes are written to the current user's local settings file and validated before use. A failed write leaves the previous file untouched.
- User-facing errors state what failed and the next recovery action.
- Diagnostics and logs remove credentials, authorization headers, cookies, tokens, and sensitive query parameters. Default exports exclude any credential-bearing configuration.

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
