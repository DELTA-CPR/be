# AGENTS.md

<!--
Adapted for a stock `dotnet new webapi` backend. HTML comments name the source of each
section and can be deleted. Wording is original; only the ideas are taken from sources.

Structure follows the .NET team's own guidance on lean instruction files. The community C#/REST
instruction files are written as tutorial prompts ("guide the user..."), so only their concrete
rules are reused here.

Source keys (all read 2026-11-11):
  [HYG]  devblogs.microsoft.com/dotnet/instructions-hygiene-what-frontier-models-still-need-you-to-say/   (.NET Blog, 2026-08-12)
  [CS]   github/awesome-copilot   instructions/csharp.instructions.md
  [REST] github/awesome-copilot   instructions/aspnet-rest-apis.instructions.md
  [SKILLS] dotnet/skills          README.md and plugins/dotnet-test
  [SPEC] agentsmd/agents.md       format guidance
  Anything tagged "unsourced" is my own addition (standard CLI / template facts).
  Commands were NOT executed while writing this; run each once before trusting it ([HYG] warns against unvalidated commands).
-->

## Project context
<!-- src: [HYG] "non-obvious facts" and "source of truth" sections; [SPEC] "project overview". Template layout: unsourced. -->
- ASP.NET Core Web API created with `dotnet new webapi`. Endpoint wiring and middleware live in `Program.cs`; settings in `appsettings.json` and `appsettings.Development.json`.
- Target framework: read the `.csproj` (or `global.json` if the repo has one). Do not change it unless asked.

## Commands
<!-- src: restore / build --no-restore / test / format --verify-no-changes: [HYG]; container publish: [CS] "Deployment and DevOps".
     Unsourced: `dotnet run`. -->
- `dotnet restore`
- `dotnet build --no-restore`
- `dotnet run` (add `--project <path>` when run from a solution root)
- `dotnet test` (once a test project exists)
- `dotnet format --verify-no-changes` before finishing; `.editorconfig` and analyzers are the style authority, don't override them
- Container image: `dotnet publish --os linux --arch x64 -p:PublishProfile=DefaultContainer`

## Conventions
<!-- src: C# rules: [CS] "C# Instructions", "Naming Conventions", "Formatting", "Nullable Reference Types".
     Endpoint-style and dependency rules: [HYG] "Choices the codebase cannot settle". The stock template defaulting to Minimal APIs: unsourced. -->
- Match the existing endpoint style. The stock template uses Minimal APIs; don't add controllers alongside unless asked.
- C#: file-scoped namespaces, pattern matching and switch expressions, `nameof` instead of string literals, `is null` / `is not null`.
- Nullable reference types are on: trust the annotations, check for null only at entry points.
- Naming: PascalCase for types, methods and public members; camelCase for private fields and locals; `I` prefix on interfaces.
- XML doc comments on public APIs.
- Use existing abstractions; ask before adding a NuGet package.

## API behavior
<!-- src: ProblemDetails (RFC 9457), global exception handling, OpenAPI docs, versioning: [REST] "Validation and Error Handling",
     "API Versioning and Documentation". Status-code mapping: [HYG] example. Validation-library narrowing: mine ([REST] lists data annotations and FluentValidation). -->
- Return errors as RFC 9457 `ProblemDetails`; handle unexpected exceptions in one global handler, not per endpoint.
- Map validation failures to 400, missing resources to 404, conflicts to 409.
- Validate request models with built-in data annotations; ask before adding FluentValidation.
- Keep OpenAPI metadata (parameters, responses) current for every endpoint you add or change.
- Don't break public contracts; version deliberately.

## Testing
<!-- src: test-project wiring and framework list: [SKILLS] plugins/dotnet-test; style rules: [CS] "Testing". -->
- `dotnet new webapi` creates no test project. Before adding one, ask which framework to use (MSTest, xUnit, NUnit or TUnit); the dotnet-test plugin in dotnet/skills has a scaffold skill for the wiring.
- Cover critical paths; copy naming and casing from nearby tests; no Arrange/Act/Assert comments.

## Hard constraints
<!-- src: [HYG] "Hard constraints" examples, adopted as sensible defaults; adjust to your project. EF Core/migrations context: [REST] "Data Access Patterns".
     user-secrets line: unsourced. -->
- Never log secrets, tokens or customer payloads. Keep local secrets in `dotnet user-secrets`, not in `appsettings*.json`.
- Preserve public JSON contracts unless the task explicitly calls for a breaking change.
- If EF Core is added later: migrations must stay compatible with the previously deployed version.
