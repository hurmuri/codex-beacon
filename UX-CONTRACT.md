# UX Contract

## Canonical UI Map

| Capability | Canonical owner | Source of truth | Allowed variants | Verification |
|---|---|---|---|---|
| Application navigation | WinUI `NavigationView` | Selected navigation tag | Compact or expanded pane | Navigate to all seven pages with mouse and keyboard |
| Service summary | Toolkit `SettingsCard` | `ComponentStatus` snapshot | Installed, signed out, stopped, running, degraded, update available | Compare enabled commands with the collected state |
| Grouped settings | Toolkit `SettingsExpander` | `AppSettings` and runtime snapshot | Expanded or collapsed | Reload saved values and inspect focus order |
| Process and network inventories | WinUI `ListView` | PowerShell collector JSON | Empty, populated, collection failure | Verify row data and explicit empty/failure message |
| Persistent feedback | Window status bar | Current refresh or action result | Busy, success, failure | Trigger refresh and a safe state action |
| Toast | Authored window status bar | Current refresh or action result | Busy, success, failure; no transient popup | Trigger refresh and verify status announcement |
| Context notice | WinUI `InfoBar` | Active route and source policy | Information or warning | Compare text with proxy-mode and version-source evidence |
| Destructive confirmation | WinUI `ContentDialog` | Requested bulk action | Stop or restart | Cancel once, then verify named exclusions |
| Service commands | WinUI `Button` inside `SettingsCard` | State-derived action properties | Install, login, start, stop, restart, upgrade | Confirm invalid transitions remain disabled |
| Version selection | WinUI `ComboBox` | NVM installed-version list | Current or another installed version | Switch versions and recollect Node/npm state |
| Download progress | WinUI `ProgressBar` plus text | Native downloader/tool output | Determinate or indeterminate | Verify stage, bytes, percentage when known, speed, elapsed time, cancellation, and timeout behavior |
| Repository links | WinUI `HyperlinkButton` | Hard-coded verified upstream URLs | Upstream or implementation reference | Open each link and compare repository owner/name |
| Application log | WinUI `LogWindow` with a read-only `TextBox` | `AppLog` append-only file | Open, copy all, open log folder | Trigger an action and confirm the new line appears in the window |
| Background presence | WinUI tray icon | Single running instance | Tray only, or tray plus window | Close the window and confirm the process and tray icon survive |

## Navigation

`NavigationView` is the canonical application shell. The order is Codex overview, Dependencies, Codex processes, Network, Tailscale, and Settings / About & updates. Views keep their state during navigation; Network never collapses into the overview.

## Status and feedback

The page-owned status bar is the canonical live status surface. Routine refresh and action completion use polite text updates; failures persist until the next successful action or explicit retry. Raw command output and secrets never appear.

## Lists

Native `ListView` is canonical for processes, Windows prerequisite components, and Tailnet devices. Connectivity probes and public egress routes use `ItemsControl` with an explicit empty state. Empty states are explicit and distinguish no matching data from collection failure.

## Confirmation

Native `ContentDialog` is canonical for destructive or broad-impact actions. Safe start/stop/restart actions execute directly; all-service restart and termination require confirmation. The dialog stays open until the user commits or cancels.

## Window lifetime

Closing the main window hides it to the system tray instead of terminating the process. The tray icon exposes Open and Exit. Exit is the only action that ends the process. The `--background` launch flag starts the process into the tray without ever showing the window, and the Windows startup setting registers that same flag.

## Async actions

Refresh and mutations are single-flight. Stale snapshot data remains visible during refresh. Buttons expose a busy state and duplicate actions are blocked. After mutation, the app collects an authoritative snapshot before claiming success.

Every operation that downloads bytes exposes progress. Determinate downloads show bytes, total, percentage, speed, and elapsed time; indeterminate downloads do not invent a percentage. Resolving, downloading, verifying, installing, and finalizing are separate user-visible stages. Safe cancellation, hard timeout, retry, and source guidance remain available.

## Settings

Native `NumberBox` and `TextBox` controls own ordinary settings entry. Settings that contain credentials are out of scope: Codex Beacon stores no provider keys and no other secret, so no password-style input, hidden secret file, or per-user credential store is required.

The existing About and Codex Beacon update controls remain under Settings / About & updates. Component-specific controls live on their owning pages: mirror and Registry settings under Dependencies, proxy settings under Network, and Tailscale options under Tailscale.

## Destructive scope

Bulk stop targets only command lines explicitly associated with Codex CLI service processes. It excludes Codex Beacon, the Windows Codex desktop application, unrelated Node processes, and user documents.
