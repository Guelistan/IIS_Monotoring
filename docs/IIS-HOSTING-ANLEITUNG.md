# AppManager IIS Hosting - Komplette Anleitung

## 🎯 Ziel
AppManager auf IIS hosten mit Pfad: `C:\Users\silav\Desktop\iismanager`

## 📋 Voraussetzungen

### 1. ASP.NET Core Hosting Bundle
```powershell
# Download und Installation von:
# https://dotnet.microsoft.com/download/dotnet/8.0
# "ASP.NET Core Runtime 8.0.x - Windows Hosting Bundle"
```

### 2. IIS Features aktivieren
```powershell
# Als Administrator ausführen:
Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole,IIS-WebServer,IIS-CommonHttpFeatures,IIS-HttpErrors,IIS-HttpLogging,IIS-Security,IIS-WindowsAuthentication,IIS-RequestFiltering,IIS-NetFxExtensibility45,IIS-ISAPIExtensions,IIS-ISAPIFilter,IIS-AspNet45,IIS-ASPNET45 -All
```

## 🚀 Deployment-Prozess

### Schritt 1: Anwendung publishen
```powershell
# Im Projektverzeichnis (C:\guelistan\Dersler\Appmanager):
dotnet publish -p:PublishProfile=IIS-Production
```
**Ergebnis:** Anwendung wird nach `C:\Users\silav\Desktop\iismanager` published

### Schritt 2: IIS automatisch konfigurieren
```powershell
# Als Administrator ausführen:
cd C:\guelistan\Dersler\Appmanager
.\Setup-IIS-AppManager.ps1
```

**Oder manuell konfigurieren (siehe unten)**

## ⚙️ IIS Manuelle Konfiguration

### 1. Application Pool erstellen
**IIS Manager → Application Pools → Add Application Pool**
- **Name:** AppManagerPool
- **.NET CLR version:** No Managed Code
- **Managed pipeline mode:** Integrated
- **Start immediately:** ✅

**Erweiterte Einstellungen:**
- **Identity:** ApplicationPoolIdentity
- **Start Mode:** AlwaysRunning
- **Idle Timeout:** 0 (deaktiviert)
- **Periodic Restart Time:** 0 (deaktiviert)

### 2. Website erstellen
**IIS Manager → Sites → Add Website**
- **Site name:** AppManager
- **Application pool:** AppManagerPool
- **Physical path:** `C:\Users\silav\Desktop\iismanager`
- **Binding:**
  - Type: http
  - Port: 80 (oder gewünschter Port)
  - Host name: (leer für alle)

### 3. Windows Authentication aktivieren
**IIS Manager → AppManager → Authentication**
- **Windows Authentication:** Enable
- **Anonymous Authentication:** Disable

### 4. NTFS-Berechtigungen setzen
```powershell
# Als Administrator ausführen:
$path = "C:\Users\silav\Desktop\iismanager"

# IIS_IUSRS Vollzugriff
icacls $path /grant "IIS_IUSRS:(OI)(CI)F" /T

# Application Pool Identity Vollzugriff
icacls $path /grant "IIS AppPool\AppManagerPool:(OI)(CI)F" /T

# Logs-Verzeichnis
icacls "$path\logs" /grant "IIS_IUSRS:(OI)(CI)F" /T
icacls "$path\logs" /grant "IIS AppPool\AppManagerPool:(OI)(CI)F" /T
```

## 🔧 Konfigurationsdateien

### web.config (automatisch erstellt)
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath="dotnet" 
                  arguments=".\AppManager.dll" 
                  stdoutLogEnabled="true" 
                  stdoutLogFile=".\logs\stdout" 
                  hostingModel="InProcess" />
      
      <!-- Windows Authentication -->
      <security>
        <authentication>
          <windowsAuthentication enabled="true" />
          <anonymousAuthentication enabled="false" />
        </authentication>
      </security>
    </system.webServer>
  </location>
</configuration>
```

### appsettings.Production.json
```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=C:\\Users\\silav\\Desktop\\iismanager\\App_Data\\production.db"
  },
  "EnforceHttpsRedirect": false
}
```

## 🧪 Testing

### 1. Application Pool prüfen
```powershell
# PowerShell als Administrator:
Import-Module WebAdministration
Get-WebAppPoolState -Name "AppManagerPool"
# Sollte "Started" anzeigen
```

### 2. Website testen
- Browser öffnen: `http://localhost` (oder dein konfigurierter Port)
- Windows Authentication sollte automatisch funktionieren
- Admin-Dashboard sollte erreichbar sein

### 3. Logs prüfen
```powershell
# Logs anzeigen:
Get-Content "C:\Users\silav\Desktop\iismanager\logs\stdout*.log" -Tail 50
```

## 🔍 Troubleshooting

### Problem: 500.19 Fehler
**Lösung:** ASP.NET Core Hosting Bundle installieren und IIS neu starten

### Problem: 401.3 Fehler (Unauthorized)
**Lösung:** Windows Authentication in IIS aktivieren

### Problem: 500.0 Fehler
**Lösung:** Logs prüfen und .NET 8.0 Runtime installieren

### Problem: Datenbankfehler
**Lösung:** NTFS-Berechtigungen für App_Data Ordner setzen

## 📞 Nächste Schritte

1. ✅ **Publish ausführen:** `dotnet publish -p:PublishProfile=IIS-Production` - **ERLEDIGT!**
2. **IIS Setup manuell:** (siehe "IIS Manuelle Konfiguration" oben)
   - Application Pool "AppManagerPool" erstellen
   - Website "AppManager" erstellen mit Pfad `C:\Users\silav\Desktop\iismanager`
   - Windows Authentication aktivieren
   - NTFS-Berechtigungen setzen
3. **Website testen:** Browser öffnen auf Port 80 (oder dein gewählter Port)
4. **Active Directory testen:** Mit Domain-Benutzer anmelden

## 🎉 Fertig!
Deine AppManager-Anwendung läuft jetzt auf IIS mit Windows Authentication!