# Codex Beacon design system

## North star

Codex Beacon should feel like a focused Windows 11 Settings page for local Codex management: native, bright, compact, predictable, and evidence-led. Codex desktop and Codex CLI are the independent product core. OpenCodex Proxy and Codex Relay are optional extensions; their absence is not a system failure.

## Information architecture

- **Overview** — current route, service readiness, version gaps, and Tailnet summary.
- **Processes** — Codex-related processes, executable versions, paths, and guarded recovery controls.
- **Network & proxy** — active provider route, machine public egress, observed remote service IPs, inactive configured endpoints, and Tailnet devices.
- **Installation** — Node.js/npm prerequisites, NVM version switching, core clients, and optional modules.
- **Settings** — refresh interval and local scheduled-task mappings.

The overview uses three service cards per row at the default desktop width. Dense inventories remain native list rows rather than nested cards.

## Native component policy

- `NavigationView` owns primary navigation.
- `SettingsCard` and `SettingsExpander` own settings and service surfaces.
- `InfoBar` owns persistent context, warnings, and source disclosures.
- `CommandBar` and `AppBarButton` own grouped service actions.
- `ListView`, `ComboBox`, `TextBox`, `NumberBox`, `ContentDialog`, `ProgressRing`, and `ProgressBar` retain platform behavior and focus visuals.
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
