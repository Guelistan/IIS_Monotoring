using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AppManager.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace AppManager.Services
{
    public class ProgramManagerService
    {
        private readonly Microsoft.Extensions.Logging.ILogger<ProgramManagerService> _logger;

        public ProgramManagerService(Microsoft.Extensions.Logging.ILogger<ProgramManagerService> logger)
        {
            _logger = logger;
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

        #region Program Control

        public async Task<bool> StartProgramAsync(Application app)
        {
            return await Task.Run(() =>
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

                    var process = Process.Start(startInfo);
                    
                    if (process != null)
                    {
                        app.ProcessId = process.Id;
                        app.IsStarted = true;
                        _logger.LogInformation($"✅ Erfolgreich gestartet! PID: {process.Id}");
                        return true;
                    }

                    _logger.LogError("❌ Process.Start() gab null zurück");
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"❌ FEHLER beim Starten von {app.Name}: {ex.Message}");
                    return false;
                }
            });
        }

        public async Task<bool> StopProgramAsync(Application app)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (app.ProcessId.HasValue)
                    {
                        var process = Process.GetProcessById(app.ProcessId.Value);
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
                    app.ProcessId = null;
                    _logger.LogInformation($"⏹️ {app.Name} gestoppt");
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.LogError($"❌ Fehler beim Stoppen: {ex.Message}");
                    app.IsStarted = false;
                    app.ProcessId = null;
                    return false;
                }
            });
        }

        public async Task<bool> RestartProgramAsync(Application app)
        {
            _logger.LogInformation($"🔄 Neustart von {app.Name}...");
            await StopProgramAsync(app);
            await Task.Delay(2000);
            return await StartProgramAsync(app);
        }

        #endregion
    }
}