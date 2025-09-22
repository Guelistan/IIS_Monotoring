IIS Server Setup (AppManager)
=============================

Use this guide to prepare IIS for hosting the AppManager application.

1) Install prerequisites
------------------------
- Enable Windows features for IIS (Server Manager or PowerShell)
	- Web Server (IIS)
	- Common HTTP Features: Static Content, Default Document, Directory Browsing (optional), HTTP Errors
	- Security: Request Filtering, Windows Authentication (if required)
	- Logging
- Install .NET 8 ASP.NET Core Hosting Bundle

PowerShell (as Administrator):
```
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole -All -NoRestart
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServer -All -NoRestart
Enable-WindowsOptionalFeature -Online -FeatureName IIS-CommonHttpFeatures -All -NoRestart
Enable-WindowsOptionalFeature -Online -FeatureName IIS-HttpErrors -All -NoRestart
Enable-WindowsOptionalFeature -Online -FeatureName IIS-HttpLogging -All -NoRestart
Enable-WindowsOptionalFeature -Online -FeatureName IIS-Security -All -NoRestart
Enable-WindowsOptionalFeature -Online -FeatureName IIS-RequestFiltering -All -NoRestart
Enable-WindowsOptionalFeature -Online -FeatureName IIS-StaticContent -All -NoRestart
Enable-WindowsOptionalFeature -Online -FeatureName IIS-DefaultDocument -All -NoRestart
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WindowsAuthentication -All -NoRestart
```

2) Create App Pool
------------------
- Name: `AppManagerPool`
- .NET CLR version: No Managed Code
- Managed pipeline: Integrated
- Identity: ApplicationPoolIdentity

PowerShell:
```
Import-Module WebAdministration
if (Get-IISAppPool -Name "AppManagerPool" -ErrorAction SilentlyContinue) { Remove-WebAppPool -Name "AppManagerPool" }
New-WebAppPool -Name "AppManagerPool"
Set-ItemProperty "IIS:\AppPools\AppManagerPool" -Name managedRuntimeVersion -Value ""
Set-ItemProperty "IIS:\AppPools\AppManagerPool" -Name processModel.identityType -Value ApplicationPoolIdentity
```

3) Create Site
--------------
- Name: `AppManager`
- Physical Path: `C:\inetpub\wwwroot\AppManager` (or your path)
- Bindings: `http :80` (and `https :443` if you have a certificate)

PowerShell:
```
if (Get-Website -Name "AppManager" -ErrorAction SilentlyContinue) { Remove-Website -Name "AppManager" }
New-Website -Name "AppManager" -Port 80 -PhysicalPath "C:\inetpub\wwwroot\AppManager" -ApplicationPool "AppManagerPool"
```

4) Folder permissions
---------------------
Grant Modify rights to the app pool identity on the site folder (and the SQLite DB path if located elsewhere):
```
$path = "C:\inetpub\wwwroot\AppManager"
$acl = Get-Acl $path
$rule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS AppPool\AppManagerPool","Modify","ContainerInherit,ObjectInherit","None","Allow")
$acl.SetAccessRule($rule)
Set-Acl $path $acl
```

5) Windows Authentication
-------------------------
- In IIS Manager → Site → Authentication:
	- Enable Windows Authentication (if required by your org)
	- Disable Anonymous Authentication to protect all pages, OR leave it enabled and enforce authorization in the app for `/Admin` area

6) HTTPS (optional but recommended)
-----------------------------------
- Bind a certificate to the site (`:443`)
- Optionally set `EnforceHttpsRedirect=true` in `appsettings.Production.json` or handle redirects in IIS URL Rewrite

7) Environment variables
------------------------
Set environment variables for the application pool if you want to override configuration:
```
[Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT","Production","Machine")
[Environment]::SetEnvironmentVariable("ConnectionStrings__DefaultConnection","Data Source=C:\inetpub\wwwroot\AppManager\local.db","Machine")
```

8) Recycle/Start
----------------
```
Restart-WebAppPool -Name "AppManagerPool"
Restart-WebSite -Name "AppManager"
```

Troubleshooting
---------------
- Use `scripts\Fix-IIS-ConfigCheck.ps1` if shared configuration issues arise
- Check Windows Event Viewer → Application and IIS logs
- For ANCM errors (500.31 etc.), confirm Hosting Bundle and x64 runtime installed
- Verify file permissions for SQLite database if activity history isn’t saved
