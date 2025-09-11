Param(
    [switch]$NoPause
)

Write-Host "🔍 IIS Konfiguration – 'redirection.config' Diagnose" -ForegroundColor Cyan
Write-Host "====================================================" -ForegroundColor Cyan

# 0) Admin Check
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "❌ Dieses Skript sollte als Administrator ausgeführt werden." -ForegroundColor Red
    Write-Host "   Rechtsklick → 'Als Administrator ausführen'" -ForegroundColor Yellow
}

# 1) OS + IIS Feature Check
if ($IsWindows) {
    try {
        $feature = Get-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole -ErrorAction Stop
        Write-Host "📦 IIS-Webserver Rolle: $($feature.State)" -ForegroundColor White
        if ($feature.State -ne 'Enabled') {
            Write-Host "   ❗ IIS ist nicht aktiviert. Aktivieren über 'Windows-Features' erforderlich." -ForegroundColor Yellow
        } else {
            Write-Host "   ✅ IIS ist aktiviert" -ForegroundColor Green
        }
    } catch {
        Write-Host "⚠️ Konnte IIS-Featurestatus nicht abrufen: $($_.Exception.Message)" -ForegroundColor Yellow
    }
} else {
    Write-Host "❌ IIS ist nur unter Windows verfügbar." -ForegroundColor Red
}

# 2) Pfade
$configDir = Join-Path $env:WINDIR 'System32\inetsrv\config'
$appHost = Join-Path $configDir 'applicationHost.config'
$redirection = Join-Path $configDir 'redirection.config'

Write-Host "`n📁 Konfigurationsordner: $configDir" -ForegroundColor White
if (-not (Test-Path $configDir)) {
    Write-Host "   ❌ Ordner nicht gefunden – IIS vermutlich nicht installiert." -ForegroundColor Red
    if (-not $NoPause) { Read-Host "Enter zum Beenden" } ; exit 1
}

# 3) applicationHost.config – Lesetest
if (Test-Path $appHost) {
    try {
        $fs = [System.IO.File]::Open($appHost, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $fs.Close()
        Write-Host "   ✅ Lesezugriff auf applicationHost.config" -ForegroundColor Green
    } catch {
        Write-Host "   ❌ Kein Zugriff auf applicationHost.config: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "   💡 Als Administrator ausführen oder Berechtigungen prüfen (nicht empfohlen, systemkritisch)" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ⚠️ applicationHost.config nicht gefunden" -ForegroundColor Yellow
}

# 4) redirection.config – Existenz + Lesetest + Inhalt prüfen
if (Test-Path $redirection) {
    try {
        $fs2 = [System.IO.File]::Open($redirection, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $fs2.Close()
        Write-Host "   ✅ Lesezugriff auf redirection.config" -ForegroundColor Green
        try {
            [xml]$xml = Get-Content -Path $redirection -ErrorAction Stop
            $redirNode = $xml.configuration.Redirection.configurationRedirection
            if ($redirNode) {
                $enabled = $redirNode.enabled
                $path = $redirNode.path
                Write-Host "   🔎 Shared Configuration: enabled=$enabled; path=$path" -ForegroundColor White
                if ($enabled -eq 'true' -and [string]::IsNullOrWhiteSpace($path)) {
                    Write-Host "   ❗ Achtung: 'enabled=true', aber kein Pfad gesetzt." -ForegroundColor Yellow
                }
                if ($enabled -eq 'true' -and $path) {
                    Write-Host "   💡 Prüfen Sie, ob der Pfad erreichbar ist und passende Anmeldeinformationen im IIS-Manager konfiguriert sind." -ForegroundColor Yellow
                }
            } else {
                Write-Host "   ℹ️ Keine Redirection-Einstellungen gefunden (Shared Config vermutlich deaktiviert)." -ForegroundColor White
            }
        } catch {
            Write-Host "   ⚠️ Konnte redirection.config nicht lesen/parsen: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    } catch {
        Write-Host "   ❌ Kein Zugriff auf redirection.config: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "   💡 Hinweis: Prüfen Sie im IIS-Manager unter 'Gemeinsame/Shared Configuration' die Einstellungen." -ForegroundColor Yellow
    }
} else {
    Write-Host "   ℹ️ redirection.config nicht vorhanden (Shared Configuration deaktiviert)" -ForegroundColor White
}

Write-Host "`n✅ Diagnose abgeschlossen." -ForegroundColor Green
if (-not $NoPause) { Read-Host "Enter zum Schließen" }
