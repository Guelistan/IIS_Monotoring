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
    // 🎯 FACADE PATTERN: Einfache Schnittstelle für komplexe Operationen
    public class ProgramManagerService
    {
        private readonly ILogger<ProgramManagerService> _logger;
        private readonly AppDbContext _dbContext;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ProgramManagerService(
            ILogger<ProgramManagerService> logger,
            AppDbContext dbContext,
            IHttpContextAccessor httpContextAccessor)
        {
            _logger = logger;
            _dbContext = dbContext;
            _httpContextAccessor = httpContextAccessor;
        }

        #region CPU Usage

        public double? GetCpuUsageForProcess(int processId)
        {
            try
            {
                using (var cpuCounter = new PerformanceCounter("Process", "% Processor Time", GetProcessInstanceName(processId), true))
                {
                    // Erstes Abfragen gibt oft 0 zurück, daher kurz warten
                    cpuCounter.NextValue();
                    System.Threading.Thread.Sleep(500);
                    return Math.Round(cpuCounter.NextValue() / Environment.ProcessorCount, 2);
                }
            }
            catch
            {
                return null;
            }
        }

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

        #region Activity Logging

        private (string? userId, string windowsUsername) ResolveCurrentUser()
        {
            try
            {
                var principal = _httpContextAccessor.HttpContext?.User;
                var windowsUsername = principal?.Identity?.Name ?? "System"; // e.g., DOMAIN\\User

                // Claims aus Negotiate-Auth (Program.cs) – werden beim Login gesetzt
                var sid = principal?.Claims.FirstOrDefault(c => c.Type == "windows_sid")?.Value;
                var claimUser = principal?.Claims.FirstOrDefault(c => c.Type == "windows_username")?.Value ?? windowsUsername;

                // 1) Versuche SID -> User
                var user = !string.IsNullOrEmpty(sid)
                    ? _dbContext.Users.FirstOrDefault(u => u.WindowsSid == sid)
                    : null;

                // 2) Fallback: WindowsUsername/UserName
                if (user == null && !string.IsNullOrEmpty(claimUser))
                {
                    user = _dbContext.Users.FirstOrDefault(u => u.WindowsUsername == claimUser || u.UserName == claimUser);
                }

                // 3) Letzter Fallback: ein GlobalAdmin/Admin
                user ??= _dbContext.Users.FirstOrDefault(u => u.IsGlobalAdmin)
                         ?? _dbContext.Users.FirstOrDefault(u => u.UserName == "admin");

                return (user?.Id, windowsUsername);
            }
            catch
            {
                // Im Zweifel ohne UserId, aber WindowsName zurückgeben – Aufrufer entscheidet über Fallback
                return (null, _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "System");
            }
        }

        public async Task LogAppActivityAsync(Application app, string action, string reason = "")
        {
            try
            {
                var (resolvedUserId, windowsUsername) = ResolveCurrentUser();

                // Absicherung: UserId ist in der DB als Required + FK hinterlegt → setze Admin-Fallback
                if (string.IsNullOrEmpty(resolvedUserId))
                {
                    var fallback = _dbContext.Users.FirstOrDefault(u => u.IsGlobalAdmin)
                                   ?? _dbContext.Users.FirstOrDefault(u => u.UserName == "admin");
                    resolvedUserId = fallback?.Id ?? throw new InvalidOperationException("Kein gültiger AppUser für History-Eintrag verfügbar.");
                }

                // 🎯 FACTORY PATTERN: Zentralisierte Objekt-Erstellung
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

                _logger.LogInformation($"📋 App activity logged: {action} - {app.Name} (UserId: {resolvedUserId}, Windows: {windowsUsername})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Fehler beim Speichern der App-Aktivität");
            }
        }

        #endregion

        #region Program Control

        // Komplexe Prozess-Verwaltung hinter einfacher API
        public async Task<bool> StartProgramAsync(Application app)
        {
            try
            {
                _logger.LogInformation($"🚀 Versuche zu starten: {app.ExecutablePath}");

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
                    app.ProcessId = process.Id;
                    app.IsStarted = true;
                    _logger.LogInformation($"✅ Erfolgreich gestartet! PID: {process.Id}");

                    // Activity loggen
                    await LogAppActivityAsync(app, "Start", $"App gestartet (PID: {process.Id})");

                    return true;
                }

                _logger.LogError("❌ Process.Start() gab null zurück");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ FEHLER beim Starten von {app.Name}: {ex.Message}");

                // Fehler loggen
                await LogAppActivityAsync(app, "Start-Fehler", $"Fehler beim Starten: {ex.Message}");

                return false;
            }
        }

        public async Task<bool> StopProgramAsync(Application app)
        {
            try
            {
                if (app.ProcessId.HasValue)
                {
                    var process = await Task.Run(() => Process.GetProcessById(app.ProcessId.Value));
                    if (!process.HasExited)
                    {
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

                _logger.LogInformation($"⏹️ {app.Name} gestoppt");

                // Activity loggen
                await LogAppActivityAsync(app, "Stop", $"App gestoppt" + (processId.HasValue ? $" (PID: {processId})" : ""));

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"❌ Fehler beim Stoppen: {ex.Message}");
                app.IsStarted = false;
                app.ProcessId = null;

                // Fehler loggen
                await LogAppActivityAsync(app, "Stop-Fehler", $"Fehler beim Stoppen: {ex.Message}");

                return false;
            }
        }

        public async Task<bool> RestartProgramAsync(Application app)
        {
            _logger.LogInformation($"🔄 Neustart von {app.Name}...");

            // Activity loggen
            await LogAppActivityAsync(app, "Restart", "App wird neu gestartet");

            await StopProgramAsync(app);
            await Task.Delay(2000);
            return await StartProgramAsync(app);
        }

        #endregion
    }
}
