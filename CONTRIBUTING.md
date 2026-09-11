# Contributing

Thanks for helping improve Codex Beacon.

## Development environment

- Windows 10 version 1809 or newer
- .NET 10 SDK
- Visual Studio 2026 or a current editor with C# support
- Windows App SDK prerequisites

Fork the repository, create a focused branch, and build the project before opening a pull request:

```powershell
dotnet restore .\CodexBeacon.csproj
dotnet build .\CodexBeacon.csproj -c Release
```

## Change expectations

- Keep pull requests narrowly scoped and explain user-visible behavior.
- Include screenshots for XAML changes.
- Preserve Windows PowerShell 5.1 compatibility and UTF-8 BOM for scripts.
- Add or update state detection before exposing a new action in the UI.
- Do not add telemetry. Provider credentials may be collected only through the approved password input and current-user configuration flow.
- Document every new external version source and label whether it is official or third-party.
- Route every byte-download operation through the shared progress contract: stage, bytes, total and percentage when known, speed, elapsed time, safe cancellation, and timeout.
- Keep Provider raw-key input hidden by default. Persist it only in `%USERPROFILE%\.CodexBeacon\providers.json`, explicitly mark `.CodexBeacon` with the Windows `Hidden` attribute, restrict the file to the current user, and keep it out of uploads, command lines, logs, errors, diagnostics, status text, crash reports, and default exports.
- Keep component-specific task names and settings on the corresponding component page, and preserve the existing About and Codex Beacon update surface.
- Update the canonical requirements and every affected product, design, README, website, UX, and security document in the same change.

Please read `AGENTS.md`, `PRODUCT-REQUIREMENTS.md`, `DESIGN.md`, and `SECURITY.md` before changing diagnostics, downloads, credentials, or process-control logic.
