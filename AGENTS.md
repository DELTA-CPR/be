# AGENTS.md — Repo agent instructions

Purpose
- Brief, actionable guidance for AI coding agents working in this repository.
- Backend is an ASP.NET Core Web API on .NET 10.0. Frontend lives in the sibling `app/` repository (Flutter).

What to run (quick)
- Build: `dotnet build`
- Run dev server: `dotnet run --project Be`
- Test (when tests exist): `dotnet test`
- Format: `dotnet format`
- Create migration (EF Core, if used): `dotnet ef migrations add <Name> --project Be`

Key locations
- Solution: `Be.slnx`
- Project root: `Be/`
- Entry point: `Be/Program.cs`
- Controllers: `Be/Controllers/`
- Configuration: `Be/appsettings.json`, `Be/appsettings.Development.json`
- HTTP test file: `Be/Be.http`
- Launch settings: `Be/Properties/launchSettings.json`

Conventions and notes for agents
- Target framework: net10.0 (latest .NET SDK installed). Keep `<TargetFramework>` current.
- Use implicit usings and nullable reference types (already enabled).
- Preserve existing documentation: prefer linking to README or other docs rather than copying large sections.
- Follow REST conventions; keep controllers thin and move logic to services.
- Add XML/JSON contract changes carefully; update clients accordingly.
- Do not commit `bin/` or `obj/` (covered by `.gitignore`).

Common pitfalls
- Run `dotnet restore` if package references change.
- HTTPS dev certificate: run `dotnet dev-certs https --trust` if requests fail locally.
- Port conflicts: check `launchSettings.json`.
- EF Core migrations require `dotnet-ef` tool installed.

What agents should do first
1. Run `dotnet build` to validate changes.
2. Run `dotnet run --project Be` for manual checks.
3. Run `dotnet test` when tests exist.

When to ask the user
- If you need credentials, secrets, connection strings, or service account tokens — stop and ask; do not fetch secrets.
- If a change touches database schema or migrations, confirm strategy.

Suggested follow-ups
- Add a test project (`dotnet new xunit`) under `Tests/`.
- Add Swagger/OpenAPI client generation for the Flutter app.
- Add Dockerfile and compose for local dev.

History
- Created by AI agent to help future code-assist tasks.
