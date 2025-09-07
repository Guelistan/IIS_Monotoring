<# $log = 'C:\guelistan\Dersler\Appmanager\scripts\fix_inetsrv_acl.log'
"--- ACL fix run started: $(Get-Date) ---" | Out-File $log -Encoding utf8

try {
    "Running: takeown /F \"C:\Windows\System32\inetsrv\config\schema\" /R /A" | Out-File $log -Append
 #>
$log = 'C:\guelistan\Dersler\Appmanager\scripts\fix_inetsrv_acl.log'

"--- ACL fix run started: $(Get-Date) ---" | Out-File $log -Encoding utf8

try {
    "Running: takeown /F \"C:\Windows\System32\inetsrv\config\schema\" /R /A" | Out-File $log -Append
    takeown /F "C:\Windows\System32\inetsrv\config\schema" /R /A 2>&1 | Tee-Object -FilePath $log -Append

    "Running: icacls \"C:\Windows\System32\inetsrv\config\schema\" /setowner \"NT SERVICE\\TrustedInstaller\" /T" | Out-File $log -Append
    icacls "C:\Windows\System32\inetsrv\config\schema" /setowner "NT SERVICE\\TrustedInstaller" /T 2>&1 | Tee-Object -FilePath $log -Append

    "icacls on schema (result):" | Out-File $log -Append
    icacls "C:\Windows\System32\inetsrv\config\schema" 2>&1 | Tee-Object -FilePath $log -Append

    "icacls on config (result):" | Out-File $log -Append
    icacls "C:\Windows\System32\inetsrv\config" 2>&1 | Tee-Object -FilePath $log -Append
} catch {
    "ERROR: $_" | Out-File $log -Append
}

"--- ACL fix run finished: $(Get-Date) ---" | Out-File $log -Append
Write-Output "Wrote log to: $log"