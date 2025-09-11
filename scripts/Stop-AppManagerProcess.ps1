Write-Host "🛑 Beende laufende AppManager.exe Prozesse" -ForegroundColor Cyan
$procs = Get-Process -Name AppManager -ErrorAction SilentlyContinue
if (-not $procs) {
    Write-Host "   ✅ Kein laufender Prozess gefunden." -ForegroundColor Green
    exit 0
}
foreach ($p in $procs) {
    try {
        Write-Host "   🔄 Beende PID $($p.Id)" -ForegroundColor Yellow
        $p.Kill()
        $p.WaitForExit()
        Write-Host "   ✅ Beendet: $($p.Id)" -ForegroundColor Green
    } catch {
        Write-Host "   ❌ Fehler beim Beenden von $($p.Id): $($_.Exception.Message)" -ForegroundColor Red
    }
}
Write-Host "Fertig." -ForegroundColor Green
