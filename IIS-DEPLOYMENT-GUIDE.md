# AppManager - IIS Deployment Guide

## 🚀 Überblick

AppManager ist eine Windows-basierte Webanwendung zur Verwaltung von Anwendungen und IIS Application Pools mit Active Directory Integration und rollenbasierter Zugriffskontrolle.

## 🔐 Rechtesystem

### Rollen-Hierarchie
- **Admin/SuperAdmin**: Vollzugriff - kann alle Apps verwalten, Benutzer anlegen, Berechtigungen vergeben
- **AppOwner**: Kann zugewiesene Apps starten/stoppen/neustarten, alle anderen Apps nur lesen
- **User**: Nur Lesezugriff - kann Status und CPU-Auslastung aller Apps sehen

### Active Directory Integration
- Automatische Benutzer-Erstellung bei Windows Authentication
- Claims Transformation für Rollenzuweisung
- Support für AD-Gruppen und Benutzer-Attribute

## 📊 Features

- **Windows Authentication**: Nahtlose Integration mit Active Directory
- **CPU & Memory Monitoring**: Echzeit-Performance-Überwachung
- **IIS Integration**: Verwaltung von Application Pools
- **Rollenbasierte Autorisierung**: Granulare Berechtigungskontrolle
- **Audit Logging**: Vollständige Nachverfolgung aller Aktionen
- **Responsive UI**: Modern Bootstrap-basierte Benutzeroberfläche

## 🛠️ Installation und Deployment

### Voraussetzungen

#### Server-Anforderungen
- Windows Server 2019/2022 oder Windows 10/11 Pro
- IIS mit ASP.NET Core Hosting Bundle
- .NET 8.0 Runtime
- SQL Server Express/Standard oder SQLite

#### Software-Downloads
1. **IIS**: Windows Feature aktivieren
2. **ASP.NET Core Hosting Bundle**: [Download](https://dotnet.microsoft.com/download/dotnet/8.0)
3. **.NET 8.0 Runtime**: [Download](https://dotnet.microsoft.com/download/dotnet/8.0)

### 🔧 Automatisches Setup

#### Schritt 1: Repository klonen
```bash
git clone https://github.com/yourusername/AppManager.git
cd AppManager
```

#### Schritt 2: IIS Setup ausführen (als Administrator)
```powershell
# Basis-Setup
.\Setup-IIS-AppManager.ps1

# Mit HTTPS Support
.\Setup-IIS-AppManager.ps1 -EnableHttps -HttpsPort 443

# Benutzerdefinierte Konfiguration
.\Setup-IIS-AppManager.ps1 -SiteName "MyAppManager" -AppPoolName "MyAppPool" -Port 8080 -PhysicalPath "C:\CustomPath\AppManager"
```

#### Schritt 3: Anwendung publishen
```bash
# IIS-optimiertes Deployment
dotnet publish -c Release -p:PublishProfile=IIS-Production

# Alternative: Self-contained für isolierte Umgebung
dotnet publish -c Release -p:PublishProfile=SelfContained-Win64
```

#### Schritt 4: Berechtigungen prüfen
Das Setup-Skript setzt automatisch die erforderlichen NTFS-Berechtigungen:
- `IIS_IUSRS`: Vollzugriff auf Anwendungsverzeichnis
- `IIS AppPool\AppManagerPool`: Vollzugriff auf Anwendungsverzeichnis

### 🔧 Manuelle Installation

#### IIS Konfiguration
1. **IIS Features aktivieren**:
   - IIS-WebServerRole
   - IIS-WindowsAuthentication
   - IIS-NetFxExtensibility45
   - IIS-ASPNET45

2. **Application Pool erstellen**:
   ```
   Name: AppManagerPool
   .NET CLR Version: No Managed Code
   Managed Pipeline Mode: Integrated
   Identity: ApplicationPoolIdentity
   Start Mode: AlwaysRunning
   Idle Timeout: 0
   ```

3. **Website erstellen**:
   ```
   Site Name: AppManager
   Physical Path: C:\inetpub\wwwroot\AppManager
   Application Pool: AppManagerPool
   Binding: http/*:80:
   ```

4. **Authentication konfigurieren**:
   - Windows Authentication: Enabled
   - Anonymous Authentication: Disabled

#### Datenbanksetup
Die Anwendung verwendet standardmäßig SQLite (local.db). Für Produktionsumgebungen SQL Server konfigurieren:

1. **appsettings.Production.json** erstellen:
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=AppManager;Trusted_Connection=true;TrustServerCertificate=true;"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

2. **Entity Framework Migrationen**:
```bash
dotnet ef database update --configuration Release
```

## 🎯 Erste Schritte nach Installation

### Admin-Benutzer konfigurieren
1. Ersten Admin-Benutzer über Windows Authentication anmelden
2. In der Datenbank `IsGlobalAdmin = true` setzen
3. Oder über Admin-Interface weitere Admins ernennen

### App-Owner Berechtigungen vergeben
1. Als Admin anmelden
2. Navigation: Admin → App-Berechtigungen
3. Benutzer auswählen und Apps zuweisen
4. IIS AppPool Namen konfigurieren (falls IIS-Apps)

### Anwendungen hinzufügen
1. Als Admin: Admin → App-Verwaltung
2. Neue Anwendung anlegen:
   - Name, Beschreibung
   - Executable Path
   - Working Directory
   - IIS Integration (falls zutreffend)
   - Tags und Kategorien

## 🔍 Monitoring und Wartung

### Performance Monitoring
- CPU-Auslastung wird automatisch überwacht
- Memory-Usage pro Prozess
- IIS Application Pool Status
- Auto-Refresh Dashboard alle 30 Sekunden

### Logging
- Anwendungs-Logs: `logs/` Verzeichnis
- IIS-Logs: Standard IIS Logging Location
- Entity Framework Migrations: Console Output
- User Action Audit: `AppLaunchHistories` Tabelle

### Backup und Wartung
```bash
# Datenbank-Backup (SQLite)
copy "C:\inetpub\wwwroot\AppManager\local.db" "C:\Backup\AppManager\local_backup_$(Get-Date -Format 'yyyyMMdd').db"

# Log-Rotation (PowerShell)
Get-ChildItem "C:\inetpub\wwwroot\AppManager\logs\*.log" -Older (Get-Date).AddDays(-30) | Remove-Item
```

## 🚨 Troubleshooting

### Häufige Probleme

#### HTTP 500.19 - Internal Server Error
**Ursache**: web.config Syntax-Fehler oder fehlende IIS-Features
**Lösung**:
1. ASP.NET Core Hosting Bundle installieren
2. IIS neu starten: `iisreset`
3. web.config Syntax prüfen

#### HTTP 401.3 - Unauthorized
**Ursache**: Windows Authentication nicht konfiguriert
**Lösung**:
1. Windows Authentication in IIS aktivieren
2. Anonymous Authentication deaktivieren
3. NTFS-Berechtigungen prüfen

#### Claims Transformation Fehler
**Ursache**: Active Directory nicht erreichbar oder Benutzer nicht gefunden
**Lösung**:
1. Domänen-Verbindung prüfen
2. Service-Account Berechtigungen prüfen
3. Fallback auf lokale Benutzer-Erstellung

#### Performance Counter Fehler
**Ursache**: Performance Counter nicht verfügbar
**Lösung**:
1. Performance Counter für Nicht-Admins aktivieren
2. Registry-Berechtigungen prüfen
3. Windows Performance Toolkit installieren

### Debug-Modus aktivieren
```json
// appsettings.Development.json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "AppManager": "Debug",
      "Microsoft": "Debug"
    }
  }
}
```

### IIS Application Pool recyceln
```powershell
# PowerShell als Administrator
Restart-WebAppPool -Name "AppManagerPool"

# Alternative: IIS Manager
# Application Pools → AppManagerPool → Recycle
```

## 📱 API Endpoints

### REST API (für Integration)
```
GET  /api/applications        - Liste aller Apps (leseberechtigt)
POST /api/applications/{id}/start   - App starten (berechtigt)
POST /api/applications/{id}/stop    - App stoppen (berechtigt)  
POST /api/applications/{id}/restart - App neustarten (berechtigt)
GET  /api/performance        - System Performance
GET  /api/users/current      - Aktueller Benutzer Info
```

### Authorization Header
```http
Authorization: Negotiate <token>
```

## 🔧 Erweiterte Konfiguration

### Custom Claims Provider
```csharp
// Services/CustomClaimsProvider.cs
public class CustomClaimsProvider : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        // Custom Claims Logic hier
        return Task.FromResult(principal);
    }
}
```

### Database Provider wechseln
```csharp
// Program.cs - SQL Server
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(connectionString));

// Program.cs - PostgreSQL  
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));
```

### Load Balancing / High Availability
- Shared Database zwischen mehreren IIS-Servern
- Redis Session State Provider
- Application Request Routing (ARR)

## 📞 Support und Beitrag

### Fehler melden
1. GitHub Issues verwenden
2. Vollständige Fehler-Logs beifügen
3. System-Konfiguration beschreiben
4. Reproduktions-Schritte angeben

### Feature Requests
1. GitHub Discussions nutzen
2. Use Case beschreiben
3. Mockups/Screenshots hilfreich

### Entwicklung
```bash
# Development Setup
git clone https://github.com/yourusername/AppManager.git
cd AppManager
dotnet restore
dotnet run --environment Development
```

## 📄 Lizenz

MIT License - siehe LICENSE.md für Details.

---

## 🎉 Herzlichen Glückwunsch!

AppManager ist nun erfolgreich installiert und einsatzbereit. Die Anwendung bietet:

✅ **Sichere Windows Authentication**  
✅ **Rollenbasierte Zugriffskontrolle**  
✅ **Real-time Performance Monitoring**  
✅ **IIS Integration**  
✅ **Active Directory Support**  
✅ **Audit Logging**  
✅ **Mobile-responsive UI**  

Viel Erfolg beim Verwalten Ihrer Anwendungen! 🚀