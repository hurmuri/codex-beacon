# Security policy

## Supported versions

Security fixes are applied to the latest revision on the default branch until the project begins publishing tagged releases.

## Reporting a vulnerability

Do not open a public issue for a vulnerability that could expose credentials, execute unintended commands, or terminate unrelated processes. Use GitHub's private vulnerability reporting feature on this repository. Include reproduction steps, affected versions, and the smallest useful diagnostic sample with secrets removed.

## Security model

Codex Beacon runs local diagnostics and can invoke installers, scheduled tasks, and service controls. It does not require elevation for read-only checks and never attempts to bypass UAC. External installation, update, and login flows use the vendor's official executable or distribution channel. Secrets are neither read nor persisted.

