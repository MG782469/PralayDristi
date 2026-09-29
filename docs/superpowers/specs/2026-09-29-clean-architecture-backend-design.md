# PralayDrishti backend: Clean Architecture restructure

Date: 2026-09-29
Status: draft, awaiting review

## 1. Goal

Restructure the working prototype backend (`PralayDrishti.Core` + `PralayDrishti.Api`) into a Clean Architecture solution with CQRS, validation, authentication, background jobs and caching, without losing any existing functionality. The hydraulics engine, its accuracy (12 passing tests) and every existing endpoint keep working.

Success criteria:

- Four projects: `PralayDrishti.Domain`, `PralayDrishti.Application`, `PralayDrishti.Infrastructure`, `PralayDrishti.API`. Dependencies only point inwards.
- Every controller action is a single `ISender.Send(...)` call (plus HTTP shaping).
- Every command has a FluentValidation validator, run automatically by a MediatR pipeline behavior.
- JWT access tokens carry `user_id`, `email` and `role` claims. Refresh tokens rotate and are stored hashed.
- Swagger has a working "Authorize" button.
- Simulations run as Hangfire jobs (Redis storage). The lake watch runs as a Hangfire recurring job.
- Redis caches read-heavy queries, with an in-memory fallback when Redis is down.
- EF Core on PostgreSQL (Neon). Every entity is configured explicitly through `IEntityTypeConfiguration<T>`.
- `Program.cs` only calls `AddApplication()`, `AddInfrastructure(config)`, `AddPresentation(config)` and maps the pipeline.
- The existing test suite passes after the move, and new tests cover auth and validation.

## 2. Decisions already agreed

| Topic | Decision |
|---|---|
| Database | Neon (serverless PostgreSQL). Connection string left empty in `appsettings.json` for the user to fill. |
| Local infra | Docker Desktop (installed). `docker-compose.dev.yml` runs Redis, plus an optional local Postgres for development until the Neon string is filled in. |
| Hangfire storage | Redis (`Hangfire.Redis.StackExchange`). Keeps Neon compute asleep between requests. |
| Mediator | MediatR 12.x (Apache-2.0; v13+ is commercially licensed). |
| Registration | Open self-registration behind `Auth:AllowSelfRegistration` (default `true` for the demo). New users get the `Viewer` role. |
| Demo accounts | Seeded Admin, Analyst and Viewer accounts from configuration; the login page offers one-click demo login. |
| Kubernetes | Not now. Design stays k8s-ready: stateless API, shared Redis, results behind an `IResultStore` abstraction, health endpoints, config from env vars. |
| Hydraulics engine | Moves into Infrastructure behind Application interfaces. Code is moved, not rewritten. |

## 3. Solution layout

```
backend/
  PralayDrishti.sln
  src/
    PralayDrishti.Domain/          no package references
    PralayDrishti.Application/     -> Domain
    PralayDrishti.Infrastructure/  -> Application, Domain
    PralayDrishti.API/             -> Application, Infrastructure (composition root only)
  tests/
    PralayDrishti.UnitTests/       Domain + Application (validators, handlers, auth)
    PralayDrishti.EngineTests/     existing solver/pipeline/export tests, re-pointed to Infrastructure
docker-compose.dev.yml
```

The API references Infrastructure only in `Program.cs` / `DependencyInjection` to register services. Controllers never use Infrastructure types.

## 4. Domain layer

**Entities** (`Entities/`), each with a `Guid Id` except `Dam` which keeps `int Id`:

- `User`: Email (unique), NormalizedEmail, FullName, PasswordHash, Role (`UserRole`: Admin, Analyst, Viewer), IsActive, CreatedUtc, LastLoginUtc.
- `RefreshToken`: UserId, TokenHash, ExpiresUtc, CreatedUtc, CreatedByIp, RevokedUtc, RevokedByIp, ReplacedByTokenHash, RevokeReason. Methods: `IsActive`, `Revoke(...)`.
- `Dam`: the existing register fields (name, river, state, lat/lon, type, height, storage, crest, category, notes, source, cascade list, approximate flag).
- `SimulationRun`: Name, Status (`RunStatus`), Progress, Stage, Mode, Origin, SpecJson, SummaryJson, Error, timestamps, DamId?, LakeDetectionId?, CreatedByUserId?, HangfireJobId?.
- `LakeDetection`: the existing fields plus State (`LakeState` enum instead of a string).
- `Alert`: the existing fields; Severity and Status become enums.

**Enums**: `UserRole`, `RunStatus`, `RunOrigin`, `LakeState`, `AlertSeverity`, `AlertStatus`, `DamCategory`.

**Exceptions** (`Exceptions/`): `DomainException` (base), `NotFoundException`, `ConflictException`, `ForbiddenAccessException`, `BusinessRuleException`.

**Repository interfaces** (`Interfaces/`): `IRepository<T, TKey>` (get, list, add, remove), `IUserRepository` (by email), `IRefreshTokenRepository` (by hash, active tokens for a user), `IDamRepository`, `ISimulationRunRepository` (paged list, by origin, latest atlas run per dam), `ILakeDetectionRepository`, `IAlertRepository`, `IUnitOfWork` (`SaveChangesAsync`).

## 5. Application layer

### 5.1 Folder convention

```
Features/
  Commands/<Category>/<Name>/<Name>Command.cs
                             <Name>CommandHandler.cs
                             <Name>CommandValidator.cs
  Queries/<Category>/<Name>/<Name>Query.cs
                            <Name>QueryHandler.cs
Common/
  Abstractions/     adapter interfaces implemented in Infrastructure / API
  Behaviors/        ValidationBehavior, LoggingBehavior, CachingBehavior, PerformanceBehavior
  Models/           DTOs, PagedResult, AuthResult
  Mappings/         entity -> DTO mapping (hand written, no AutoMapper)
Services/           cross-feature orchestration
Jobs/               Hangfire-agnostic job classes
DependencyInjection.cs   AddApplication()
```

### 5.2 Features

| Category | Commands | Queries |
|---|---|---|
| Auth | Register, Login, RefreshToken, Logout, ChangePassword | GetCurrentUser |
| Users | ChangeUserRole, SetUserActive (Admin) | GetUsers (Admin) |
| Dams | CreateDam, UpdateDam, DeleteDam, SimulateDam | GetDams, GetDamById, GetDefaultScenario |
| Runs | StartSimulation, CancelRun, RerunSimulation, DeleteRun, RaiseAlert | GetRuns, GetRunDetail, GetRunStatus, SamplePoint, GetRiverProfile, ExportRun, GetLayerImage |
| Analysis | (none) | EstimateNaturalDam, EstimateReservoir, CalculateBreach, InverseThreshold, InverseFromObserved |
| Lakes | IngestDetections, SimulateLake, DismissLake, DetectFromNdwi | GetLakes, GetWaterArea |
| Alerts | AcknowledgeAlert | GetAlerts, GetAlertCap, GetAlertFeed |
| Atlas | RunAtlas | GetLeaderboard |
| Validation | CompareWithObserved, RunBenchmark | GetAnalyticCheck, GetSphCheck, GetScoreboard |
| Data | UploadDem, UploadExposure | GetDems, GetExposure, GetSystemInfo |
| Wms | (none) | GetWmsCapabilities, GetWmsMap |

Inverse queries run the model several times. They are queries (no state change) but long-running, so they keep a request timeout and honour cancellation.

### 5.3 Pipeline behaviors

- `ValidationBehavior`: runs every `IValidator<TRequest>` and throws `ValidationException` with all failures.
- `CachingBehavior`: requests implementing `ICacheableQuery` (`CacheKey`, `Expiry`) are served from `ICacheService`. Commands implementing `ICacheInvalidator` (`InvalidatesKeys`) clear keys after success.
- `LoggingBehavior`: request name, user id, duration.
- `PerformanceBehavior`: logs a warning for requests slower than 2 s, excluding known long-running requests.

### 5.4 Adapter interfaces (`Common/Abstractions`)

| Interface | Implemented in | Purpose |
|---|---|---|
| `ICurrentUserService` | API | user id, email and role from claims |
| `IPasswordHasher` | Infrastructure | wraps ASP.NET Core `PasswordHasher<User>` |
| `ITokenService` | Infrastructure | access token creation, refresh token generation and hashing |
| `IDateTimeProvider` | Infrastructure | testable clock |
| `ICacheService` | Infrastructure | get/set/remove with JSON serialisation; Redis with in-memory fallback |
| `IBackgroundJobScheduler` | Infrastructure | enqueue, schedule, recurring, delete (Hangfire) |
| `IRunNotifier` | API | SignalR push: progress, status, alert, lake |
| `IFloodSimulationEngine` | Infrastructure | runs a `ScenarioSpec` end to end, returns a run outcome handle |
| `IDemProvider`, `IExposureProvider` | Infrastructure | terrain and exposure sources |
| `IResultStore` | Infrastructure | save/load/delete run results (local disk now; object storage later) |
| `IExportService` | Infrastructure | SHP, KMZ, KML, GeoJSON, CAP, PDF, CSV, ASC, Delft3D, DualSPHysics |
| `IMapRenderer` | Infrastructure | layer PNGs and WMS GetMap |
| `IHydraulicAnalysis` | Infrastructure | natural dam, reservoir estimate, breach calculator, inverse queries |
| `IValidationBenchmarks` | Infrastructure | analytic, SPH, benchmark and observed-flood comparisons |
| `IFileStorage` | Infrastructure | uploaded DEM / exposure / NDWI files |

The engine DTOs that cross the boundary (`ScenarioSpec`, `RunDetailDto`, `ImpactReport`, and so on) live in Application `Common/Models`. Engine-internal types stay in Infrastructure.

### 5.5 Services

- `SimulationRunService`: executes a queued run: status updates, progress throttling, calls the engine, stores results, writes the summary, notifies clients, raises the alert for lake-watch runs.
- `LakeToAlertService`: detection → volume estimate → natural-dam scenario → queue run.
- `AtlasService`: queues batch runs and builds the leaderboard.
- `AlertService`: builds and stores CAP alerts from an outcome.
- `AuthService`: issues token pairs, rotates refresh tokens, detects reuse.

### 5.6 Background jobs (Hangfire-agnostic, in `Jobs/`)

- `RunSimulationJob.Execute(Guid runId)`: enqueued by StartSimulation, SimulateDam, Rerun, Atlas and the lake chain. The Hangfire job id is stored on the run so CancelRun can delete it. A running job observes a cancellation token that is triggered through Redis.
- `LakeWatchJob.Execute()`: recurring every minute; processes `data/gee_outbox`.
- `RefreshTokenCleanupJob.Execute()`: recurring daily; deletes expired or revoked tokens older than 30 days.
- `ResultsRetentionJob.Execute()`: recurring daily; removes result folders of deleted runs.

Queues: `simulations` (worker count from config, default 1), `default`. This is what lets simulation workers scale separately later.

## 6. Infrastructure layer

```
Persistence/   AppDbContext, Configurations/*Configuration.cs, Repositories/, UnitOfWork, Migrations/, Seed/DbSeeder
Identity/      JwtTokenService, PasswordHasherAdapter, JwtOptions
Caching/       RedisCacheService (IDistributedCache + in-memory fallback)
BackgroundJobs/ HangfireJobScheduler, HangfireDashboardAuthFilter, RecurringJobsRegistrar
Engine/        Geo, Hydraulics, Solvers, Analysis, Pipeline, Export, External (moved from Core)
Services/      FloodSimulationEngine, DemProvider, ExposureProvider, ResultStore, ExportService, MapRenderer, HydraulicAnalysis, ValidationBenchmarks, FileStorage
DependencyInjection.cs   AddInfrastructure(IConfiguration)
```

### 6.1 EF Core model rules

- Provider: Npgsql, with `EnableRetryOnFailure` and snake_case naming (`EFCore.NamingConventions`).
- Every entity has a configuration class. It sets table name, key, `IsRequired`, `HasMaxLength` on every string, precision on decimals, `HasConversion<string>()` for enums, and `timestamptz` for all `DateTime` values (UTC only).
- `SpecJson` and `SummaryJson` are `jsonb`.
- Indexes:
  - unique `users.normalized_email`
  - `refresh_tokens.token_hash` (unique) and `refresh_tokens.user_id`
  - `simulation_runs.created_utc` and `(origin, status)`
  - `lake_detections.detected_utc`
  - `alerts.created_utc`
- Relationships:
  - RefreshToken → User: cascade delete
  - SimulationRun → Dam: set null
  - SimulationRun → User: set null
  - LakeDetection → SimulationRun: set null
  - Alert → SimulationRun: cascade
- Migrations live in Infrastructure. They are applied at startup when `Database:MigrateOnStartup` is true (default true in Development).
- Seeding: dams from `data/seed/dams.json`, plus demo users from `Seed:DemoUsers`.

### 6.2 JWT and refresh tokens

- Access token: HMAC-SHA256, lifetime `Jwt:AccessTokenMinutes` (default 15). Claims: `user_id`, `email`, `role`, `jti`, `iat`. `TokenValidationParameters` map `RoleClaimType = "role"` and `NameClaimType = "email"`.
- Refresh token: 64 random bytes, base64url, lifetime `Jwt:RefreshTokenDays` (default 7). Only its SHA-256 hash is stored.
- Login and register return `{ accessToken, accessTokenExpiresUtc, refreshToken, refreshTokenExpiresUtc, user }`. They also set the refresh token in an `HttpOnly`, `Secure`, `SameSite=Strict` cookie scoped to `/api/auth`.
- Refresh accepts the token from the body (Swagger, tools) or from the cookie (browser). It revokes the old token, sets `ReplacedByTokenHash` and issues a new pair.
- Reuse detection: presenting an already-revoked token revokes every active token of that user and returns 401.
- Logout revokes the presented token and clears the cookie.
- Passwords: at least 8 characters, with a letter and a digit (FluentValidation).

### 6.3 Caching

| Key | Content | Expiry | Invalidated by |
|---|---|---|---|
| `dams:all` | dam list | 10 min | Create / Update / Delete dam |
| `dams:{id}:scenario:{cascade}` | default scenario | 10 min | Update / Delete dam |
| `runs:{id}:detail` | run detail DTO (only once completed) | 1 h sliding | DeleteRun |
| `runs:{id}:profile` | river profile | 1 h | DeleteRun |
| `validation:scoreboard:analytic` | analytic and SPH checks | 24 h | none |
| `system:info` | system info | 30 s | none |
| `atlas:leaderboard` | leaderboard | 2 min | run completion with origin atlas |

Large binaries (layer PNGs, exports) are not put in Redis. They use HTTP cache headers and the on-disk export cache that already exists.

### 6.4 Configuration (`appsettings.json`)

```json
"ConnectionStrings": { "DefaultConnection": "", "Redis": "localhost:6379" },
"Jwt": { "Issuer": "PralayDrishti", "Audience": "PralayDrishti.Client", "Secret": "", "AccessTokenMinutes": 15, "RefreshTokenDays": 7 },
"Auth": { "AllowSelfRegistration": true },
"Seed": { "DemoUsers": [
  { "Email": "admin@pralaydrishti.local", "Password": "", "Role": "Admin" },
  { "Email": "analyst@pralaydrishti.local", "Password": "", "Role": "Analyst" },
  { "Email": "viewer@pralaydrishti.local", "Password": "", "Role": "Viewer" } ] },
"Hangfire": { "SimulationWorkers": 1, "DashboardPath": "/hangfire" },
"Database": { "MigrateOnStartup": true },
"PralayDrishti": { "DataDir": "../../../data", "Offline": false, "UseOsm": true, "...": "unchanged from today" }
```

`appsettings.Development.json` holds a development JWT secret, demo passwords and a local Postgres string, so the project runs before the Neon string exists. Options are validated at startup with `ValidateOnStart`. An empty connection string outside Development fails fast with a clear message.

## 7. API layer

```
Controllers/   AuthController, UsersController, DamsController, RunsController, AnalysisController,
               LakesController, AlertsController, AtlasController, ValidationController, DataController,
               TilesController, WmsController
Hubs/          RunHub, SignalRRunNotifier (IRunNotifier)
Middleware/    ExceptionHandlingMiddleware
Services/      CurrentUserService
Extensions/    DependencyInjection.AddPresentation(), SwaggerExtensions, AuthExtensions, WebApplicationExtensions
Program.cs
```

- Error mapping (RFC 7807 ProblemDetails):

  | Exception | Status |
  |---|---|
  | `ValidationException` | 400, with an `errors` dictionary |
  | `NotFoundException` | 404 |
  | `ConflictException` | 409 |
  | `ForbiddenAccessException` | 403 |
  | `BusinessRuleException` | 422 |
  | anything else | 500, no stack trace outside Development |

- Authorization policies:

  | Policy | Roles | Covers |
  |---|---|---|
  | `CanRead` | any authenticated user | read endpoints |
  | `CanSimulate` | Analyst, Admin | runs, analysis, lake actions, uploads, alerts |
  | `AdminOnly` | Admin | dam CRUD, users, atlas, Hangfire dashboard |

  Dam CRUD is Admin only; SimulateDam is `CanSimulate`.
- Anonymous endpoints:
  - `/api/auth/*` (except logout and me)
  - `/health/live` and `/health/ready`
  - `/api/tiles/terrarium/*`
  - `/wms` and `/api/runs/{id}/image/*`: GIS clients cannot send a JWT, and run ids are unguessable GUIDs. This is noted as a production hardening item (API key).
- Swagger: Bearer security definition and requirement, XML comments, and grouping by controller.
- SignalR: JWT read from the `access_token` query string for `/hubs/runs`.
- Health checks: Postgres, Redis, Hangfire.

## 8. Engine fixes in scope

Found while making the slide visuals; fixed during the move:

1. **Dam snapping into reservoirs** (Tehri): the snap radius drops to 400 m and prefers the highest-accumulation cell whose downstream trace descends at least 10 m within 2 km. If none qualifies, the run logs a warning and uses the nearest channel cell.
2. **Short dam walls** (Hirakud): the cross-valley wall half-length becomes `max(3 km, crest length)`, capped at 15 km. Dams can also carry an optional dyke polyline in the register.
3. **Breach peak vs empirical**: routed and empirical peaks are both reported (already done). A new `CalibrateBreachToEmpirical` option (default off) scales breach width so the routed peak matches the chosen empirical peak. When they differ by more than a factor of 2, the log and UI show a warning.

## 9. Frontend changes in scope

- Login / register page with one-click demo buttons (Admin, Analyst, Viewer).
- Auth context: access token kept in memory, refresh through the cookie, and a fetch wrapper that retries once after refresh on a 401.
- SignalR `accessTokenFactory`.
- Role-aware UI: run, simulate, upload and alert actions disabled for Viewer; a Users tab for Admin; logout.

## 10. Testing

- Existing engine, pipeline and export tests move to `PralayDrishti.EngineTests` and must stay green (12 tests).
- Unit tests:
  - every validator: valid and invalid cases
  - auth: register, login with bad password, refresh rotation, reuse detection revokes the family, logout
  - `CachingBehavior` hit and miss
  - `ValidationBehavior`
  - handler tests that use in-memory fakes of repository interfaces
- Integration smoke test (runs only when Docker is available): `WebApplicationFactory` with Postgres and Redis containers. It covers register → login → start a synthetic-valley run → poll until completed → export PDF.

## 11. Out of scope for this spec

- GEE Python scripts (`gee/lake_watch.py`, `gee/s1_flood_map.py`): next task.
- Production Dockerfiles, full `docker-compose.yml` and README: next task. This spec only adds `docker-compose.dev.yml`.
- Kubernetes manifests: roadmap only.
- Rewriting the hydraulics engine. Only the three fixes in section 8 are made.

## 12. Risks

- **Neon cold start** adds 0.5-2 s to the first request after idle. Mitigation: retry-on-failure and a warm-up query in the health check.
- **Redis unavailable in development.** The cache falls back to memory, but Hangfire needs Redis. Without it, `AddInfrastructure` registers an `InProcessJobScheduler` (a `Channel`-backed hosted service, the same approach the prototype uses today) instead of Hangfire, and logs a warning. Recurring jobs then run on a `PeriodicTimer`.
- **Long-running inverse queries tie up a request thread.** They are acceptable for the demo, and noted for later conversion into jobs.
