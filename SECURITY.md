# Security policy

## Supported versions

Security fixes are applied to the latest revision on the default branch until the project begins publishing tagged releases.

## Reporting a vulnerability

Do not open a public issue for a vulnerability that could expose credentials, execute unintended commands, or terminate unrelated processes. Use GitHub's private vulnerability reporting feature on this repository. Include reproduction steps, affected versions, and the smallest useful diagnostic sample with secrets removed.

## Security model

Codex Beacon runs local diagnostics and can invoke installers, scheduled tasks, and service controls. It does not require elevation for read-only checks and never attempts to bypass UAC. External installation, update, and login flows use the vendor's official executable or distribution channel.

Codex Beacon stores no credentials. It has no API-key, token, or password input, keeps no secret file, and writes nothing to the Codex configuration file. The only persisted state is the current user's local application settings (refresh interval, display language, Windows startup, update preference, download mirrors, and proxy mode) plus the Codex Beacon application log.

Because no secret is ever accepted, none can be uploaded, placed on a process command line, logged, included in diagnostics or crash reports, echoed in errors or user-visible status text, or included in a default export. The application log applies a redaction pass before a line is written, so key-shaped values that appear incidentally in installer output are replaced with `<redacted>`.

Process termination uses an explicit path allowlist and always excludes Codex Beacon itself, the ChatGPT desktop app, and unrelated Node.js processes. The Windows startup setting writes a single `HKCU\...\Run` value that points at the Codex Beacon executable with the `--background` flag; it does not create a scheduled task and does not run elevated.

Downloads identify their source, expose progress, separate verification from installation, and never present a third-party package mirror as an official installer.
