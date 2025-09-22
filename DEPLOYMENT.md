Deploy & Configuration Guide (AppManager)
=========================================

This guide describes how to publish, configure, and host the AppManager (ASP.NET Core Razor Pages, .NET 8) on Windows with IIS. It also documents appsettings and environment configuration.

Prerequisites
-------------
- Windows Server 2019/2022 or Windows 10/11 with IIS enabled
- Administrator rights on the machine
- .NET 8 ASP.NET Core Hosting Bundle installed (contains the runtime and IIS module)
	- Download: https://dotnet.microsoft.com/download/dotnet/8.0 → "ASP.NET Core Runtime 8.x - Hosting Bundle"
- Git (optional) and PowerShell 5+ (default on Windows)

Repository layout (relevant)
----------------------------
- `AppManager.sln` / `AppManager.csproj` – application
- `appsettings*.json` – configuration per environment
- `Install-AppManagerServer.ps1` – IIS bootstrap (creates site + app pool, firewall rules)
- `scripts/Stop-AppManagerProcess.ps1` – kills running local process to unblock publish
- `scripts/Fix-IIS-ConfigCheck.ps1` – diagnoses IIS shared configuration redirection
- `web.config` – ASP.NET Core IIS Module config for the deployed site

Environments and configuration
------------------------------
The application uses standard ASP.NET Core configuration:
- Base: `appsettings.json`
- Overrides: `appsettings.Development.json`, `appsettings.Production.json`, plus optional `appsettings.Portable.json`, `appsettings.SQLite.json`
- Environment selection via `ASPNETCORE_ENVIRONMENT` (e.g., `Production`, `Development`)

Important keys
- `ConnectionStrings:DefaultConnection` – SQLite database path. Default is `Data Source=local.db` (relative to current working directory).
- `Logging` – log levels
- `EnforceHttpsRedirect` – set to `true` in production if you terminate TLS in IIS and want HTTPS redirects handled by the app (optional)
- `AllowedHosts` – hosts allowed by Host Filtering middleware (use `*` or a comma-separated list)

Environment variable overrides
Any key can be overridden by environment variables. Use double underscore `__` for nesting, for example:
- `ASPNETCORE_ENVIRONMENT=Production`
- `ConnectionStrings__DefaultConnection=Data Source=C:\inetpub\wwwroot\AppManager\local.db`

SQLite database placement and permissions
----------------------------------------
The app uses EF Core with SQLite. Place the database in a folder where the IIS Application Pool identity can read/write.

Recommended:
- Keep `local.db` beside the deployed binaries (site folder) OR under a dedicated data folder (e.g., `C:\Data\AppManager\local.db`).
- Set NTFS permissions: grant Modify to the app pool identity `IIS AppPool\<AppPoolName>`.

IIS setup (automated)
---------------------
You can let the script create an IIS site and app pool:

1) Open PowerShell as Administrator
2) Run:

```
cd C:\guelistan\Dersler\Appmanager
.\n+Install-AppManagerServer.ps1 -SiteName "AppManager" -AppPoolName "AppManagerPool" -Port 80 -SitePath "C:\inetpub\wwwroot\AppManager"
```

What it does:
- Enables IIS features
- Checks for .NET runtime
- Creates/Configures App Pool (`No managed code`, Identity = `ApplicationPoolIdentity`)
- Creates site on specified port and path
- Grants NTFS permissions to the app pool identity
- Adds firewall rules for the port

IIS setup (manual summary)
--------------------------
If you prefer manual steps via IIS Manager:
- Install IIS with features: Static Content, Default Document, HTTP Errors, Logging, Request Filtering, Security
- Install .NET 8 Hosting Bundle
- Create an Application Pool (No Managed Code, Integrated, Identity = ApplicationPoolIdentity)
- Create a Site (Physical Path = publish folder, Binding = http/https as needed)
- Grant NTFS Modify permissions for `IIS AppPool\<AppPoolName>` on the site folder (and on the SQLite DB file/folder)
- If using HTTPS: add a certificate binding in IIS and optionally enable HSTS/redirects

Windows Authentication
----------------------
The application expects Windows Authentication to be available to enrich claims. In IIS:
- Enable "Windows Authentication" for the site
- Disable "Anonymous Authentication" if you require Windows login for the whole app
- If you want public pages with Windows auth only for /Admin, keep Anonymous enabled and enforce authorization via the app

Publishing
----------
From a developer machine or build agent:

PowerShell (run from repo root):

```
# Optional: stop any local dev process to avoid file locks
scripts\Stop-AppManagerProcess.ps1

# Restore + Build + Publish (Release)
dotnet restore
dotnet build -c Release
dotnet publish -c Release -o .\publish
```

Deployment
----------
Copy the publish output to the IIS site path, e.g. `C:\inetpub\wwwroot\AppManager`.

```
# Example copy to site folder (adjust path)
robocopy .\publish C:\inetpub\wwwroot\AppManager /MIR /R:1 /W:2 /NFL /NDL /NP

# Ensure database location and permissions
$db = "C:\inetpub\wwwroot\AppManager\local.db"
if (-not (Test-Path $db)) { New-Item -Path $db -ItemType File | Out-Null }
$acl = Get-Acl (Split-Path $db -Parent)
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS AppPool\AppManagerPool","Modify","ContainerInherit,ObjectInherit","None","Allow")
$acl.SetAccessRule($rule); Set-Acl (Split-Path $db -Parent) $acl

# Optionally set explicit connection string via environment variable for the App Pool
[Environment]::SetEnvironmentVariable("ConnectionStrings__DefaultConnection", "Data Source=$db", "Machine")
```

App Pool and site recycle
-------------------------
```
Import-Module WebAdministration
Restart-WebAppPool -Name "AppManagerPool"
Restart-WebSite -Name "AppManager"
```

Database migrations
-------------------
The app applies EF Core migrations on startup (see `Program.cs`). Ensure the App Pool identity can write to the database. If you prefer manual migration execution, run the app once to let it create/update the DB.

Configuring HTTPS
-----------------
Two options:
1) Terminate TLS in IIS only: configure HTTPS binding on the site with a certificate; set `EnforceHttpsRedirect` in `appsettings.Production.json` to `true` (and ensure reverse-proxy headers are forwarded by the IIS module by default).
2) HTTP-only (intranet): keep `EnforceHttpsRedirect=false` and only allow internal access.

Log levels (production)
-----------------------
`appsettings.Production.json` sets:
- `Default`: `Warning`
- `Microsoft.AspNetCore`: `Warning`
You can raise EF SQL logs temporarily by adding `"Microsoft.EntityFrameworkCore.Database.Command": "Information"` while troubleshooting.

Troubleshooting
---------------
- 500.31 ANCM Failed to Find Native Dependencies: ensure Hosting Bundle installed, correct x64 runtime
- 403/401: verify Windows Authentication is enabled/ordered correctly in IIS and the app’s authorization rules
- Database locked or no history entries: verify file permissions for `local.db` (App Pool identity needs Modify)
- Redirection/config errors: run `scripts/Fix-IIS-ConfigCheck.ps1` for shared configuration diagnostics
- File locks on deploy: recycle the site or stop the worker process; use `scripts/Stop-AppManagerProcess.ps1` for local dev

Quick checklist
---------------
- [ ] Hosting Bundle installed
- [ ] IIS site + app pool created
- [ ] Folder permissions for `IIS AppPool\<AppPoolName>`
- [ ] Publish artifacts copied to site folder
- [ ] `ConnectionStrings:DefaultConnection` points to a writable path
- [ ] Windows Authentication configured as desired
- [ ] Optional: HTTPS binding and redirects
