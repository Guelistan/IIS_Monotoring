Configuration Reference (AppManager)
====================================

Overview
--------
AppManager uses standard ASP.NET Core configuration with layered `appsettings` files and environment variables.

Files
-----
- `appsettings.json` – base settings
- `appsettings.Development.json` – dev overrides
- `appsettings.Production.json` – prod overrides
- `appsettings.Portable.json` – optional profile for portable runs (empty by default)
- `appsettings.SQLite.json` – optional profile for SQLite-specific overrides (empty by default)

Selecting environment
---------------------
Set `ASPNETCORE_ENVIRONMENT` to choose which `appsettings.{Environment}.json` is loaded. Common values:
- `Development`
- `Production`

Key sections
------------
1) ConnectionStrings
- `DefaultConnection`: SQLite connection string. Default: `Data Source=local.db`.
  - Example absolute path: `Data Source=C:\Data\AppManager\local.db`

2) Logging
- Configure log levels per category.
- Dev example includes EF SQL logging:
  - `Microsoft.EntityFrameworkCore.Database.Command: Information`

3) EnforceHttpsRedirect (Production)
- Boolean. If `true`, the app redirects HTTP → HTTPS inside the app (in addition to IIS binding).

4) AllowedHosts
- `*` or comma-separated hostnames to allow.

Environment variable overrides
------------------------------
All keys can be overridden via environment variables using `__` (double underscore) as a section separator.

Examples:
```
ASPNETCORE_ENVIRONMENT=Production
ConnectionStrings__DefaultConnection=Data Source=C:\inetpub\wwwroot\AppManager\local.db
Logging__LogLevel__Default=Warning
Logging__LogLevel__Microsoft.AspNetCore=Warning
```

Notes on SQLite
---------------
- Ensure the IIS App Pool identity has Modify rights on the folder containing the `.db` file.
- On first run, the app applies migrations and creates the DB if it doesn’t exist.

Windows Authentication
----------------------
- Enable in IIS if you want Windows-based user identities.
- The app enriches claims (e.g., SID, domain\user). Ensure your site’s authentication matches your security policy.
