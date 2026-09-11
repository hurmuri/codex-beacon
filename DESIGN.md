# Codex Beacon design system

## North star

Codex Beacon should feel like a focused Windows 11 Settings page for local Codex management: native, bright, compact, predictable, and evidence-led. Codex desktop and Codex CLI are the independent product core. OpenCodex Proxy and Codex Relay are optional extensions; their absence is not a system failure.

## Information architecture

- **Codex overview** — ChatGPT desktop and the active user-managed Codex CLI, including version gaps and the next valid action.
- **Dependencies** — App Installer/WinGet/Store and NVM/Node.js/npm/Registry prerequisite chains.
- **Codex processes** — Codex-related processes, executable versions, paths, ownership, and guarded recovery controls.
- **Network** — ChatGPT and CLI reachability, system/TUN/HTTP proxy state, evidence-backed active routes, and machine public egress.
- **Providers** — provider CRUD, activation, default model, protected key entry, and layered connectivity tests.
- **OpenCodex** — independent installation, service health, providers, static/live models, tests, synchronization, and Codex integration state.
- **Tailscale** — independent service, sign-in, local-node, and Tailnet inventory.
- **Codex Relay** — independent optional install, service, listener, login, and pairing state.
- **Settings / About & updates** — application-wide preferences plus the preserved Codex Beacon version, release notes, update check, download, verification, and restart surface.

This order is canonical in `NavigationView`. Network is a dedicated page. The overview is not a generic dashboard and contains only the two core products. Dense inventories remain native list rows rather than nested cards. See [PRODUCT-REQUIREMENTS.md](PRODUCT-REQUIREMENTS.md) for the functional contract.

Component settings stay with their owner: runtime mirrors and Registry under Dependencies, proxy options under Network, provider configuration under Providers, and scheduled-task/service names under OpenCodex or Codex Relay. The global Settings page does not duplicate those controls.

## Native component policy

- `NavigationView` owns primary navigation.
- `SettingsCard` and `SettingsExpander` own settings and service surfaces.
- `InfoBar` owns persistent context, warnings, and source disclosures.
- `CommandBar` and `AppBarButton` own grouped service actions.
- `ListView`, `ComboBox`, `TextBox`, `NumberBox`, `ContentDialog`, `ProgressRing`, and `ProgressBar` retain platform behavior and focus visuals.
- Provider secrets use a password-style control, hidden by default, with an explicitly labelled reveal interaction. A saved key may repopulate the masked control, while lists show only `Key saved`. The raw value never appears in status text, logs, diagnostics, errors, or command lines.
- Custom borders are limited to status pills and table containment. Do not build a parallel card component library.

## State-driven workflow

Every component follows the applicable parts of this sequence:

```text
Not installed → Install → Installed / signed out → Sign in
→ Ready / stopped → Start → Running → Update available → Upgrade
```

Actions stay disabled until prerequisites are proven. Detection distinguishes package installation, service registration, process/listener state, account state, and version availability. Unknown is not healthy. After a mutation, the app refreshes authoritative state.

- npm packages require Node.js ≥ 22.14.0 and a working npm executable.
- Tailscale requires the Windows package, service, and a signed-in Tailnet state.
- Codex CLI login is verified by its status command; login opens a visible official CLI flow.
- OpenCodex Proxy and Codex Relay may be installed without a configured scheduled task.
- Codex desktop installation and update always open the official Microsoft Store product page.

## Optional-module routing

The compact route strip only renders the active route supported by evidence:

```text
Codex client → selected model_provider → active base_url owner → observed upstream
```

OpenCodex Proxy or Codex Relay enters this route only when the selected provider and listener evidence prove that it is active. Installing an extension alone never changes the route. Configured-but-inactive endpoints are listed separately. The machine public egress address is never conflated with remote service IPs.

## Self-update

Codex Beacon's own update flow is a separate track from the components it manages:

```text
Up to date → Newer release found → Prompted (banner) → Download → Verified
→ Replace install package → Restart into new version
```

- Version checks read the official GitHub Releases feed for this repository. No third-party mirror or aggregator is consulted.
- A newer release raises a window-top `InfoBar`; the Settings page owns the durable surface with the current version, package type, release notes, and manual controls.
- Download progress, transfer speed, and the SHA-256 result are always shown as text, never colour alone.
- The update never installs silently: the window restarts only after the user confirms, and the release page stays one click away.
- A build that was not started from a single-file package cannot be replaced in place; the surface states that plainly and points at the release page instead of pretending to install.

## Download progress

Every download, not only Codex Beacon self-update, uses the same native progress vocabulary: resolving, connecting, downloading, verifying, installing, and finalizing. When the source provides a total, show transferred/total bytes, percentage, speed, elapsed time, and a reliable ETA. When the total is unknown, use an indeterminate progress bar and never fabricate a percentage. Keep safe cancellation and retry visible, and identify the official source or labelled mirror.

## Visual language

- Forced light theme for the initial release.
- Segoe UI Variable; Cascadia Mono only for versions, ports, PIDs, endpoints, and paths.
- System theme resources first. Green, amber, and red supplement explicit status text.
- 24–30 px page gutter, 24 px section rhythm, 8–12 px control gaps.
- Inherit WinUI shape and elevation; no decorative gradients or heavy shadows.
- Native progress indicators only, respecting reduced-motion settings.

## Content and accessibility

Use concise Simplified Chinese labels. Errors state what failed and the next recovery action. Status always has text, not color alone. Controls retain native keyboard navigation and focus indicators. Destructive bulk actions require a confirmation dialog naming affected and excluded processes.

## Source disclosure

Codex desktop's installed version comes from local `OpenAI.Codex` package metadata. Its latest downloadable Windows package currently comes from the third-party Codex App Mirror manifest, which reports synchronization with Microsoft Store product `9PLM9XGG6VKS`. UI and documentation must label this source accurately; installation and update still use the official Store page.
