<#
Simple automated check + minimal fix for IIS <-> AppManager.
Run this in an elevated PowerShell (Run as Administrator).
It writes a human-readable log to ./scripts/iis_check_and_fix.log and saves admin page HTML to ./scripts/admin_Page_check.html if reachable.
#>

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$log = Join-Path $ScriptDir 'iis_check_and_fix.log'
$adminHtml = Join-Path $ScriptDir 'admin_Page_check.html'

if (Test-Path $log) { Remove-Item $log -Force }
if (Test-Path $adminHtml) { Remove-Item $adminHtml -Force }

function Log { param([string]$s) $time = (Get-Date).ToString('o'); "$time`t$s" | Tee-Object -FilePath $log -Append; Write-Output $s }

Log '=== START iis_check_and_fix ==='

Log "User: $(whoami)"
Log "IsAdmin: $((([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)))"

# Website / AppPool info
try {
    Import-Module WebAdministration -ErrorAction Stop
    Log 'WebAdministration module loaded.'
} catch {
    Log "Warning: Could not load WebAdministration module: $_"
}

try {
    $site = Get-Website -Name 'AppManager' -ErrorAction Stop
    Log "Website: $($site.Name) State=$($site.State) PhysicalPath=$($site.PhysicalPath)"
    Log "Bindings:"
    $bindings = Get-WebBinding -Name 'AppManager' -ErrorAction SilentlyContinue
    if ($bindings) { $bindings | ForEach-Object { Log " - $($_.protocol) $($_.bindingInformation)" } } else { Log ' - No bindings returned.' }
} catch {
    Log "Get-Website failed or site not present: $_"
}

try {
    $ap = Get-Item 'IIS:\AppPools\AppManagerPool_Admin' -ErrorAction Stop
    Log "AppPool: $($ap.Name) State=$($ap.State)"
    Log "AppPool.processModel.identityType: $($ap.processModel.identityType)"
} catch {
    Log "AppPool read failed: $_"
}

# Publish folder + web.config
$pub = Join-Path (Resolve-Path .).Path 'publish\AppManager'
Log "PublishDir: $pub"
if (Test-Path $pub) {
    Log 'Publish directory exists. Top files:'
    Get-ChildItem $pub -File -ErrorAction SilentlyContinue | Select-Object Name,Length | ForEach-Object { Log "  $($_.Name) ($($_.Length) bytes)" }
    $cfg = Join-Path $pub 'web.config'
    Log "web.config present: $(Test-Path $cfg)"
    if (Test-Path $cfg) {
        $has = Select-String -Path $cfg -Pattern '<aspNetCore' -SimpleMatch -Quiet
        if ($has) { Log 'web.config contains <aspNetCore> (OK)'} else { Log 'web.config missing <aspNetCore> (ANCM proxy may not be configured)'}
        Log '--- web.config head ---'
        Get-Content $cfg -TotalCount 40 | ForEach-Object { Log "  $_" }
    }
} else {
    Log 'Publish dir not found.'
}

# ANCM module
try {
    $appcmd = Join-Path $env:SystemRoot 'System32\inetsrv\appcmd.exe'
    if (Test-Path $appcmd) {
        Log 'Querying appcmd for AspNetCore module entries...'
        & $appcmd list module | Select-String -Pattern 'AspNetCore|aspnetcore' -CaseSensitive -SimpleMatch | ForEach-Object { Log "  $_" }
    } else {
        Log "appcmd.exe not found at $appcmd"
    }
} catch {
    Log "appcmd check failed: $_"
}

# Hosting bundle registry check
try {
    Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*' -ErrorAction SilentlyContinue |
      Where-Object { $_.DisplayName -like '*Hosting*Bundle*' -or $_.DisplayName -like '*ASP.NET Core*Hosting*' } |
      Select-Object DisplayName,DisplayVersion | ForEach-Object { Log "HostingBundle: $($_.DisplayName) $($_.DisplayVersion)" }
} catch {
    Log "Registry check failed: $_"
}

# Port listener
try {
    $found = netstat -ano | findstr ":5005"
    if ($found) { Log "netstat :5005 -> $found" } else { Log 'no listener on :5005' }
} catch { Log "netstat failed: $_" }

# Processes
try { Get-Process dotnet -ErrorAction SilentlyContinue | ForEach-Object { Log "dotnet PID=$($_.Id) Name=$($_.ProcessName)" } } catch {}
try { Get-Process w3wp -ErrorAction SilentlyContinue | ForEach-Object { Log "w3wp PID=$($_.Id)" } } catch {}

# redirection.config ACL check + minimal grant
$rc = 'C:\Windows\System32\inetsrv\config\redirection.config'
Log "redirection.config exists: $(Test-Path $rc)"
if (Test-Path $rc) {
    Log 'ACL before:'
    icacls $rc | ForEach-Object { Log "  $_" }
    Log "Granting read to IIS AppPool\AppManagerPool_Admin (minimal)"
    icacls $rc /grant "IIS AppPool\AppManagerPool_Admin:R" | ForEach-Object { Log "  $_" }
    Start-Sleep -Milliseconds 300
    Log 'ACL after:'
    icacls $rc | ForEach-Object { Log "  $_" }
} else {
    Log 'redirection.config not found; skipping ACL step.'
}

# Test Admin page via HTTP
try {
    Log 'Testing http://localhost:5005/Admin/ApplicationManagement'
    $r = Invoke-WebRequest 'http://localhost:5005/Admin/ApplicationManagement' -UseBasicParsing -TimeoutSec 10 -ErrorAction Stop
    Log "Admin fetch: $($r.StatusCode) length=$($r.RawContentLength)"
    $r.Content | Out-File -Encoding utf8 $adminHtml
    Log "Saved admin HTML to $adminHtml"
} catch {
    Log "Admin fetch failed: $_"
}

Log '=== iis_check_and_fix finished ==='
Write-Output "Log written to: $log"
Write-Output "Admin HTML (if saved) at: $adminHtml"

exit 0
