# Repository instructions

## Scope

Codex Beacon is a Windows-only WinUI 3 application. Keep platform integration in `SystemService.cs` and the PowerShell scripts, view state in `Models.cs`, and UI behavior in `MainWindow.xaml.cs`.

## UI

- Prefer built-in WinUI 3 controls and Windows Community Toolkit Settings controls.
- Follow Windows 11 spacing, typography, focus visuals, keyboard behavior, and the light theme defined in `DESIGN.md`.
- Do not introduce web-style dashboard cards, decorative gradients, custom-drawn controls, or a large topology illustration.
- Service actions must be state-driven: install before login, login before use, start only when stopped, and upgrade only when a newer version is known.
- Preserve the three-column service layout at desktop widths and keep the signal path compact.

## Diagnostics and safety

- PowerShell scripts must remain compatible with Windows PowerShell 5.1 and be saved as UTF-8 with BOM.
- Never collect, log, serialize, or display tokens, passwords, authorization headers, cookies, or API keys.
- A proxy path is valid only when supported by the selected provider, its configured endpoint, listener ownership, and observed TCP connections. Configured-but-inactive endpoints belong in the candidate list.
- Distinguish the machine's public egress IP from remote IPs contacted by Codex-related processes.
- Process termination must use an explicit allowlist and must exclude Codex Beacon, the Codex desktop app, and unrelated Node.js processes.
- External installers and login flows must use their official channel and remain visible to the user.

## Verification

Run before proposing a change:

```powershell
dotnet build .\CodexBeacon.csproj -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Scripts\Collect-CodexStatus.ps1 -IncludeLatest
```

For UI changes, launch the application and verify every navigation page at the default 1320 × 860 window size.

