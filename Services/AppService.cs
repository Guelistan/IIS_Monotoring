using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Web.Administration;
using AppModel = AppManager.Models.Application;

namespace AppManager.Services
{
    public class AppService
    {
        private readonly ILogger<AppService> _logger;

        public AppService(ILogger<AppService> logger)
        {
            _logger = logger;
        }

        public bool TryStartProcess(AppModel app, out string error)
        {
            error = null;
            try
            {
                var psi = new ProcessStartInfo(app.ExecutablePath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public bool TryStopProcess(AppModel app, out string error)
        {
            error = null;
            try
            {
                string processName = System.IO.Path.GetFileNameWithoutExtension(app.ExecutablePath);
                var processes = Process.GetProcessesByName(processName);

                if (processes.Length == 0)
                {
                    error = $"Kein laufender Prozess mit dem Namen '{processName}' gefunden.";
                    return false;
                }

                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit();
                    }
                    catch (Exception ex)
                    {
                        error = $"Prozess '{processName}' konnte nicht beendet werden: {ex.Message}";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        // Einfache IIS-Methoden
        public bool TryListIisAppPools(out List<(string Name, string State)> pools, out string error)
        {
            pools = new List<(string, string)>();
            error = null;

            try
            {
                using var iisManager = new ServerManager();
                pools = iisManager.ApplicationPools
                    .Select(pool => (pool.Name, pool.State.ToString()))
                    .ToList();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                error = "Keine Berechtigung für IIS-Zugriff";
                return false;
            }
            catch (Exception ex)
            {
                error = $"Fehler: {ex.Message}";
                return false;
            }
        }

        public bool TryStartIisAppPool(string poolName, out string error)
        {
            return TryStartIisAppPoolWithVerification(poolName, out error);
        }

        public bool TryStartIisAppPoolWithVerification(string poolName, out string message)
        {
            message = string.Empty;
            try
            {
                using var iisManager = new ServerManager();
                var appPool = iisManager.ApplicationPools[poolName];
                
                if (appPool == null)
                {
                    message = $"AppPool '{poolName}' nicht gefunden";
                    return false;
                }

                if (appPool.State == ObjectState.Started)
                {
                    message = $"AppPool '{poolName}' läuft bereits";
                    return true;
                }

                appPool.Start();
                message = $"AppPool '{poolName}' gestartet";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Fehler: {ex.Message}";
                return false;
            }
        }

        public bool TryStopIisAppPool(string poolName, out string error)
        {
            return TryStopIisAppPoolWithVerification(poolName, out error);
        }

        public bool TryStopIisAppPoolWithVerification(string poolName, out string message)
        {
            message = string.Empty;
            try
            {
                using var iisManager = new ServerManager();
                var appPool = iisManager.ApplicationPools[poolName];
                
                if (appPool == null)
                {
                    message = $"AppPool '{poolName}' nicht gefunden";
                    return false;
                }

                appPool.Stop();
                message = $"AppPool '{poolName}' gestoppt";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Fehler: {ex.Message}";
                return false;
            }
        }

        public bool TryRecycleIisAppPool(string poolName, out string error)
        {
            return TryRecycleIisAppPoolWithVerification(poolName, out error);
        }

        public bool TryRecycleIisAppPoolWithVerification(string poolName, out string message)
        {
            message = string.Empty;
            try
            {
                using var iisManager = new ServerManager();
                var appPool = iisManager.ApplicationPools[poolName];
                
                if (appPool == null)
                {
                    message = $"AppPool '{poolName}' nicht gefunden";
                    return false;
                }

                appPool.Recycle();
                message = $"AppPool '{poolName}' recycelt";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Fehler: {ex.Message}";
                return false;
            }
        }
    }
}
