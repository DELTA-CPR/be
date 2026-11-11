# AGENTS.md — Repo agent instructions

Purpose
- Brief, actionable guidance for AI coding agents working in this repository.

What to run (quick)
- Backend (C#/.NET): from repository root run `dotnet build` and `dotnet test` (if tests exist).
- Run dev server: `dotnet run --project DeltaCprBe`.

Key locations
- Backend project root: DeltaCprBe/
- Solution: DeltaCprBe.slnx
- Controllers: DeltaCprBe/Controllers/
- Configuration: DeltaCprBe/appsettings.json

Conventions and notes for agents
- Target framework: net10.0 (latest .NET SDK installed).
- Preserve existing documentation: prefer linking to README or other docs rather than copying large sections.
- Formatting: follow existing repository style; avoid large sweeping refactors without explicit tests and CI confirmation.

Common pitfalls
- Remove bin/ and obj/ before committing (covered by .gitignore).

What agents should do first
1. Run `dotnet build` to validate backend changes.
2. Run the app with `dotnet run --project DeltaCprBe` for manual checks.

When to ask the user
- If you need credentials, secrets, or service account tokens — stop and ask; do not attempt to fetch secrets.

History
- Created by AI agent to help future code-assist tasks.
