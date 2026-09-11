# Security policy

## Supported versions

Security fixes are applied to the latest revision on the default branch until the project begins publishing tagged releases.

## Reporting a vulnerability

Do not open a public issue for a vulnerability that could expose credentials, execute unintended commands, or terminate unrelated processes. Use GitHub's private vulnerability reporting feature on this repository. Include reproduction steps, affected versions, and the smallest useful diagnostic sample with secrets removed.

## Security model

Codex Beacon runs local diagnostics and can invoke installers, scheduled tasks, and service controls. It does not require elevation for read-only checks and never attempts to bypass UAC. External installation, update, and login flows use the vendor's official executable or distribution channel.

Provider setup accepts a raw key in a password-style input that is hidden by default. When the user explicitly saves a provider, Codex Beacon may store the key in plaintext under `%USERPROFILE%\.CodexBeacon\providers.json`. The `.CodexBeacon` directory must be assigned the Windows `Hidden` attribute explicitly; a leading dot does not hide a directory on Windows by itself. The file must be restricted to the current Windows user and written through an atomic replacement that preserves that access rule.

Saved keys are local configuration only. They must never be uploaded, placed on a process command line, logged, included in diagnostics or crash reports, echoed in errors or user-visible status text, or included in a default export. Provider lists reveal only whether a key is saved. Connectivity tests pass keys through standard input, protected local IPC, or another non-command-line channel and redact downstream output before display. Removing a provider or using `Clear key` removes the persisted key.

Downloads identify their source, expose progress, separate verification from installation, and never present a third-party package mirror as an official installer.
