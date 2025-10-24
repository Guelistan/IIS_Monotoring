# AppManager IIS Setup Script
# Führt das komplette Setup für die AppManager-Anwendung auf IIS durch

param(
    [string]$SiteName = "AppManager",
    [string]$AppPoolName = "AppManagerPool",
    [string]$PhysicalPath = "C:\Users\silav\Desktop\iismanager",
    [int]$Port = 80,
    [string]$Protocol = "http",
    [switch]$EnableHttps = $false,
    [int]$HttpsPort = 443
)

# Prüfen ob Administrator
if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Error "❌ Dieses Skript muss als Administrator ausgeführt werden!"
    exit 1
}

Write-Host "🚀 AppManager IIS Setup wird gestartet..." -ForegroundColor Green
Write-Host "   Site Name: $SiteName" -ForegroundColor Cyan
Write-Host "   App Pool: $AppPoolName" -ForegroundColor Cyan
Write-Host "   Pfad: $PhysicalPath" -ForegroundColor Cyan
Write-Host "   Port: $Port" -ForegroundColor Cyan

try {
    # IIS-Features aktivieren
    Write-Host "📦 IIS-Features werden aktiviert..." -ForegroundColor Yellow
    
    $features = @(
        "IIS-WebServerRole",
        "IIS-WebServer",
        "IIS-CommonHttpFeatures",
        "IIS-HttpErrors",
        "IIS-HttpLogging",
        "IIS-Security",
        "IIS-WindowsAuthentication",
        "IIS-RequestFiltering",
        "IIS-NetFxExtensibility45",
        "IIS-ISAPIExtensions",
        "IIS-ISAPIFilter",
        "IIS-AspNet45",
        "IIS-ASPNET45"
    )
    
    foreach ($feature in $features) {
        try {
            Enable-WindowsOptionalFeature -Online -FeatureName $feature -All -NoRestart
            Write-Host "   ✅ $feature aktiviert" -ForegroundColor Green
        }
        catch {
            Write-Host "   ⚠️ $feature bereits aktiviert oder nicht verfügbar" -ForegroundColor Yellow
        }
    }

    # ASP.NET Core Hosting Bundle prüfen
    Write-Host "🔍 ASP.NET Core Hosting Bundle wird geprüft..." -ForegroundColor Yellow
    $hostingBundle = Get-ItemProperty "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Updates\.NET Core*" -ErrorAction SilentlyContinue
    if (-not $hostingBundle) {
        Write-Host "⚠️ ASP.NET Core Hosting Bundle nicht gefunden!" -ForegroundColor Yellow
        Write-Host "   Bitte herunterladen von: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
        Write-Host "   Nach der Installation IIS neu starten!" -ForegroundColor Red
    } else {
        Write-Host "   ✅ ASP.NET Core Hosting Bundle gefunden" -ForegroundColor Green
    }

    # WebAdministration Modul laden
    Import-Module WebAdministration -ErrorAction Stop
    Write-Host "✅ WebAdministration Modul geladen" -ForegroundColor Green

    # Application Pool erstellen
    Write-Host "🏊 Application Pool wird erstellt..." -ForegroundColor Yellow
    if (Get-IISAppPool -Name $AppPoolName -ErrorAction SilentlyContinue) {
        Write-Host "   ⚠️ AppPool '$AppPoolName' existiert bereits, wird neu konfiguriert..." -ForegroundColor Yellow
        Remove-WebAppPool -Name $AppPoolName -ErrorAction SilentlyContinue
    }
    
    New-WebAppPool -Name $AppPoolName
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name "managedRuntimeVersion" -Value ""
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name "startMode" -Value "AlwaysRunning"
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name "processModel.identityType" -Value "ApplicationPoolIdentity"
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name "processModel.idleTimeout" -Value "00:00:00"
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name "recycling.periodicRestart.time" -Value "00:00:00"
    
    Write-Host "   ✅ Application Pool '$AppPoolName' erstellt und konfiguriert" -ForegroundColor Green

    # Verzeichnis erstellen
    Write-Host "📁 Verzeichnisse werden erstellt..." -ForegroundColor Yellow
    if (-not (Test-Path $PhysicalPath)) {
        New-Item -ItemType Directory -Path $PhysicalPath -Force
        Write-Host "   ✅ Verzeichnis '$PhysicalPath' erstellt" -ForegroundColor Green
    } else {
        Write-Host "   ℹ️ Verzeichnis '$PhysicalPath' existiert bereits" -ForegroundColor Cyan
    }

    # Unterverzeichnisse erstellen
    $subDirs = @("logs", "App_Data", "wwwroot")
    foreach ($subDir in $subDirs) {
        $fullPath = Join-Path $PhysicalPath $subDir
        if (-not (Test-Path $fullPath)) {
            New-Item -ItemType Directory -Path $fullPath -Force
            Write-Host "   ✅ Unterverzeichnis '$subDir' erstellt" -ForegroundColor Green
        }
    }

    # Website erstellen
    Write-Host "🌐 Website wird erstellt..." -ForegroundColor Yellow
    if (Get-Website -Name $SiteName -ErrorAction SilentlyContinue) {
        Write-Host "   ⚠️ Website '$SiteName' existiert bereits, wird entfernt..." -ForegroundColor Yellow
        Remove-Website -Name $SiteName
    }
    
    New-Website -Name $SiteName -PhysicalPath $PhysicalPath -ApplicationPool $AppPoolName -Port $Port
    Write-Host "   ✅ Website '$SiteName' erstellt" -ForegroundColor Green

    # HTTPS Binding hinzufügen (falls gewünscht)
    if ($EnableHttps) {
        Write-Host "🔒 HTTPS Binding wird hinzugefügt..." -ForegroundColor Yellow
        New-WebBinding -Name $SiteName -Protocol "https" -Port $HttpsPort
        Write-Host "   ✅ HTTPS Binding auf Port $HttpsPort hinzugefügt" -ForegroundColor Green
        Write-Host "   ⚠️ SSL-Zertifikat muss manuell konfiguriert werden!" -ForegroundColor Yellow
    }

    # Windows Authentication aktivieren
    Write-Host "🔐 Windows Authentication wird aktiviert..." -ForegroundColor Yellow
    Set-WebConfigurationProperty -Filter "/system.webServer/security/authentication/windowsAuthentication" -Name "enabled" -Value "True" -PSPath "IIS:\" -Location "$SiteName"
    Set-WebConfigurationProperty -Filter "/system.webServer/security/authentication/anonymousAuthentication" -Name "enabled" -Value "False" -PSPath "IIS:\" -Location "$SiteName"
    Write-Host "   ✅ Windows Authentication aktiviert" -ForegroundColor Green

    # NTFS-Berechtigungen setzen
    Write-Host "🔑 NTFS-Berechtigungen werden gesetzt..." -ForegroundColor Yellow
    
    # IIS_IUSRS Vollzugriff
    $acl = Get-Acl $PhysicalPath
    $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS_IUSRS", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($accessRule)
    Set-Acl -Path $PhysicalPath -AclObject $acl
    Write-Host "   ✅ IIS_IUSRS Vollzugriff gesetzt" -ForegroundColor Green
    
    # Application Pool Identity Vollzugriff
    $appPoolIdentity = "IIS AppPool\$AppPoolName"
    $accessRule2 = New-Object System.Security.AccessControl.FileSystemAccessRule($appPoolIdentity, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
    $acl.SetAccessRule($accessRule2)
    Set-Acl -Path $PhysicalPath -AclObject $acl
    Write-Host "   ✅ Application Pool Identity Vollzugriff gesetzt" -ForegroundColor Green

    # Request Filtering konfigurieren
    Write-Host "📊 Request Filtering wird konfiguriert..." -ForegroundColor Yellow
    Set-WebConfigurationProperty -Filter "/system.webServer/security/requestFiltering/requestLimits" -Name "maxAllowedContentLength" -Value 52428800 -PSPath "IIS:\" -Location "$SiteName"
    Write-Host "   ✅ Max Request Size auf 50MB gesetzt" -ForegroundColor Green

    # Application Pool starten
    Write-Host "▶️ Application Pool wird gestartet..." -ForegroundColor Yellow
    Start-WebAppPool -Name $AppPoolName
    Write-Host "   ✅ Application Pool '$AppPoolName' gestartet" -ForegroundColor Green

    # Website starten
    Write-Host "🌐 Website wird gestartet..." -ForegroundColor Yellow
    Start-Website -Name $SiteName
    Write-Host "   ✅ Website '$SiteName' gestartet" -ForegroundColor Green

    # Abschluss
    Write-Host ""
    Write-Host "🎉 AppManager IIS Setup erfolgreich abgeschlossen!" -ForegroundColor Green
    Write-Host ""
    Write-Host "📋 Zusammenfassung:" -ForegroundColor Cyan
    Write-Host "   Website: $SiteName" -ForegroundColor White
    Write-Host "   URL: http://localhost:$Port" -ForegroundColor White
    if ($EnableHttps) {
        Write-Host "   HTTPS URL: https://localhost:$HttpsPort" -ForegroundColor White
    }
    Write-Host "   App Pool: $AppPoolName" -ForegroundColor White
    Write-Host "   Pfad: $PhysicalPath" -ForegroundColor White
    Write-Host ""
    Write-Host "📝 Nächste Schritte:" -ForegroundColor Yellow
    Write-Host "   1. AppManager mit 'dotnet publish -p:PublishProfile=IIS-Production' veröffentlichen"
    Write-Host "   2. Bei Bedarf SSL-Zertifikat für HTTPS konfigurieren"
    Write-Host "   3. Firewall-Regeln für Port $Port (und $HttpsPort bei HTTPS) erstellen"
    Write-Host "   4. DNS/Host-Einträge konfigurieren falls nötig"
    Write-Host ""

} catch {
    Write-Error "❌ Fehler beim IIS Setup: $_"
    Write-Host "Stacktrace:" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor Red
    exit 1
}

Write-Host "✅ Setup abgeschlossen! AppManager ist bereit für das Deployment." -ForegroundColor Green