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
- Do not add telemetry or collect credentials.
- Document every new external version source and label whether it is official or third-party.

Please read `AGENTS.md`, `DESIGN.md`, and `SECURITY.md` before changing diagnostics or process-control logic.

