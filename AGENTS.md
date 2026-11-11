# AGENTS.md - Repo agent instructions

Purpose
- Brief, actionable guidance for AI coding agents working in this repository.

What to run (quick)
- Backend (C#/.NET): change to `Be/` and use `dotnet build` and `dotnet test`.
- Frontend (app): change to `app/` and use `flutter pub get` then `flutter run`.

Key locations
- Backend project root: Be/
- Backend tests: Be/Tests/ (when added)
- Frontend app: app/

Conventions and notes for agents
- Preserve existing documentation: prefer linking to README or other docs rather than copying large sections.
- Tests: run `dotnet test` under `Be/Tests` to validate backend changes; run `flutter test` for app changes.
- Formatting: follow existing repository style; avoid large sweeping refactors without explicit tests and CI confirmation.

Common pitfalls
- Windows/docker: on Windows, Docker Compose may require elevated permissions; check environment if `docker compose up` fails.

What agents should do first
1. Run the unit tests (`dotnet test`) for backend changes.
2. Run `flutter test` for app changes.

When to ask the user
- If you need credentials, secrets, or service account tokens - stop and ask; do not attempt to fetch secrets.
- If a change touches database schema or EF migrations, confirm migration strategy and whether to update production migrations.

Suggested follow-ups
- Create small skill files for running: `dev:backend`, `dev:frontend`, and `test:backend` automation.

History
- Created by AI agent to help future code-assist tasks.
