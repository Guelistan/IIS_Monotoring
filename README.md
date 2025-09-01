# AppManager Projekt Dokumentation

## Projektbeschreibung

ASP.NET Core Web-Anwendung zum Verwalten und Steuern von Server-Anwendungen mit Benutzerberechtigungen, Datenbank-Integration und Web-Admin-Bereich.

**Hauptfunktionen:**

- Anwendungen starten und stoppen
- Benutzerberechtigungen verwalten (App-Owner System)
- SQL Server Datenbank mit Windows-Authentifizierung
- Web-Administratorbereich mit Statusübersicht
- ASP.NET Core Identity für Authentifizierung

**📊 Diagramme:** Siehe [DIAGRAMS.md](DIAGRAMS.md) für Use Case und Klassendiagramme

## Aktuelle Technische Konfiguration

### Backend-Stack

- **Framework:** ASP.NET Core 9.0 (ohne Kestrel - Compliance)
- **Frontend:** Razor Pages
- **Datenbank:** SQL Server mit Entity Framework Core 8.0.11
- **Authentifizierung:** ASP.NET Core Identity + Windows Auth
- **ORM:** Entity Framework Core (downgrade für Stabilität)

### Server-Umgebungen

- **Development:** SQL Server LocalDB "AppManagerTest"
- **Production:** buhlertal123 Server (APPUSER Datenbank)
- **Verbindung:** Windows-Authentifizierung (Trusted_Connection=true)

## Datenbankmodelle (Implementiert)

### Applications

```csharp
- Id (Guid)
- Name, Description, ExecutablePath
- ProcessId, Arguments, WorkingDirectory  
- RequiresAdmin, Category, Tags
- IIS-Integration: AppPoolName, SiteName, IsIISApplication
- LaunchHistory Navigation
```

### AppOwnership (App-Owner System)

```csharp
- UserId, ApplicationId (Beziehungen)
- WindowsUsername (für Windows Auth)
- IISAppPoolName (für IIS-Integration)
- CreatedAt, CreatedBy
```

### AppLaunchHistory (Audit Trail)

```csharp
- ApplicationId, UserId
- WindowsUsername, IISAppPoolName
- Action, Reason, LaunchTime
```

## Wichtige Commands & Setup

### Projekt erstellen (ABGESCHLOSSEN)

```powershell
dotnet new webapp -n AppManager
cd AppManager
```

### NuGet Packages (INSTALLIERT)

```powershell
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.SqlServer oder Sqlite
dotnet add package Microsoft.EntityFrameworkCore.Tools
dotnet add package Microsoft.AspNetCore.Identity.UI
```

### SQL Server LocalDB Setup (KONFIGURIERT)

```powershell
sqllocaldb create "AppManagerTest" -s
dotnet ef migrations add "SqlServerMigration"
dotnet ef database update
```

### Build & Run

```powershell
taskkill /F /IM AppManager.exe  # Falls App läuft
dotnet build
dotnet run --launch-profile http
```

**URL:** <http://localhost:5130>

## Projektstruktur (Aktuell)

```text
AppManager/
├── Data/
│   └── AppUser.cs (DbContext + Identity)
├── Models/
│   ├── Applications.cs (App-Definitionen)
│   ├── AppOwnership.cs (User-App Berechtigungen)
│   └── AppLaunchHistory.cs (Audit-Log)
├── Services/
│   ├── AppService.cs (App-Management)
│   ├── ConsoleEmailSender.cs (Dev Email)
│   └── ProgramManagerService.cs (Process Control)
├── Pages/
│   ├── Admin/ (Dashboard, Users, History)
│   ├── Account/ (Login, Register, etc.)
│   └── Shared/ (Layout, Partials)
├── TestDataSeeder.cs (Development-Daten)
├── ProductionSeeder.cs (Standard-Apps)
└── Program.cs (Startup-Konfiguration)
```

## TestDataSeeder vs ProductionSeeder

### TestDataSeeder.cs (Nur Development)

- **Zweck:** Test- und Beispieldaten für Entwicklung
- **Inhalt:** Dummy-Apps (Paint, Rechner), Test-Ownership
- **Aktivierung:** Nur wenn `app.Environment.IsDevelopment()`
- **Produktion:** Wird automatisch übersprungen

### ProductionSeeder.cs (Alle Umgebungen)

- **Zweck:** Standard Windows-Apps für alle Server
- **Inhalt:** Explorer, Notepad, CMD
- **Aktivierung:** Development + Production
- **Produktion:** Läuft auf buhlertal123

## Status & Nächste Schritte

### Abgeschlossen

- [x] Kestrel entfernt (Compliance erfüllt)
- [x] SQL Server LocalDB Setup + Migration
- [x] App-Owner Datenmodelle implementiert
- [x] Windows-Authentifizierung konfiguriert
- [x] Test- und Produktions-Seeder erstellt
- [x] IIS-Integration Grundlagen
- [x] **📊 Diagramm-Seite**: Use Case, Klassen- und ER-Diagramme integriert

### In Arbeit

- [ ] App-Owner Management UI
- [ ] IIS App Pool Integration (echte Funktionalität)
- [ ] Admin-Dashboard erweitern

### TODO für buhlertal123 Server

- [ ] Connection String auf Produktionsserver anpassen
- [ ] Migration auf echtem SQL Server ausführen
- [ ] Active Directory Integration für User-Import
- [ ] IIS Management API Integration
- [ ] Produktions-Deployment

## Wichtige Connection Strings

### Development (Aktiv)

```json
"Server=(localdb)\\MSSQLLocalDB;Database=AppManagerTest;Trusted_Connection=true;"
```

### Production (Vorbereitet)

```json
"Server=buhlertal123;Database=APPUSER;Trusted_Connection=true;"
```

## Bekannte Probleme & Lösungen

### Build-Fehler "Process in use"

```powershell
taskkill /F /IM AppManager.exe
dotnet build
```

### Migration Rollback

```powershell
dotnet ef database update NameDerVorherigenMigration
# Dann Migration bearbeiten
dotnet ef database update
```

### Kestrel Package prüfen

```powershell
dotnet list package | findstr -i kestrel
```

## Git Repository

```powershell
git init
git add .
git commit -m "Initial commit"
git branch -M main
git remote add origin https://github.com/Guelistan/Appmanager.git
git push -u origin main
```

---

**Letzte Aktualisierung:** 23. Juli 2025  
**Status:** Development-Phase abgeschlossen, bereit für App-Owner UI
