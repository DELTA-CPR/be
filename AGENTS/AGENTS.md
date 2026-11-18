# Backend API - AGENTS.md

## Context & Environment
- **Path**: `be/`
- **Framework**: ASP.NET Core Web API on **.NET 10** (`net10.0`)
- **Solution File**: `Be.slnx` (Project: `Be/Be.csproj`)
- **API Style**: Controller-based (`Controllers/`, `[ApiController]`, inheriting `ControllerBase`)
- **Documentation**: ASP.NET OpenAPI (`Microsoft.AspNetCore.OpenApi`)
- **Settings**: `appsettings.json`, `appsettings.Development.json`

All commands below must be executed from the `be/` directory.

---

## Verification & Workflow Commands
```bash
# Build the solution (Mandatory verification gate before finishing tasks)
dotnet build Be.slnx

# Run automated tests (when test project exists)
dotnet test

# Format code according to .editorconfig and dotnet standards
dotnet format

# Run local development API
dotnet run --project Be/Be.csproj
```

---

## High-Impact Architecture & Coding Rules

### 1. Controller Design & REST Conventions
- **Controller-Based Architecture**: Follow the established pattern in `Be/Controllers/`. Use `[ApiController]`, inherit from `ControllerBase`, and use route attributes like `[Route("api/[controller]")]` or explicit RESTful path segments.
- **DTO Isolation**: Never bind or return database entities or internal domain models directly across controller endpoints. Always define explicit, strongly-typed Request and Response DTOs.
- **Action Return Types**: Use `ActionResult<T>` or `IActionResult`. Return semantic HTTP status codes (`Ok(result)`, `CreatedAtAction(...)`, `NoContent()`, `NotFound(...)`, `BadRequest(...)`).
- **OpenAPI Synchronization**: Keep endpoint documentation up to date using XML doc comments, `[ProducesResponseType]`, and OpenAPI attributes for all expected response codes.

### 2. Asynchronous Integrity (Zero Thread-Pool Starvation)
- **Async All the Way**: All I/O, database, and network operations must be non-blocking (`async Task<ActionResult<T>>`).
- **Cancellation Tokens**: Always accept `CancellationToken cancellationToken = default` in controller actions and forward it down to all async calls (`DbContext`, `HttpClient`, streams, etc.).
- **No Blocking Sync Calls**: Never use `.Result`, `.Wait()`, or `.GetAwaiter().GetResult()`. This causes severe thread-pool exhaustion and deadlocks under load.

### 3. Dependency Injection & Service Lifetimes
- **Constructor Injection**: Inject dependencies via primary constructors or explicit constructor parameters. Never resolve dependencies via `IServiceProvider` (Service Locator anti-pattern).
- **Lifetime Correctness**:
  - `AddScoped`: For per-request state and data contexts (e.g. database contexts, unit of work, user context).
  - `AddTransient`: For lightweight, stateless operations.
  - `AddSingleton`: Only for thread-safe caches, single-instance background workers, or read-only services.

### 4. Error Handling & RFC 9457 ProblemDetails
- **Standardized Error Format**: Return client-facing errors using RFC 9457 `ProblemDetails`.
- **Global Exception Handling**: Do not wrap every controller action in boilerplate `try-catch` blocks. Rely on global exception handling middleware in `Program.cs` for unhandled exceptions.
- **Status Code Mapping**:
  - Validation failures -> 400 Bad Request
  - Resource not found -> 404 Not Found
  - Unauthorized/Forbidden -> 401 / 403
  - Concurrency/State conflict -> 409 Conflict

### 5. Configuration & Secrets Hygiene
- **Strongly Typed Options**: Bind configuration sections in `appsettings.json` to strongly typed classes via `IOptions<TOptions>` or `IOptionsSnapshot<TOptions>`.
- **Zero Secrets in Git**: Never place connection strings, JWT keys, or API tokens in `appsettings*.json`. Use `dotnet user-secrets` for local development and environment variables for deployment.
- **Safe Logging**: Never log PII, authorization tokens, or sensitive payloads into logging providers.
