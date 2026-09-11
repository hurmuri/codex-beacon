# UX Contract

## Canonical UI Map

| Capability | Canonical owner | Source of truth | Allowed variants | Verification |
|---|---|---|---|---|
| Application navigation | WinUI `NavigationView` | Selected navigation tag | Compact or expanded pane | Navigate to all five pages with mouse and keyboard |
| Service summary | Toolkit `SettingsCard` | `ComponentStatus` snapshot | Installed, signed out, stopped, running, degraded, update available | Compare enabled commands with the collected state |
| Grouped settings | Toolkit `SettingsExpander` | `AppSettings` and runtime snapshot | Expanded or collapsed | Reload saved values and inspect focus order |
| Process and network inventories | WinUI `ListView` | PowerShell collector JSON | Empty, populated, collection failure | Verify row data and explicit empty/failure message |
| Persistent feedback | Window status bar | Current refresh or action result | Busy, success, failure | Trigger refresh and a safe state action |
| Toast | Authored window status bar | Current refresh or action result | Busy, success, failure; no transient popup in v0.1 | Trigger refresh and verify status announcement |
| Context notice | WinUI `InfoBar` | Active route and source policy | Information or warning | Compare text with provider and version-source evidence |
| Destructive confirmation | WinUI `ContentDialog` | Requested bulk action | Stop or restart | Cancel once, then verify named exclusions |
| Service commands | WinUI `CommandBar` | State-derived action properties | Install, login, start, stop, restart, upgrade | Confirm invalid transitions remain disabled |
| Version selection | WinUI `ComboBox` | NVM installed-version list | Current or another installed version | Switch versions and recollect Node/npm state |
| Repository links | WinUI `HyperlinkButton` | Hard-coded verified upstream URLs | Upstream or implementation reference | Open each link and compare repository owner/name |

## Navigation

`NavigationView` is the canonical application shell. Dashboard, process, network, installation, and settings views keep their state during navigation.

## Status and feedback

The page-owned status bar is the canonical live status surface. Routine refresh and action completion use polite text updates; failures persist until the next successful action or explicit retry. Raw command output and secrets never appear.

## Lists

Native `ListView` is canonical for processes, components, proxy hops, and Tailnet devices. Empty states are explicit and distinguish no matching data from collection failure.

## Confirmation

Native `ContentDialog` is canonical for destructive or broad-impact actions. Safe start/stop/restart actions execute directly; all-service restart and termination require confirmation. The dialog stays open until the user commits or cancels.

## Async actions

Refresh and mutations are single-flight. Stale snapshot data remains visible during refresh. Buttons expose a busy state and duplicate actions are blocked. After mutation, the app collects an authoritative snapshot before claiming success.

## Settings

Native `NumberBox` and `TextBox` controls own settings entry. Values are validated before saving and stored under `%LOCALAPPDATA%\CodexBeacon`; secrets are never accepted or persisted.

## Destructive scope

Bulk stop targets only command lines or scheduled tasks explicitly associated with OpenCodex proxy, Codex Relay, or CLI service processes. It excludes Codex Beacon, the Windows Codex desktop application, unrelated Node processes, and user documents.
