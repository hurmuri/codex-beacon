# Repository instructions

## Scope

Codex Beacon is a Windows-only WinUI 3 application. Keep platform integration in `SystemService.cs` and the PowerShell scripts, view state in `Models.cs`, and UI behavior in `MainWindow.xaml.cs`.

## UI

- Prefer built-in WinUI 3 controls and Windows Community Toolkit Settings controls.
- Follow Windows 11 spacing, typography, focus visuals, keyboard behavior, and the light theme defined in `DESIGN.md`.
- Do not introduce web-style dashboard cards, decorative gradients, custom-drawn controls, or a large topology illustration.
- Service actions must be state-driven: install before login, login before use, start only when stopped, and upgrade only when a newer version is known.
- Preserve the three-column service layout at desktop widths and keep the signal path compact.

### Core UI Architecture & Standards (基础 UI 规范)

- The unified layout introduced in this refactoring is the canonical baseline UI specification for all current and future screens in Codex Beacon.
- All pages must strictly share identical outer container constraints:
  ```xaml
  <ScrollViewer x:Name="XXXPage" Visibility="Collapsed"
                HorizontalScrollBarVisibility="Disabled"
                VerticalScrollBarVisibility="Auto"
                Padding="28,20,28,36">
      <StackPanel MaxWidth="1280" HorizontalAlignment="Stretch">
          <!-- Content fills the 1280px layout width responsively -->
      </StackPanel>
  </ScrollViewer>
  ```
- Horizontal overflow or clipping is forbidden; horizontal scrolling must always be disabled (`HorizontalScrollBarVisibility="Disabled"`).
- Never hardcode narrow pixel widths (e.g. `Width="400"` or `Width="1000"`) on top-level section cards, expanders, or grid columns that leave half the screen blank on high-resolution or maximized windows.
- Follow the visual benchmark established by "Dependencies" and "Overview": utilize responsive multi-column grids (`Grid` with `ColumnSpacing="14"` or `18` and `*` proportions) so cards stretch and balance symmetrically across the 1280px canvas.
- Component cards must remain clean, compact, and uncluttered: avoid verbose developer stdout logs or diagnostic explanations inside cards. Present only title, role subtitle, status badge (Healthy/Stopped/Unavailable), version line, and state-driven action buttons.

## Diagnostics and safety

- PowerShell scripts must remain compatible with Windows PowerShell 5.1 and be saved as UTF-8 with BOM.
- Provider API keys may be saved in the current user's configuration directory when explicitly entered by the user. Key inputs must use a password control and remain hidden by default. Keys must never be uploaded, logged, included in diagnostics, command-line arguments, error messages, or user-visible status text. Configuration files containing keys must be restricted to the current user.
- A proxy path is valid only when supported by the selected provider, its configured endpoint, listener ownership, and observed TCP connections. Configured-but-inactive endpoints belong in the candidate list.
- Distinguish the machine's public egress IP from remote IPs contacted by Codex-related processes.
- Process termination must use an explicit allowlist and must exclude Codex Beacon, the Codex desktop app, and unrelated Node.js processes.
- External installers and login flows must use their official channel and remain visible to the user.

## Verification & Release Rules

Every code modification must go through a complete inspection, single-file build, changelog update, and GitHub synchronization cycle:

1. Code Inspection & Verification (修改自检):
   - Code must build cleanly with 0 warnings and 0 errors: `dotnet build .\CodexBeacon.csproj -c Debug`.
   - PowerShell scripts must parse cleanly without syntax errors and remain UTF-8 with BOM.
   - Run diagnostics to confirm healthy JSON payload:
     ```powershell
     powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Scripts\Collect-CodexStatus.ps1
     ```
   - Verify runtime execution on desktop: start the application, confirm window renders without crash, and verify `%LOCALAPPDATA%\CodexBeacon\crash.log` does not exist or has no errors.

2. Single-File Packaging with Versioning (独立单文件打包):
   - The application must always be packaged as a **single, standalone executable** (`.exe`).
   - **Never produce or distribute `.zip` archive packages.**
   - Maintain dual single-file release variants (双打包规范：含运行时与不含运行时):
     1. **Portable (含运行时完整版)**: Self-contained with bundled .NET runtime and Windows App SDK payload. Runs out-of-the-box on any target machine without external runtime prerequisites. Outputs as `publish\CodexBeacon-<version>.exe` (and convenient sibling `publish\CodexBeacon.exe`).
     2. **Slim (不含运行时精简版)**: Framework-dependent payload relying on the machine's installed .NET runtime, producing a much smaller standalone executable. Outputs as `publish\CodexBeacon-<version>-slim.exe`.
   - Both variants must be built as single, standalone `.exe` executables without companion ZIPs or loose dependency folders.
   - Run the automated single-file dual build:
     ```powershell
     powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Scripts\Build-SingleFile.ps1 -Target all
     ```
   - The standalone executables are generated directly at:
     - `publish\CodexBeacon-<version>.exe` (Portable)
     - `publish\CodexBeacon-<version>-slim.exe` (Slim)

3. Changelog & GitHub Workflow (日志生成与代码推送):
   - Update `CHANGELOG.md` with all notable additions, changes, and fixes under the current release header.
   - Prepare release notes detailing user-facing improvements and technical changes.
   - Commit all modified and tracked files with clear, structured commit messages.
   - Push the committed changes to GitHub to maintain remote synchronization.
