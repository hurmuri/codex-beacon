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

## Verification & Release Rules

Before pushing to GitHub or publishing a release, complete local packaging and execution checks:

1. Build and package locally:
   ```powershell
   dotnet publish .\CodexBeacon.csproj -c Release -r win-x64 --self-contained true -p:WindowsAppSDKSelfContained=true -o .\publish\portable-win-x64
   ```
2. Run diagnostics:
   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Scripts\Collect-CodexStatus.ps1 -IncludeLatest
   ```
3. Local launch test:
   Launch `.\publish\portable-win-x64\CodexBeacon.exe` on the local desktop. Confirm the process starts, the window renders without crashing, and no exceptions exist in `%LOCALAPPDATA%\CodexBeacon\crash.log`. Never push or release without a verified local run.
