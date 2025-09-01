# AppManager Server Installation Script
# Automatisiert die komplette IIS-Setup

param(
    [string]$SiteName = "AppManager",
    [string]$AppPoolName = "AppManagerPool", 
    [string]$Port = "80",
    [string]$SitePath = "C:\inetpub\wwwroot\AppManager"
)

Write-Host "🚀 AppManager Server Installation gestartet..." -ForegroundColor Green

# 1. Prüfen ob als Administrator ausgeführt
if (-NOT ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Error "❌ Dieses Script muss als Administrator ausgeführt werden!"
    exit 1
}

# 2. IIS Features aktivieren
Write-Host "📦 Aktiviere IIS Features..." -ForegroundColor Yellow
try {
    Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole -All -NoRestart
    Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServer -All -NoRestart
    Enable-WindowsOptionalFeature -Online -FeatureName IIS-CommonHttpFeatures -All -NoRestart
    Enable-WindowsOptionalFeature -Online -FeatureName IIS-HttpErrors -All -NoRestart
    Enable-WindowsOptionalFeature -Online -FeatureName IIS-HttpLogging -All -NoRestart
    Enable-WindowsOptionalFeature -Online -FeatureName IIS-Security -All -NoRestart
    Enable-WindowsOptionalFeature -Online -FeatureName IIS-RequestFiltering -All -NoRestart
    Enable-WindowsOptionalFeature -Online -FeatureName IIS-StaticContent -All -NoRestart
    Enable-WindowsOptionalFeature -Online -FeatureName IIS-DefaultDocument -All -NoRestart
    Enable-WindowsOptionalFeature -Online -FeatureName IIS-DirectoryBrowsing -All -NoRestart
    Write-Host "✅ IIS Features aktiviert" -ForegroundColor Green
}
catch {
    Write-Error "❌ Fehler beim Aktivieren der IIS Features: $($_.Exception.Message)"
    exit 1
}

# 3. Prüfen ob ASP.NET Core Runtime installiert ist
Write-Host "🔍 Prüfe ASP.NET Core Runtime..." -ForegroundColor Yellow
$dotnetVersion = & dotnet --version 2>$null
if ($dotnetVersion) {
    Write-Host "✅ .NET Runtime gefunden: $dotnetVersion" -ForegroundColor Green
}
else {
    Write-Warning "⚠️ .NET 8.0 Runtime nicht gefunden!"
    Write-Host "📥 Bitte installieren Sie das ASP.NET Core Hosting Bundle von:" -ForegroundColor Yellow
    Write-Host "https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
    Write-Host "→ 'ASP.NET Core Runtime 8.0.x - Windows Hosting Bundle'" -ForegroundColor Cyan
    
    $response = Read-Host "Möchten Sie trotzdem fortfahren? (j/n)"
    if ($response -ne "j" -and $response -ne "y") {
        exit 1
    }
}

# 4. Application Pool erstellen
Write-Host "🏊 Erstelle Application Pool: $AppPoolName..." -ForegroundColor Yellow
try {
    Import-Module WebAdministration
    
    # Prüfen ob Pool bereits existiert
    if (Get-IISAppPool -Name $AppPoolName -ErrorAction SilentlyContinue) {
        Write-Host "ℹ️ Application Pool existiert bereits, wird neu konfiguriert..." -ForegroundColor Blue
        Remove-WebAppPool -Name $AppPoolName
    }
    
    # Neuen Pool erstellen
    New-WebAppPool -Name $AppPoolName
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name "managedRuntimeVersion" -Value ""
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name "processModel.identityType" -Value "ApplicationPoolIdentity"
    Set-ItemProperty -Path "IIS:\AppPools\$AppPoolName" -Name "recycling.periodicRestart.time" -Value "00:00:00"
    
    Write-Host "✅ Application Pool erstellt" -ForegroundColor Green
}
catch {
    Write-Error "❌ Fehler beim Erstellen des Application Pools: $($_.Exception.Message)"
    exit 1
}

# 5. Website erstellen
Write-Host "🌐 Erstelle Website: $SiteName..." -ForegroundColor Yellow
try {
    # Prüfen ob Site bereits existiert
    if (Get-Website -Name $SiteName -ErrorAction SilentlyContinue) {
        Write-Host "ℹ️ Website existiert bereits, wird entfernt..." -ForegroundColor Blue
        Remove-Website -Name $SiteName
    }
    
    # Website erstellen
    New-Website -Name $SiteName -Port $Port -PhysicalPath $SitePath -ApplicationPool $AppPoolName
    
    Write-Host "✅ Website erstellt" -ForegroundColor Green
}
catch {
    Write-Error "❌ Fehler beim Erstellen der Website: $($_.Exception.Message)"
    exit 1
}

# 6. Ordner-Berechtigungen setzen
Write-Host "🔐 Setze Ordner-Berechtigungen..." -ForegroundColor Yellow
try {
    if (Test-Path $SitePath) {
        $acl = Get-Acl $SitePath
        $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS AppPool\$AppPoolName", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
        $acl.SetAccessRule($accessRule)
        Set-Acl $SitePath $acl
        
        Write-Host "✅ Berechtigungen gesetzt" -ForegroundColor Green
    }
    else {
        Write-Warning "⚠️ Zielordner $SitePath existiert nicht!"
        Write-Host "📁 Erstelle Ordner..." -ForegroundColor Yellow
        New-Item -ItemType Directory -Path $SitePath -Force
        
        $acl = Get-Acl $SitePath
        $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule("IIS AppPool\$AppPoolName", "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")
        $acl.SetAccessRule($accessRule)
        Set-Acl $SitePath $acl
        
        Write-Host "✅ Ordner erstellt und Berechtigungen gesetzt" -ForegroundColor Green
    }
}
catch {
    Write-Error "❌ Fehler beim Setzen der Berechtigungen: $($_.Exception.Message)"
    exit 1
}

# 7. Firewall-Regeln
Write-Host "🔥 Konfiguriere Firewall..." -ForegroundColor Yellow
try {
    # HTTP Port
    $httpRule = Get-NetFirewallRule -DisplayName "AppManager HTTP" -ErrorAction SilentlyContinue
    if (-not $httpRule) {
        New-NetFirewallRule -DisplayName "AppManager HTTP" -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow
        Write-Host "✅ Firewall-Regel für HTTP (Port $Port) erstellt" -ForegroundColor Green
    }
    else {
        Write-Host "ℹ️ Firewall-Regel für HTTP existiert bereits" -ForegroundColor Blue
    }
    
    # HTTPS Port (optional)
    $httpsRule = Get-NetFirewallRule -DisplayName "AppManager HTTPS" -ErrorAction SilentlyContinue
    if (-not $httpsRule) {
        New-NetFirewallRule -DisplayName "AppManager HTTPS" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow
        Write-Host "✅ Firewall-Regel für HTTPS (Port 443) erstellt" -ForegroundColor Green
    }
    else {
        Write-Host "ℹ️ Firewall-Regel für HTTPS existiert bereits" -ForegroundColor Blue
    }
}
catch {
    Write-Warning "⚠️ Firewall-Konfiguration fehlgeschlagen: $($_.Exception.Message)"
}

# 8. Installation abgeschlossen
Write-Host ""
Write-Host "🎉 AppManager Server Installation abgeschlossen!" -ForegroundColor Green
Write-Host ""
Write-Host "📋 Zusammenfassung:" -ForegroundColor Cyan
Write-Host "   Website:          $SiteName" -ForegroundColor White
Write-Host "   Application Pool: $AppPoolName" -ForegroundColor White
Write-Host "   Pfad:             $SitePath" -ForegroundColor White
Write-Host "   Port:             $Port" -ForegroundColor White
Write-Host "   URL:              http://localhost:$Port" -ForegroundColor White
Write-Host ""
Write-Host "📝 Nächste Schritte:" -ForegroundColor Yellow
Write-Host "   1. AppManager Dateien nach $SitePath kopieren" -ForegroundColor White
Write-Host "   2. appsettings.Production.json anpassen" -ForegroundColor White
Write-Host "   3. SQL Server Datenbank erstellen" -ForegroundColor White
Write-Host "   4. Website testen: http://localhost:$Port" -ForegroundColor White
Write-Host ""
Write-Host "💡 Tipp: Verwenden Sie 'iisreset' um IIS neu zu starten falls nötig" -ForegroundColor Blue
