Architecture & Flows (AppManager)
=================================

Overview
--------
AppManager is an ASP.NET Core Razor Pages app (.NET 8) hosted on IIS with Windows Authentication. This document covers authentication/claims, DI registrations, DB path resolution, HTTPS/HSTS options, request pipeline, and activity-logging flow.

Authentication: Negotiate + Windows Claims
-----------------------------------------
- Configured in `Program.cs` using `AddNegotiate` with a custom `OnAuthenticated` event.
- When a user is authenticated via Windows (Kerberos/NTLM), we enrich the principal with:
  - `windows_username` = DOMAIN\User
  - `windows_sid` = user SID
- Optional claims transformation (`WindowsUserClaimsTransformation`) can create an `AppUser` and attach Identity roles; currently not active by default.

Authorization
-------------
- Default and fallback policy requires an authenticated Windows user.
- `/Admin` folder is protected (Razor Pages conventions).
- An `Admin` policy exists and checks roles (`Admin`, `SuperAdmin`) when used.

Dependency Injection (key registrations)
---------------------------------------
- `AddDbContext<AppDbContext>` – EF Core with SQLite. Connection string from configuration.
- `AddIdentityCore<AppUser>` + roles + EF stores.
- `AddAuthentication().AddNegotiate("Windows")` with claims enrichment.
- `AddHttpContextAccessor()` – needed for services to resolve the current user.
- Scoped services: `ProgramManagerService`, `IISService`, `AppService`.
- `AddHttpLogging` for request logging.
- `AddHsts` configured but only enabled when HTTPS enforcement is active.

SQLite DB path resolution
-------------------------
- Configured in `Program.cs`. If the `Data Source` is a relative path (e.g., `local.db`), it is expanded to an absolute path under `ContentRootPath` and the folder is created if missing.
- This ensures the DB lives alongside deployed files in IIS deployments.

HTTPS and HSTS
--------------
- Controlled by the `EnforceHttpsRedirect` setting (default: `false`).
- If `true`: the app enables `UseHttpsRedirection()` and `UseHsts()` (in non-development).
- If terminating TLS in IIS, you can still enable redirects in the app for consistent behavior.

Request pipeline
----------------
Order in `Program.cs` (simplified):
1) Apply migrations on startup
2) Error handling (UseExceptionHandler in Production)
3) Optional: HSTS + HTTPS Redirection (when enabled)
4) Static Files
5) Routing
6) HTTP Logging
7) Authentication
8) Authorization
9) Map Razor Pages and Controllers

Activity logging flow
---------------------
- Centralized in `ProgramManagerService.LogAppActivityAsync(Application app, string action, string reason)`.
- Resolves current user via `IHttpContextAccessor` with the following strategy:
  1) Try by `windows_sid` claim → `AppUser.WindowsSid`
  2) Fallback by `windows_username` claim or `User.Identity.Name`
  3) Fallback to a `GlobalAdmin` or built-in `admin`
- Persists `AppLaunchHistory` with: `ApplicationId`, `UserId`, `WindowsUsername`, `IISAppPoolName`, `LaunchTime`, `Action`, `Reason`.
- Called from local app actions (start/stop/restart) and IIS actions (start/stop/recycle) in the pages.

IIS hosting model
-----------------
- `web.config` uses the ASP.NET Core Module V2 (inprocess). The publish pipeline injects `%LAUNCHER_PATH%` and `%LAUNCHER_ARGS%`.
- Environment defaults to `Production` via `web.config` environmentVariables (can be overridden per App Pool).

Key edge cases
--------------
- No Windows SID claim: fallback paths ensure we still log with `admin` as `UserId`.
- SQLite locked: ensure NTFS Modify permission for the App Pool identity on the DB folder.
- HTTPS enforced on terminal servers: keep `EnforceHttpsRedirect=false` to avoid auto-redirect loops if not fully configured.
