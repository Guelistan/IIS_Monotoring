using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AppManager.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using AppManager.Data;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;

#nullable enable

namespace AppManager.Services
{
    // FACADE PATTERN: Einfache Schnittstelle für komplexe Operationen ihre merkmale sind Kapselung, Vereinfachung und Flexibilität
    public class ProgramManagerService
    {
        private readonly ILogger<ProgramManagerService> _logger;
        private readonly AppDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ProgramManagerService(
            ILogger<ProgramManagerService> logger,
            AppDbContext dbContext,
            IHttpContextAccessor httpContextAccessor)// Konstruktor mit Dependency Injection für Logger, DbContext und HttpContextAccessor 
        {
            _logger = logger;
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
        }
        // endregion des Konstruktors


        #region CPU Nutzung

        // Liefert den CPU-Auslastungs-Wert des Prozesses anhand der Prozess-ID in Prozent
        public double? GetCpuUsageForProcess(int processId)// Methode zur Ermittlung der CPU-Auslastung eines einzelnen Prozesses
        {
            try
            {
                using (var cpuCounter = new PerformanceCounter("Process", "% Processor Time", GetProcessInstanceName(processId), true))
                {
                    // Das erste Abfragen liefert oft 0 zurück, daher kurze Wartezeit weil PerformanceCounter initialisiert werden muss
                    cpuCounter.NextValue();
                    System.Threading.Thread.Sleep(500);
                    return Math.Round(cpuCounter.NextValue() / Environment.ProcessorCount, 2);
                }
            }
            catch
            {
                return null;// Bei Fehlern (z.B. Prozess nicht gefunden) wird null zurückgegeben
            }
        }

        // Berechnet die gesamte CPU-Auslastung für eine Sammlung von Prozess-IDs
        public double? GetCpuUsageForAppPool(IEnumerable<int> processIds)
        {
            if (processIds == null) return null;
            var values = new List<double>();
            foreach (var pid in processIds)
            {
                var v = GetCpuUsageForProcess(pid);
                if (v.HasValue) values.Add(v.Value);
            }
            if (values.Count == 0) return null;
            return Math.Round(values.Sum(), 2);
        }

        // Ermittelt den Instanznamen des Prozesses anhand der Prozess-ID
        private string GetProcessInstanceName(int processId)
        {
            var process = Process.GetProcessById(processId);
            var processName = process.ProcessName;
            var category = new PerformanceCounterCategory("Process");
            var instances = category.GetInstanceNames();
            foreach (var instance in instances)
            {
                using (var counter = new PerformanceCounter("Process", "ID Process", instance, true))
                {
                    if ((int)counter.RawValue == processId)
                        return instance;
                }
            }
            return processName;
        }

        #endregion

        #region Aktivitätsprotokollierung

        // Ermittelt den aktuellen Benutzer anhand des HTTP-Kontexts und ermittelt die zugehörige UserId sowie den Windows-Benutzernamen
        private (string? userId, string windowsUsername) ResolveCurrentUser()
        {
            try
            {
                var principal = _httpContextAccessor.HttpContext?.User;
                var windowsUsername = principal?.Identity?.Name ?? "System"; // Beispiel: DOMAIN\User

                // Auslesen der Claims aus der Negotiate-Authentifizierung, die beim Login gesetzt werden
                var sid = principal?.Claims.FirstOrDefault(c => c.Type == "windows_sid")?.Value;
                var claimUser = principal?.Claims.FirstOrDefault(c => c.Type == "windows_username")?.Value ?? windowsUsername;

                // 1) Versuch, den Benutzer über die SID zu finden
                var user = !string.IsNullOrEmpty(sid)
                    ? _dbContext.Users.FirstOrDefault(u => u.WindowsSid == sid)
                    : null;

                // 2) Fallback: Suche anhand des Windows-Benutzernamens oder UserNames
                if (user == null && !string.IsNullOrEmpty(claimUser))
                {
                    user = _dbContext.Users.FirstOrDefault(u => u.WindowsUsername == claimUser || u.UserName == claimUser);
                }

                // 3) Letzter Fallback: Verwende einen GlobalAdmin oder den Benutzer "admin"
                user ??= _dbContext.Users.FirstOrDefault(u => u.IsGlobalAdmin)
                         ?? _dbContext.Users.FirstOrDefault(u => u.UserName == "admin");

                return (user?.Id, windowsUsername);
            }
            catch
            {
                // Falls ein Fehler auftritt, wird als Fallback kein UserId und der Windows-Benutzername "System" verwendet
                return (null, _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System");
            }
        }

        // Protokolliert Aktivitätsereignisse (z.B. Start, Stop, Neustart) der Anwendung in der Datenbank
        public async Task LogAppActivityAsync(Application app, string action, string reason = "")
        {
            try
            {
                var (resolvedUserId, windowsUsername) = ResolveCurrentUser();

                // Sicherstellen, dass eine gültige UserId vorhanden ist. Ist dies nicht der Fall, wird ein Fallback verwendet
                if (string.IsNullOrEmpty(resolvedUserId))
                {
                    var fallback = _dbContext.Users.FirstOrDefault(u => u.IsGlobalAdmin)
                                   ?? _dbContext.Users.FirstOrDefault(u => u.UserName == "admin");
                    resolvedUserId = fallback?.Id ?? throw new InvalidOperationException("Kein gültiger AppUser für History-Eintrag verfügbar.");
                }

                // FACTORY PATTERN: Zentralisierte Erstellung des Protokoll-Eintrags
                var launchHistory = new AppLaunchHistory
                {
                    ApplicationId = app.Id,
                    UserId = resolvedUserId,
                    WindowsUsername = windowsUsername,
                    IISAppPoolName = app.IISAppPoolName ?? string.Empty,
                    LaunchTime = DateTime.UtcNow,
                    Action = action,
                    Reason = reason
                };

                _dbContext.AppLaunchHistories.Add(launchHistory);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation($"App-Aktivität protokolliert: {action} - {app.Name} (UserId: {resolvedUserId}, Windows: {windowsUsername})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Speichern der App-Aktivität");
            }
        }

        #endregion

        #region Programmsteuerung

        // Startet ein Programm anhand der im Application-Objekt hinterlegten Informationen
        public async Task<bool> StartProgramAsync(Application app)
        {
            try
            {
                _logger.LogInformation($"Versuche zu starten: {app.ExecutablePath}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = app.ExecutablePath,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WorkingDirectory = string.IsNullOrEmpty(app.WorkingDirectory)
                        ? Environment.GetFolderPath(Environment.SpecialFolder.System)
                        : app.WorkingDirectory
                };

                if (!string.IsNullOrEmpty(app.Arguments))
                {
                    startInfo.Arguments = app.Arguments;
                }

                var process = await Task.Run(() => Process.Start(startInfo));

                if (process != null)
                {
                    // Speichert die Prozess-ID und markiert die Anwendung als gestartet
                    app.ProcessId = process.Id;
                    app.IsStarted = true;
                    _logger.LogInformation($"Erfolgreich gestartet! PID: {process.Id}");

                    // Protokolliert die Start-Aktivität der Anwendung
                    await LogAppActivityAsync(app, "Start", $"App gestartet (PID: {process.Id})");

                    return true;
                }

                _logger.LogError("Process.Start() gab null zurück");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Fehler beim Starten von {app.Name}: {ex.Message}");

                // Protokolliert den Fehler im Zusammenhang mit dem Startvorgang
                await LogAppActivityAsync(app, "Start-Fehler", $"Fehler beim Starten: {ex.Message}");

                return false;
            }
        }

        // Stoppt ein Programm über die gespeicherte Prozess-ID
        public async Task<bool> StopProgramAsync(Application app)
        {
            try
            {
                if (app.ProcessId.HasValue)
                {
                    var process = await Task.Run(() => Process.GetProcessById(app.ProcessId.Value));
                    if (!process.HasExited)
                    {
                        // Versucht das Hauptfenster des Prozesses zu schließen, bei längerer Verzögerung wird der Prozess beendet
                        process.CloseMainWindow();
                        if (!process.WaitForExit(3000))
                        {
                            process.Kill();
                        }
                    }
                }

                app.IsStarted = false;
                var processId = app.ProcessId;
                app.ProcessId = null;

                _logger.LogInformation($"{app.Name} wurde gestoppt");

                // Protokolliert die Stop-Aktivität der Anwendung
                await LogAppActivityAsync(app, "Stop", $"App gestoppt" + (processId.HasValue ? $" (PID: {processId})" : ""));

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"Fehler beim Stoppen: {ex.Message}");
                app.IsStarted = false;
                app.ProcessId = null;

                // Protokolliert den Fehler im Zusammenhang mit dem Stopvorgang
                await LogAppActivityAsync(app, "Stop-Fehler", $"Fehler beim Stoppen: {ex.Message}");

                return false;
            }
        }

        // Startet ein Programm neu, indem es zunächst gestoppt und dann wieder gestartet wird
        public async Task<bool> RestartProgramAsync(Application app)
        {
            _logger.LogInformation($"Starte {app.Name} neu...");

            // Protokolliert den Neustart der Anwendung
            await LogAppActivityAsync(app, "Restart", "App wird neu gestartet");

            await StopProgramAsync(app);
            await Task.Delay(2000);
            return await StartProgramAsync(app);
        }

        #endregion
    }
}
