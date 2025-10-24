using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AppManager.Services
{
    /// <summary>
    /// CPU-Monitoring Service für IIS Application Pools und Prozesse
    /// </summary>
    public class CpuMonitoringService
    {
        private readonly ILogger<CpuMonitoringService> _logger;
        private readonly PerformanceCounter _totalCpuCounter;

        public CpuMonitoringService(ILogger<CpuMonitoringService> logger)
        {
            _logger = logger;
            _totalCpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        }

        /// <summary>
        /// Gesamte CPU-Auslastung des Systems
        /// </summary>
        public async Task<double> GetSystemCpuUsageAsync()
        {
            try
            {
                // Erste Messung (Referenzwert)
                _totalCpuCounter.NextValue();
                await Task.Delay(1000); // 1 Sekunde warten für genaue Messung
                
                var cpuUsage = _totalCpuCounter.NextValue();
                return Math.Round(cpuUsage, 2);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Fehler beim Abrufen der System-CPU-Auslastung");
                return 0;
            }
        }

        /// <summary>
        /// CPU-Auslastung für einen bestimmten Prozess
        /// </summary>
        public double GetProcessCpuUsage(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                if (process == null || process.HasExited)
                    return 0;

                var startTime = DateTime.UtcNow;
                var startCpuUsage = process.TotalProcessorTime;
                
                Thread.Sleep(500); // Kurz warten für Messung
                
                var endTime = DateTime.UtcNow;
                var endCpuUsage = process.TotalProcessorTime;
                
                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);
                
                return Math.Round(cpuUsageTotal * 100, 2);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"⚠️ CPU-Auslastung für Prozess {processId} nicht verfügbar: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Speicher-Auslastung für einen bestimmten Prozess (in MB)
        /// </summary>
        public double GetProcessMemoryUsage(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                if (process == null || process.HasExited)
                    return 0;

                return Math.Round(process.WorkingSet64 / 1024.0 / 1024.0, 2); // Bytes -> MB
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"⚠️ Speicher-Auslastung für Prozess {processId} nicht verfügbar: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// IIS Application Pool Prozesse finden
        /// </summary>
        public int[] GetIISAppPoolProcessIds(string appPoolName)
        {
            try
            {
                var processes = Process.GetProcessesByName("w3wp");
                var appPoolProcesses = new List<int>();

                foreach (var process in processes)
                {
                    try
                    {
                        // Kommandozeile prüfen um den AppPool zu identifizieren
                        var commandLine = GetProcessCommandLine(process.Id);
                        if (commandLine.Contains($"-ap \"{appPoolName}\"") || 
                            commandLine.Contains($"-ap {appPoolName}"))
                        {
                            appPoolProcesses.Add(process.Id);
                        }
                    }
                    catch
                    {
                        // Ignorieren wenn Kommandozeile nicht lesbar
                    }
                }

                return appPoolProcesses.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Fehler beim Finden der IIS AppPool Prozesse für {appPoolName}");
                return Array.Empty<int>();
            }
        }

        /// <summary>
        /// Hilfsmethode um Kommandozeile eines Prozesses zu erhalten
        /// </summary>
        private string GetProcessCommandLine(int processId)
        {
            try
            {
                // Vereinfachte Implementierung ohne WMI
                var process = Process.GetProcessById(processId);
                return process?.ProcessName ?? "";
            }
            catch
            {
                return "";
            }
        }

        public void Dispose()
        {
            _totalCpuCounter?.Dispose();
        }
    }

    /// <summary>
    /// DTO für Performance-Daten
    /// </summary>
    public class PerformanceData
    {
        public double CpuUsage { get; set; }
        public double MemoryUsageMB { get; set; }
        public int ProcessId { get; set; }
        public string ProcessName { get; set; } = "";
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}