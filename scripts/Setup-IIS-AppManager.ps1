<#
.SYNOPSIS
  Automates IIS setup for AppManager publish folder.

.DESCRIPTION
  Creates/updates an IIS Application Pool and Website pointing to a self-contained publish folder.
  - Enables required IIS features (if available)
  - Checks for ASP.NET Core Module (Hosting Bundle)
  - Creates App Pool (default LocalSystem for CPU counters)
  - Creates Website with HTTP binding
  - Configures Authentication (Anonymous on by default; Windows optional)
  - Grants NTFS permissions (IIS_IUSRS + AppPool identity)
  - Creates logs folder
  - Adds optional Windows Firewall rule
  - Warms up the site and prints status

.PARAMETER PublishPath
  Absolute path to the self-contained publish folder (contains AppManager.exe and web.config)

.PARAMETER SiteName
  IIS Website name (default: AppManager)

.PARAMETER AppPoolName
  IIS Application Pool name (default: AppManagerPool)

.PARAMETER Port
  HTTP port for binding (default: 8082)

.PARAMETER AppPoolIdentity
  Identity for the App Pool: LocalSystem | ApplicationPoolIdentity | NetworkService | LocalService | Custom (default: LocalSystem)

.PARAMETER CustomCredential
  PSCredential used only when AppPoolIdentity=Custom

.PARAMETER EnableWindowsAuth
  Enables Windows Authentication (default: false). Anonymous stays enabled regardless.

.PARAMETER CreateFirewallRule
  Creates an inbound Windows Firewall rule for the selected port (default: true)

.EXAMPLE
  .\scripts\Setup-IIS-AppManager.ps1 -PublishPath "C:\Users\silav\Desktop\Appmanager_2" -Port 8082

#>
[CmdletBinding(SupportsShouldProcess=$true)]
param(
  [Parameter(Mandatory=$true)]
  [ValidateScript({ Test-Path $_ -PathType Container })]
  [string]$PublishPath,

  [string]$SiteName = 'AppManager',
  [string]$AppPoolName = 'AppManagerPool',
  [ValidateRange(1,65535)]
  [int]$Port = 8082,
  [ValidateSet('LocalSystem','ApplicationPoolIdentity','NetworkService','LocalService','Custom')]
  [string]$AppPoolIdentity = 'LocalSystem',
  [pscredential]$CustomCredential,
  [switch]$EnableWindowsAuth,
  [switch]$WindowsOnly,
  [switch]$CreateFirewallRule = $true
)

function Assert-Admin {
  if (-not ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Dieses Skript muss als Administrator ausgeführt werden.'
  }
}

function Ensure-IISFeatures {
  Write-Host '📦 Prüfe/aktiviere IIS-Features...' -ForegroundColor Yellow
  $features = @(
    'IIS-WebServerRole',
    'IIS-WebServer',
    'IIS-CommonHttpFeatures',
    'IIS-StaticContent',
    'IIS-DefaultDocument',
    'IIS-HttpErrors',
    'IIS-HttpLogging',
    'IIS-RequestFiltering',
    'IIS-ManagementConsole'
  )
  foreach ($f in $features) {
    try {
      Enable-WindowsOptionalFeature -Online -FeatureName $f -All -NoRestart -ErrorAction Stop | Out-Null
      Write-Host ("   ✅ {0}" -f $f) -ForegroundColor Green
    } catch {
      Write-Host ("   ℹ️ {0} bereits aktiv oder nicht verfügbar" -f $f) -ForegroundColor DarkYellow
    }
  }
}

function Check-AspNetCoreModuleV2 {
  Write-Host '🔍 Prüfe ASP.NET Core IIS Hosting Modul (V2)...' -ForegroundColor Yellow
  $dllPath = Join-Path $env:ProgramFiles 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
  if (-not (Test-Path $dllPath)) {
    Write-Warning 'ASP.NET Core Hosting Bundle nicht gefunden. Für IIS in-process Hosting wird das AspNetCoreModuleV2 benötigt.'
    Write-Host 'Download: https://dotnet.microsoft.com/download/dotnet/8.0' -ForegroundColor Cyan
  } else {
    Write-Host '   ✅ AspNetCoreModuleV2 gefunden' -ForegroundColor Green
  }
}

function New-OrReplace-AppPool {
  param(
    [string]$Name,
    [string]$Identity,
    [pscredential]$Cred
  )
  Import-Module WebAdministration -ErrorAction Stop
  if (Get-IISAppPool -Name $Name -ErrorAction SilentlyContinue) {
    Write-Host ("   ⚠️ AppPool '{0}' existiert – wird neu erstellt" -f $Name) -ForegroundColor Yellow
    Stop-WebAppPool -Name $Name -ErrorAction SilentlyContinue
    Remove-WebAppPool -Name $Name -ErrorAction SilentlyContinue
  }
  New-WebAppPool -Name $Name | Out-Null

  # .NET Core: No managed CLR
  Set-ItemProperty "IIS:\AppPools\$Name" -Name managedRuntimeVersion -Value ''
  Set-ItemProperty "IIS:\AppPools\$Name" -Name startMode -Value 'AlwaysRunning'
  Set-ItemProperty "IIS:\AppPools\$Name" -Name 'processModel.loadUserProfile' -Value $true
  # Keep-alive: disable idle timeout and time-based recycle
  Set-ItemProperty "IIS:\AppPools\$Name" -Name 'processModel.idleTimeout' -Value '00:00:00'
  Set-ItemProperty "IIS:\AppPools\$Name" -Name 'recycling.periodicRestart.time' -Value '00:00:00'

  switch ($Identity) {
    'LocalSystem' { Set-ItemProperty "IIS:\AppPools\$Name" -Name 'processModel.identityType' -Value 'LocalSystem' }
    'ApplicationPoolIdentity' { Set-ItemProperty "IIS:\AppPools\$Name" -Name 'processModel.identityType' -Value 'ApplicationPoolIdentity' }
    'NetworkService' { Set-ItemProperty "IIS:\AppPools\$Name" -Name 'processModel.identityType' -Value 'NetworkService' }
    'LocalService' { Set-ItemProperty "IIS:\AppPools\$Name" -Name 'processModel.identityType' -Value 'LocalService' }
    'Custom' {
      if (-not $Cred) { throw 'CustomCredential wird benötigt, wenn AppPoolIdentity=Custom.' }
      Set-ItemProperty "IIS:\AppPools\$Name" -Name 'processModel.identityType' -Value 'SpecificUser'
      Set-ItemProperty "IIS:\AppPools\$Name" -Name 'processModel.userName' -Value $Cred.UserName
      $plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($Cred.Password))
      Set-ItemProperty "IIS:\AppPools\$Name" -Name 'processModel.password' -Value $plain
    }
  }
  Write-Host ("   ✅ AppPool '{0}' angelegt (Identity: {1})" -f $Name, $Identity) -ForegroundColor Green
}

function New-OrReplace-Website {
  param(
    [string]$Name,
    [string]$Path,
    [string]$Pool,
    [int]$Port
  )
  Import-Module WebAdministration -ErrorAction Stop

  # Check for binding conflict
  $bindingConflict = (Get-WebBinding | Where-Object { $_.bindingInformation -match ":$Port:" })
  if ($bindingConflict) {
    Write-Warning ("Ein anderes Site-Binding nutzt bereits Port {0}. Der Start kann fehlschlagen." -f $Port)
  }

  if (Get-Website -Name $Name -ErrorAction SilentlyContinue) {
    Write-Host ("   ⚠️ Website '{0}' existiert – wird neu erstellt" -f $Name) -ForegroundColor Yellow
    Stop-Website -Name $Name -ErrorAction SilentlyContinue
    Remove-Website -Name $Name -ErrorAction SilentlyContinue
  }
  New-Website -Name $Name -PhysicalPath $Path -ApplicationPool $Pool -Port $Port -Protocol 'http' | Out-Null
  Write-Host ("   ✅ Website '{0}' angelegt (http://localhost:{1})" -f $Name, $Port) -ForegroundColor Green
}

function Set-Auth {
  param(
    [string]$Site,
    [bool]$WinAuth,
    [bool]$AnonEnabled
  )
  Import-Module WebAdministration -ErrorAction Stop
  # Anonymous ON
  Set-WebConfigurationProperty -Filter '/system.webServer/security/authentication/anonymousAuthentication' -Name enabled -Value $AnonEnabled -PSPath 'IIS:\' -Location $Site
  # Windows optional
    Set-WebConfigurationProperty -Filter '/system.webServer/security/authentication/windowsAuthentication' -Name enabled -Value ($WinAuth) -PSPath 'IIS:\' -Location $Site
  $anonText = $AnonEnabled ? 'ON' : 'OFF'
  Write-Host ("   ✅ Authentication: Anonymous={0}, Windows={1}" -f $anonText, ($WinAuth ? 'ON' : 'OFF')) -ForegroundColor Green
}

function Grant-NTFSPermissions {
  param(
    [string]$Path,
    [string]$PoolName
  )
  Write-Host '🔑 Setze NTFS-Berechtigungen...' -ForegroundColor Yellow
  $acl = Get-Acl -Path $Path
  $rules = @()
  $rules += New-Object System.Security.AccessControl.FileSystemAccessRule('IIS_IUSRS','Modify','ContainerInherit, ObjectInherit','None','Allow')
  $rules += New-Object System.Security.AccessControl.FileSystemAccessRule('IUSR','Modify','ContainerInherit, ObjectInherit','None','Allow')
    $rules += New-Object System.Security.AccessControl.FileSystemAccessRule("IIS AppPool\\$PoolName","FullControl","ContainerInherit, ObjectInherit","None","Allow")
  foreach ($r in $rules) { $acl.AddAccessRule($r) | Out-Null }
  Set-Acl -Path $Path -AclObject $acl
  Write-Host '   ✅ Berechtigungen gesetzt (IIS_IUSRS, IUSR, AppPool)' -ForegroundColor Green
}

function Ensure-LogsFolder {
  param([string]$Path)
  $logs = Join-Path $Path 'logs'
  if (-not (Test-Path $logs)) {
    New-Item -ItemType Directory -Path $logs -Force | Out-Null
    Write-Host "   📁 logs/ erstellt" -ForegroundColor Green
  }
}

function Add-FirewallRuleIfNeeded {
  param([int]$Port)
  try {
    $name = "AppManager HTTP ($Port)"
    if (-not (Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue)) {
      New-NetFirewallRule -DisplayName $name -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
      Write-Host "   ✅ Firewall-Regel erstellt: $name" -ForegroundColor Green
    } else {
      Write-Host "   ℹ️ Firewall-Regel existiert bereits: $name" -ForegroundColor DarkYellow
    }
  } catch {
    Write-Warning "Firewall-Regel konnte nicht erstellt werden: $($_.Exception.Message)"
  }
}

try {
  Write-Host "🚀 IIS-Setup für AppManager startet..." -ForegroundColor Green
  Write-Host "   Site: $SiteName | AppPool: $AppPoolName | Port: $Port" -ForegroundColor Cyan
  Write-Host "   Pfad: $PublishPath" -ForegroundColor Cyan

  Assert-Admin
  Ensure-IISFeatures
  Check-AspNetCoreModuleV2

  # Ensure publish folder exists and has a web.config
  if (-not (Test-Path (Join-Path $PublishPath 'web.config'))) {
    Write-Warning 'web.config im Publish-Ordner nicht gefunden. Für IIS in-process Hosting erforderlich.'
  }
  Ensure-LogsFolder -Path $PublishPath

  New-OrReplace-AppPool -Name $AppPoolName -Identity $AppPoolIdentity -Cred $CustomCredential
  New-OrReplace-Website -Name $SiteName -Path $PublishPath -Pool $AppPoolName -Port $Port
  $anonEnabled = $true
  if ($WindowsOnly) { $anonEnabled = $false }
  Set-Auth -Site $SiteName -WinAuth ([bool]$EnableWindowsAuth) -AnonEnabled $anonEnabled
  Grant-NTFSPermissions -Path $PublishPath -PoolName $AppPoolName

  if ($CreateFirewallRule) { Add-FirewallRuleIfNeeded -Port $Port }

  Start-WebAppPool -Name $AppPoolName | Out-Null
  Start-Website -Name $SiteName | Out-Null

  # Warm-up
  $url = "http://localhost:$Port/"
  Write-Host "🌐 Starte Warm-up: $url" -ForegroundColor Yellow
  try {
    $resp = Invoke-WebRequest -UseBasicParsing -Uri $url -Method GET -TimeoutSec 15
    Write-Host ("   ✅ Antwort: {0}" -f $resp.StatusCode) -ForegroundColor Green
  } catch {
    Write-Warning ("Warm-up fehlgeschlagen: {0}" -f $_.Exception.Message)
  }

  Write-Host ''
  Write-Host '🎉 IIS-Setup abgeschlossen.' -ForegroundColor Green
  Write-Host ("➡ URL: {0}" -f $url) -ForegroundColor White
  Write-Host ("➡ AppPool Identity: {0}" -f $AppPoolIdentity) -ForegroundColor White
}
catch {
  Write-Error "❌ Fehler im Setup: $($_.Exception.Message)"
  if ($_.ScriptStackTrace) { Write-Host $_.ScriptStackTrace -ForegroundColor Red }
  exit 1
}
