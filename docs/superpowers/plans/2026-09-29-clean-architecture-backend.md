# Clean Architecture Backend Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure the PralayDrishti backend into Domain / Application / Infrastructure / API with MediatR CQRS, FluentValidation, EF Core on PostgreSQL (Neon), JWT with rotating refresh tokens, Hangfire on Redis and Redis caching, keeping every existing feature and test working.

**Architecture:** The hydraulics engine (today `PralayDrishti.Core`) moves into `PralayDrishti.Infrastructure/Engine` and is reached only through Application interfaces. Controllers send MediatR requests; handlers use Domain repository interfaces and Application adapter interfaces; Infrastructure implements them; the API is the composition root.

**Tech Stack:** .NET 8, ASP.NET Core, MediatR 12.4.1, FluentValidation 11.11, EF Core 8 + Npgsql 8 + EFCore.NamingConventions 8, Hangfire.Core/AspNetCore 1.8 + Hangfire.Redis.StackExchange 1.9, StackExchange.Redis via Microsoft.Extensions.Caching.StackExchangeRedis 8, Microsoft.AspNetCore.Authentication.JwtBearer 8, Swashbuckle 6.6, xUnit, React 18 (frontend).

**Spec:** `docs/superpowers/specs/2026-09-29-clean-architecture-backend-design.md`

## Global Constraints

- Project names exactly: `PralayDrishti.Domain`, `PralayDrishti.Application`, `PralayDrishti.Infrastructure`, `PralayDrishti.API`; tests `PralayDrishti.UnitTests`, `PralayDrishti.EngineTests`.
- Domain has no package references. Application references only Domain, MediatR, FluentValidation, `Microsoft.Extensions.*.Abstractions`. No Hangfire, EF or Redis types in Application.
- Folder convention: `Features/Commands/<Category>/<Name>/<Name>Command.cs|CommandHandler.cs|CommandValidator.cs`, `Features/Queries/<Category>/<Name>/<Name>Query.cs|QueryHandler.cs`.
- `Program.cs` calls only `AddApplication()`, `AddInfrastructure(builder.Configuration, builder.Environment)`, `AddPresentation(builder.Configuration)` and `app.UsePresentation()`.
- JWT claims: `user_id`, `email`, `role` (+ `jti`, `iat`). Access 15 min, refresh 7 days, refresh stored as SHA-256 hash only.
- New self-registered users get `Viewer`. `Auth:AllowSelfRegistration` default `true`.
- `ConnectionStrings:DefaultConnection` is `""` in `appsettings.json`; Development uses local Postgres from `docker-compose.dev.yml`.
- Every string property has an explicit `HasMaxLength` except `jsonb` columns; enums stored as strings; `DateTime` as `timestamptz` UTC.
- Existing 12 engine tests must stay green at every task boundary.
- Human-like code: match the prototype's style (sparse, purposeful comments, no XML-doc on every member).

## Review Focus

1. Two browser tabs refreshing at the same moment → the second presents a just-rotated token; expected: not logged out. Test in Task 4 (`Refresh_within_grace_window_returns_same_successor`) + single-flight refresh in Task 10.
2. Email casing / whitespace (`Admin@X.com ` vs `admin@x.com`) → same account. Test in Task 4 (`Login_is_case_insensitive_on_email`).
3. Redis down (dev box, container stopped) → API still starts, cache falls back, jobs run in-process. Test in Task 5 (`Cache_falls_back_to_memory_when_redis_unreachable`, `InProcess_scheduler_runs_enqueued_job`).
4. Deleting a dam that has runs → runs keep existing with `DamId = null`. Test in Task 3 (`Deleting_dam_nulls_run_reference`).
5. NaN values from the engine (arrival never reached) in JSON responses → serialised, not a 500. Test in Task 8 (`Run_detail_with_nan_serialises`).

---

### Task 0: Repository and solution skeleton, engine moved

**Files:**
- Create: `.gitignore`, `backend/src/PralayDrishti.Domain/`, `backend/src/PralayDrishti.Application/`, `backend/src/PralayDrishti.Infrastructure/`, `backend/tests/PralayDrishti.EngineTests/`, `backend/tests/PralayDrishti.UnitTests/`
- Move: `backend/src/PralayDrishti.Core/**` → `backend/src/PralayDrishti.Infrastructure/Engine/**` (namespaces `PralayDrishti.Core.X` → `PralayDrishti.Infrastructure.Engine.X`)
- Move: `backend/tests/PralayDrishti.Tests/*.cs` → `backend/tests/PralayDrishti.EngineTests/`
- Rename: `backend/src/PralayDrishti.Api` → `backend/src/PralayDrishti.API` (kept compiling on old code until Task 8)

**Interfaces:**
- Produces: project graph API→{Application, Infrastructure}, Infrastructure→{Application, Domain}, Application→Domain; engine namespaces `PralayDrishti.Infrastructure.Engine.{Geo,Hydraulics,Solvers,Analysis,Pipeline,Export,External}`.

- [ ] **Step 1:** `git init` at repo root, `.gitignore` for `bin/ obj/ node_modules/ data/runs/ data/cache/ data/*.db* data/uploads/ .vs/`. Commit the prototype as-is ("chore: prototype baseline").
- [ ] **Step 2:** Create the projects/solution entries and references; move Core files with a namespace rewrite; point the old API project at Infrastructure.
- [ ] **Step 3:** Run `dotnet test backend/tests/PralayDrishti.EngineTests` → expected `Passed: 12`.
- [ ] **Step 4:** Commit "refactor: clean-architecture skeleton, engine moved to Infrastructure".

### Task 1: Domain

**Files:**
- Create: `Domain/Entities/{User,RefreshToken,Dam,SimulationRun,LakeDetection,Alert}.cs`, `Domain/Enums/{UserRole,RunStatus,RunOrigin,LakeState,AlertSeverity,AlertStatus,DamCategory}.cs`, `Domain/Exceptions/{DomainException,NotFoundException,ConflictException,ForbiddenAccessException,BusinessRuleException}.cs`, `Domain/Interfaces/{IRepository,IUserRepository,IRefreshTokenRepository,IDamRepository,ISimulationRunRepository,ILakeDetectionRepository,IAlertRepository,IUnitOfWork}.cs`
- Test: `UnitTests/Domain/RefreshTokenTests.cs`, `UnitTests/Domain/UserTests.cs`

**Interfaces:**
- Produces: `User.Create(string email, string fullName, string passwordHash, UserRole role, DateTime nowUtc)` (sets `NormalizedEmail = email.Trim().ToUpperInvariant()`), `RefreshToken.IsActive(DateTime nowUtc)`, `RefreshToken.Revoke(DateTime nowUtc, string? ip, string reason, string? replacedByHash = null)`, `IRepository<T,TKey> { Task<T?> GetByIdAsync(TKey, CancellationToken); Task AddAsync(T, CancellationToken); void Remove(T); }`, `IUserRepository.GetByEmailAsync(string normalizedEmail, ct)`, `IRefreshTokenRepository.GetByHashAsync(string hash, ct)` and `.GetActiveForUserAsync(Guid userId, DateTime nowUtc, ct)`, `ISimulationRunRepository.ListAsync(RunOrigin? origin, int take, ct)`, `IUnitOfWork.SaveChangesAsync(ct)`.

- [ ] **Step 1: Write failing tests** — `Revoked_token_is_not_active`, `Expired_token_is_not_active`, `Create_normalizes_email` (`" Admin@X.com "` → `NormalizedEmail == "ADMIN@X.COM"`, `Email == "Admin@X.com"`).
- [ ] **Step 2:** Run `dotnet test backend/tests/PralayDrishti.UnitTests` → FAIL (types missing).
- [ ] **Step 3:** Implement entities (private setters, factory methods), enums, exceptions, interfaces.
- [ ] **Step 4:** Run tests → PASS. Commit "feat(domain): entities, enums, exceptions, repository interfaces".

### Task 2: Application core (DI, behaviors, abstractions, models)

**Files:**
- Create: `Application/DependencyInjection.cs` (`AddApplication(this IServiceCollection)`), `Application/Common/Behaviors/{ValidationBehavior,CachingBehavior,LoggingBehavior,PerformanceBehavior}.cs`, `Application/Common/Abstractions/*.cs` (the 15 interfaces from spec §5.4), `Application/Common/Caching/{ICacheableQuery,ICacheInvalidator}.cs`, `Application/Common/Exceptions/ValidationException.cs`, `Application/Common/Models/*.cs` (move `ScenarioSpec`, `DamSpec`, `NaturalDamSpec`, `StructureSpec` and enums `ScenarioMode`, `FarFieldEngine` here from the engine; engine consumes them), `Application/Common/Models/{RunSummaryDto,KeyNumbersDto,RunDetailDto,AuthResult,UserDto,PagedResult}.cs`
- Test: `UnitTests/Application/Behaviors/{ValidationBehaviorTests,CachingBehaviorTests}.cs`

**Interfaces:**
- Produces: `ICacheableQuery { string CacheKey { get; } TimeSpan Expiry { get; } }`, `ICacheInvalidator { IEnumerable<string> InvalidatesKeys { get; } }`, `ICacheService { Task<T?> GetAsync<T>(string key, ct); Task SetAsync<T>(string key, T value, TimeSpan expiry, ct); Task RemoveAsync(string key, ct); }`, `IBackgroundJobScheduler { string Enqueue<TJob>(Expression<Func<TJob,Task>> call, string queue = "default"); bool Delete(string jobId); void AddOrUpdateRecurring<TJob>(string id, Expression<Func<TJob,Task>> call, string cron); }`, `IRunNotifier { Task ProgressAsync(Guid runId, string stage, double fraction); Task StatusAsync(Guid runId, string status); Task AlertAsync(object alert); Task LakeAsync(object lake); }`, `IFloodSimulationEngine { Task<EngineRunResult> RunAsync(Guid runId, ScenarioSpec spec, IProgress<(string stage, double fraction)> progress, CancellationToken ct); }` where `EngineRunResult(KeyNumbersDto Numbers, IReadOnlyList<string> Log)`, `IResultStore { Task<RunDetailDto?> GetDetailAsync(Guid id, ct); bool Exists(Guid id); void Delete(Guid id); }`, `IExportService.ExportAsync(Guid runId, string format, ct) -> (byte[] Data, string ContentType, string FileName)`, `ICurrentUserService { Guid? UserId; string? Email; UserRole? Role; }`, `ITokenService { (string token, DateTime expiresUtc) CreateAccessToken(User user); (string raw, string hash, DateTime expiresUtc) CreateRefreshToken(); string Hash(string raw); }`, `IPasswordHasher { string Hash(User u, string password); bool Verify(User u, string hash, string password); }`, `IDateTimeProvider.UtcNow`.

- [ ] **Step 1: Failing tests** — `Throws_with_all_failures_when_invalid` (two rules → `ValidationException.Errors.Count == 2`), `Calls_next_when_valid`, `Returns_cached_value_without_calling_handler`, `Stores_result_on_miss`, `Invalidator_removes_keys_after_success`.
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement behaviors, register MediatR (`RegisterServicesFromAssembly`, open behaviors in order Logging → Validation → Caching → Performance) and `AddValidatorsFromAssembly`.
- [ ] **Step 4:** Run → PASS; EngineTests still 12. Commit "feat(application): DI, pipeline behaviors, abstractions".

### Task 3: Persistence (EF Core + Npgsql) and dev infrastructure

**Files:**
- Create: `docker-compose.dev.yml` (services `redis:7-alpine` on 6379, `postgres:16-alpine` on 5432 db `pralaydrishti` user/pass `pralay`/`pralay`), `Infrastructure/Persistence/AppDbContext.cs`, `Infrastructure/Persistence/Configurations/{User,RefreshToken,Dam,SimulationRun,LakeDetection,Alert}Configuration.cs`, `Infrastructure/Persistence/Repositories/*.cs`, `Infrastructure/Persistence/UnitOfWork.cs`, `Infrastructure/Persistence/Seed/DbSeeder.cs`, `Infrastructure/Persistence/Migrations/*` (`InitialCreate`), `Infrastructure/DependencyInjection.cs` (persistence part)
- Test: `UnitTests/Infrastructure/ModelConventionsTests.cs`, `EngineTests/Integration/PersistenceTests.cs` (skipped when Postgres unreachable)

**Interfaces:**
- Consumes: Task 1 interfaces.
- Produces: `AddInfrastructure(this IServiceCollection, IConfiguration, IHostEnvironment)`; `DbSeeder.SeedAsync(AppDbContext, IPasswordHasher, SeedOptions, string seedDamsPath, ct)`.

- [ ] **Step 1: Failing tests** — `Every_string_property_has_max_length_or_is_jsonb` (iterate `db.Model.GetEntityTypes()`), `Enums_are_stored_as_strings`, `Email_index_is_unique`, `Deleting_dam_nulls_run_reference` (integration), `Seeder_creates_dams_and_demo_users_once` (integration, run twice → counts unchanged).
- [ ] **Step 2:** `docker compose -f docker-compose.dev.yml up -d`; run → FAIL.
- [ ] **Step 3:** Implement context (`UseNpgsql(cs, o => o.EnableRetryOnFailure(5))`, `.UseSnakeCaseNamingConvention()`), configurations per spec §6.1 (lengths: email 256, names 160, river/state 80, stage 120, error 2000 as `text`, notes 400), repositories, UoW, seeder (dams from `data/seed/dams.json`, demo users from `Seed:DemoUsers`, skipped if password empty). `dotnet ef migrations add InitialCreate -p Infrastructure -s API -o Persistence/Migrations`.
- [ ] **Step 4:** Run → PASS. Commit "feat(infrastructure): EF Core model, repositories, migrations, seeding".

### Task 4: Identity and auth features

**Files:**
- Create: `Infrastructure/Identity/{JwtOptions,JwtTokenService,PasswordHasherAdapter,DateTimeProvider}.cs`, `Application/Services/AuthService.cs`, `Application/Features/Commands/Auth/{Register,Login,RefreshToken,Logout,ChangePassword}/*`, `Application/Features/Queries/Auth/GetCurrentUser/*`, `Application/Features/Commands/Users/{ChangeUserRole,SetUserActive}/*`, `Application/Features/Queries/Users/GetUsers/*`
- Test: `UnitTests/Application/Auth/AuthTests.cs`, `UnitTests/Infrastructure/JwtTokenServiceTests.cs`

**Interfaces:**
- Produces: `RegisterCommand(string Email, string Password, string FullName, string? Ip) : IRequest<AuthResult>`, `LoginCommand(string Email, string Password, string? Ip)`, `RefreshTokenCommand(string RefreshToken, string? Ip)`, `LogoutCommand(string RefreshToken, string? Ip) : IRequest`, `AuthResult(string AccessToken, DateTime AccessTokenExpiresUtc, string RefreshToken, DateTime RefreshTokenExpiresUtc, UserDto User)`, `AuthOptions { bool AllowSelfRegistration }`.

- [ ] **Step 1: Failing tests** (fake repos + real `JwtTokenService`):
  - `Register_assigns_viewer_role`
  - `Register_rejected_when_self_registration_disabled` → `BusinessRuleException`
  - `Register_duplicate_email_conflicts` → `ConflictException`
  - `Login_with_wrong_password_fails` → `ForbiddenAccessException`
  - `Login_is_case_insensitive_on_email`
  - `Refresh_rotates_token` (old revoked, `ReplacedByTokenHash` = new hash)
  - `Reusing_revoked_token_revokes_all_user_tokens`
  - `Refresh_within_grace_window_returns_same_successor` (presenting a token revoked ≤ 30 s ago with reason `rotated` returns a pair whose refresh hash equals its successor — no family revoke)
  - `Access_token_contains_user_id_email_role_claims`
  - validators: `Password_needs_8_chars_letter_and_digit`, `Email_must_be_valid`
- [ ] **Step 2:** Run → FAIL.
- [ ] **Step 3:** Implement. Grace window: at rotation, put the successor's raw refresh token in `ICacheService` under `rt-grace:{oldHash}` for 30 s; a request presenting the old token inside that window gets a fresh access token plus that cached successor instead of triggering reuse detection.
- [ ] **Step 4:** Run → PASS. Commit "feat(auth): JWT, rotating refresh tokens, auth and user features".

### Task 5: Caching and background jobs

**Files:**
- Create: `Infrastructure/Caching/RedisCacheService.cs`, `Infrastructure/BackgroundJobs/{HangfireJobScheduler,InProcessJobScheduler,RecurringJobsRegistrar,HangfireDashboardAuthFilter}.cs`, `Application/Jobs/{RunSimulationJob,LakeWatchJob,RefreshTokenCleanupJob,ResultsRetentionJob}.cs`
- Modify: `Infrastructure/DependencyInjection.cs` (Redis probe at startup with 2 s timeout → Hangfire + Redis cache, else in-memory cache + `InProcessJobScheduler`; queues `simulations` with `Hangfire:SimulationWorkers` workers and `default`)
- Test: `UnitTests/Infrastructure/{CacheFallbackTests,InProcessSchedulerTests}.cs`

**Interfaces:**
- Consumes: `ICacheService`, `IBackgroundJobScheduler` (Task 2).
- Produces: recurring ids `lake-watch` (`* * * * *`), `refresh-token-cleanup` (`0 3 * * *`), `results-retention` (`30 3 * * *`).

- [ ] **Step 1: Failing tests** — `Cache_falls_back_to_memory_when_redis_unreachable` (connection string `localhost:1`), `InProcess_scheduler_runs_enqueued_job`, `InProcess_delete_cancels_queued_job`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement. **Step 4:** Run → PASS. Commit "feat(infrastructure): redis cache with fallback, hangfire/in-process jobs".

### Task 6: Engine adapters

**Files:**
- Create: `Infrastructure/Services/{FloodSimulationEngine,DemProvider,ExposureProvider,ResultStore,ExportService,MapRenderer,HydraulicAnalysis,ValidationBenchmarks,FileStorage}.cs` — logic moved from `API/Services/{DemProvider,ExposureProvider,RunStore,RunViews}.cs` and the analysis/validation/WMS controllers.
- Test: `EngineTests/Adapters/{FloodSimulationEngineTests,ResultStoreTests}.cs`

**Interfaces:**
- Consumes: Task 2 abstractions; `ScenarioSpec` now from Application.
- Produces: implementations registered in `AddInfrastructure`; `IMapRenderer.RenderLayerPng(Guid runId, string layer) -> byte[]?`, `IMapRenderer.RenderWms(IReadOnlyDictionary<string,string> query) -> (byte[] Body, string ContentType, int Status)`, `IHydraulicAnalysis.{EstimateNaturalDamAsync, EstimateReservoirAsync, CalculateBreach, InverseThresholdAsync, InverseFromObservedAsync}` with request/response records in `Application/Common/Models/Analysis/`.

- [ ] **Step 1: Failing tests** — `Engine_runs_synthetic_valley_and_reports_numbers` (offline data dir → synthetic DEM, `Numbers.FloodedAreaKm2 > 0`, result exists in store), `Result_store_detail_roundtrip` (`GetDetailAsync` returns hydrograph peak equal to engine's).
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement by moving code. **Step 4:** Run → PASS (EngineTests total ≥ 14). Commit "feat(infrastructure): engine adapters behind application interfaces".

### Task 7: Application features (dams, runs, lakes, alerts, atlas, analysis, validation, data, wms)

**Files:**
- Create: every command/query in spec §5.2 under `Features/`, plus `Application/Services/{SimulationRunService,LakeToAlertService,AtlasService,AlertService}.cs`
- Test: `UnitTests/Application/Features/{DamFeatureTests,RunFeatureTests,LakeFeatureTests}.cs`

**Interfaces:**
- Consumes: Tasks 1, 2, 5, 6.
- Produces (used by Task 8 controllers): e.g. `CreateDamCommand(DamDto Dam) : IRequest<DamDto>, ICacheInvalidator`, `GetDamsQuery() : IRequest<IReadOnlyList<DamDto>>, ICacheableQuery` (key `dams:all`, 10 min), `StartSimulationCommand(ScenarioSpec Spec, int? DamId) : IRequest<RunSummaryDto>`, `GetRunDetailQuery(Guid Id) : IRequest<object>` (summary while pending, `RunDetailDto` when completed; cached `runs:{id}:detail` 1 h only when completed), `ExportRunQuery(Guid Id, string Format) : IRequest<FileResultDto>`, `IngestDetectionsCommand(IReadOnlyList<LakeDetectionInput> Detections, bool? AutoSimulate)`, `RaiseAlertCommand(Guid RunId, AlertStatus Status)`, `RunAtlasCommand(IReadOnlyList<int>? DamIds)`, `GetWmsMapQuery(IReadOnlyDictionary<string,string> Query)`.
- `SimulationRunService.ExecuteAsync(Guid runId, CancellationToken ct)` is what `RunSimulationJob` calls; cancellation is signalled by `CancelRunCommand` setting cache key `runs:{id}:cancel` which the service polls every 2 s.

- [ ] **Step 1: Failing tests** — `StartSimulation_validator_rejects_natural_mode_without_polygon`, `StartSimulation_creates_queued_run_and_enqueues_job` (fake scheduler records queue `simulations`), `DeleteDam_missing_throws_not_found`, `CreateDam_invalidates_dams_cache`, `CancelRun_on_queued_deletes_job_and_marks_cancelled`, `IngestDetections_creates_lake_and_queues_run_when_auto`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement (port validation rules from `RunsController.Validate` and existing controllers into validators/handlers). **Step 4:** Run → PASS. Commit "feat(application): CQRS features and services".

### Task 8: API layer

**Files:**
- Create: `API/Extensions/{DependencyInjection,SwaggerExtensions,AuthExtensions,WebApplicationExtensions}.cs`, `API/Middleware/ExceptionHandlingMiddleware.cs`, `API/Services/CurrentUserService.cs`, `API/Hubs/{RunHub,SignalRRunNotifier}.cs`, `API/Controllers/{Auth,Users,Dams,Runs,Analysis,Lakes,Alerts,Atlas,Validation,Data,Tiles,Wms}Controller.cs`
- Modify: `API/Program.cs` (four calls only), `API/appsettings.json` (spec §6.4), `API/appsettings.Development.json` (local Postgres string, dev JWT secret ≥ 32 chars, demo passwords `Demo@12345`)
- Delete: old `API/Services/*`, `API/Data/*`, old controllers
- Test: `EngineTests/Integration/ApiTests.cs` (WebApplicationFactory against docker Postgres/Redis; skipped when unreachable)

**Interfaces:**
- Consumes: all Application requests (Task 7), Task 4 auth.
- Produces: routes unchanged from the prototype (`/api/...`, `/wms`, `/hubs/runs`, `/health/live`, `/health/ready`, `/hangfire`) plus `/api/auth/{register,login,refresh,logout,me}` and `/api/users`.

- [ ] **Step 1: Failing tests** — `Swagger_declares_bearer_scheme`, `Anonymous_get_dams_is_401`, `Viewer_cannot_start_run_403`, `Analyst_full_flow_register_login_run_export_pdf` (synthetic offline run, poll ≤ 120 s), `Run_detail_with_nan_serialises`, `Validation_error_returns_problem_details_400_with_errors`, `Wms_getcapabilities_is_anonymous`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement: JWT bearer (`MapInboundClaims = false`, `RoleClaimType = "role"`, SignalR token from `access_token` query on `/hubs`), policies `CanRead`/`CanSimulate`/`AdminOnly`, JSON options (`AllowNamedFloatingPointLiterals`, `IncludeFields`, string enums), Hangfire dashboard with Admin filter, health checks (Npgsql, Redis), migrate + seed on startup when `Database:MigrateOnStartup`. **Step 4:** Run → PASS; all tests green. Commit "feat(api): thin controllers, auth, swagger, hangfire dashboard".

### Task 9: Engine fixes (spec §8)

**Files:**
- Modify: `Infrastructure/Engine/Geo/Hydrology.cs` (`SnapToRiver` gains `Func<int,bool>? accept`), `Infrastructure/Engine/Pipeline/ScenarioEngine.cs` (snap acceptance: downstream trace descends ≥ 10 m within 2 km, radius 400 m; wall half-length `Math.Clamp(Math.Max(3000, spec.Dam.CrestLengthM), 3000, 15000)`; `CalibrateBreachToEmpirical`), `Application/Common/Models/ScenarioSpec.cs` (add `bool CalibrateBreachToEmpirical`), `Application/Common/Models/DamSpec.cs` (add `List<double[]>? Dykes`)
- Test: `EngineTests/EngineFixTests.cs`

- [ ] **Step 1: Failing tests** — `Snap_rejects_reservoir_cell_and_picks_descending_channel` (synthetic valley with a flat 30-cell "lake" upstream of the dam point: snapped cell's downstream 2 km drop ≥ 10 m), `Wall_spans_crest_length` (crest 6000 m → wall cells ≥ 5500 m / dx), `Calibration_brings_routed_peak_within_10_percent_of_empirical`, `Large_peak_mismatch_logs_warning`.
- [ ] **Step 2:** Run → FAIL. **Step 3:** Implement (calibration: bisection on breach width factor in [0.2, 3], 12 iterations, target = first empirical peak). **Step 4:** Run → PASS. Commit "fix(engine): reservoir snapping, long dam walls, breach calibration".

### Task 10: Frontend auth

**Files:**
- Create: `frontend/src/auth.tsx` (context: `login`, `register`, `logout`, `user`, `hasRole`), `frontend/src/pages/LoginPage.tsx`
- Modify: `frontend/src/api.ts` (bearer header, `credentials: 'include'`, single-flight refresh on 401 then one retry), `frontend/src/hub.ts` (`accessTokenFactory`), `frontend/src/App.tsx` (gate on user, logout, Users page for Admin), pages with actions (disable for Viewer)

- [ ] **Step 1:** Implement; `npx tsc -b` → no errors.
- [ ] **Step 2:** Browser check: demo Viewer login → Run button disabled; demo Analyst → start synthetic run → progress via SignalR → PDF export downloads; token expiry simulated by `Jwt:AccessTokenMinutes = 1` → page keeps working after 70 s.
- [ ] **Step 3:** Commit "feat(frontend): login, token refresh, role-aware UI".

### Task 11: End-to-end verification

- [ ] **Step 1:** `docker compose -f docker-compose.dev.yml up -d`; `dotnet test backend` → all green (report counts).
- [ ] **Step 2:** Run API + dashboard; Swagger: Authorize with Analyst token → `POST /api/dams/1/simulate` → completes; `/hangfire` visible only to Admin.
- [ ] **Step 3:** Stop Redis container → API restart still healthy (`/health/live` 200, `/health/ready` degraded), run still completes in-process.
- [ ] **Step 4:** Commit "chore: verified end-to-end"; summarise to user with any deviations.
