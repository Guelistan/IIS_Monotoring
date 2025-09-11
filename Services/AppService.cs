using System;
using System.Diagnostics;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Principal;
using AppManager.Models;
using AppModel = AppManager.Models.Application;

namespace AppManager.Services
{
    public class AppService
    {
        // Start a process by executable path. Returns false and an error message on failure.
        public bool TryStartProcess(Application app, out string error)
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

        // Stop processes matching the executable name (best effort). Returns false and an error on failure.
        public bool TryStopProcess(Application app, out string error)
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

        // ------------------------------------------------------------------
        // IIS helpers (reflection-based) - optional: will try to load
        // Microsoft.Web.Administration at runtime. If unavailable or
        // permissions are insufficient the methods return false and an
        // explanatory error message.
        // These helpers avoid a compile-time dependency so the project can
        // still build on non-Windows/dev machines.
        // ------------------------------------------------------------------

        public bool TryListIisAppPools(out List<(string Name, string State)> pools, out string error)
        {
            pools = new List<(string, string)>();
            error = null;
            try
            {
                // Pre-flight: check IIS config file accessibility to provide clearer guidance
                if (!TryCheckIisConfigAccess(out var preflightError))
                {
                    error = preflightError;
                    return false;
                }

                var asm = GetOrLoadIisAssembly(out string loadError);
                if (asm == null)
                {
                    error = loadError ?? "Microsoft.Web.Administration not found.";
                    return false;
                }

                var serverManagerType = asm.GetType("Microsoft.Web.Administration.ServerManager");
                if (serverManagerType == null)
                {
                    error = "ServerManager type not found in Microsoft.Web.Administration.";
                    return false;
                }

                using (var serverManager = Activator.CreateInstance(serverManagerType) as IDisposable)
                {
                    var appPoolsProp = serverManagerType.GetProperty("ApplicationPools");
                    var appPools = appPoolsProp?.GetValue(serverManager) as IEnumerable;
                    if (appPools == null)
                    {
                        error = "No ApplicationPools collection available.";
                        return false;
                    }

                    foreach (var p in appPools)
                    {
                        var t = p.GetType();
                        var name = t.GetProperty("Name")?.GetValue(p)?.ToString() ?? "";
                        var stateObj = t.GetProperty("State")?.GetValue(p);
                        var state = stateObj?.ToString() ?? "Unknown";
                        pools.Add((name, state));
                    }
                }

                return true;
            }
            catch (TargetInvocationException tie)
            {
                error = FormatIisError(tie.InnerException ?? tie);
                return false;
            }
            catch (System.UnauthorizedAccessException)
            {
                error = "⚠️ Keine Berechtigung für IIS-Zugriff. Starten Sie die Anwendung als Administrator.";
                return false;
            }
            catch (System.ComponentModel.Win32Exception w32ex)
            {
                error = $"⚠️ Windows-Systemproblem: {w32ex.Message}";
                return false;
            }
            catch (System.IO.FileNotFoundException fnfex) when (fnfex.Message.Contains("redirection.config"))
            {
                error = "⚠️ IIS-Konfigurationsdatei nicht verfügbar. Bitte prüfen Sie die IIS-Installation.";
                return false;
            }
            catch (Exception ex)
            {
                error = FormatIisError(ex);
                return false;
            }
        }

        public bool TryStartIisAppPool(string name, out string error)
        {
            return ExecuteIisPoolOperation(name, "Start", out error);
        }

        public bool TryStopIisAppPool(string name, out string error)
        {
            return ExecuteIisPoolOperation(name, "Stop", out error);
        }

        public bool TryRecycleIisAppPool(string name, out string error)
        {
            return ExecuteIisPoolOperation(name, "Recycle", out error);
        }

        private static Assembly _cachedIisAssembly;
        private static readonly object _assemblyLock = new object();

        private bool ExecuteIisPoolOperation(string poolName, string methodName, out string error)
        {
            error = null;
            try
            {
                // Pre-flight: check IIS config file accessibility to provide clearer guidance
                if (!TryCheckIisConfigAccess(out var preflightError))
                {
                    error = preflightError;
                    return false;
                }

                var asm = GetOrLoadIisAssembly(out string loadError);
                if (asm == null)
                {
                    error = loadError ?? "Microsoft.Web.Administration not available.";
                    return false;
                }

                var serverManagerType = asm.GetType("Microsoft.Web.Administration.ServerManager");
                var sm = Activator.CreateInstance(serverManagerType);
                try
                {
                    var appPoolsProp = serverManagerType.GetProperty("ApplicationPools");
                    var appPools = appPoolsProp?.GetValue(sm) as IEnumerable;
                    if (appPools == null)
                    {
                        error = "ApplicationPools not found.";
                        return false;
                    }

                    foreach (var p in appPools)
                    {
                        var t = p.GetType();
                        var pname = t.GetProperty("Name")?.GetValue(p)?.ToString();
                        if (string.Equals(pname, poolName, StringComparison.OrdinalIgnoreCase))
                        {
                            var mi = t.GetMethod(methodName);
                            mi?.Invoke(p, null);
                            var commit = serverManagerType.GetMethod("CommitChanges");
                            commit?.Invoke(sm, null);
                            return true;
                        }
                    }

                    error = "AppPool not found.";
                    return false;
                }
                finally
                {
                    (sm as IDisposable)?.Dispose();
                }
            }
            catch (TargetInvocationException tie)
            {
                error = FormatIisError(tie.InnerException ?? tie);
                return false;
            }
            catch (System.UnauthorizedAccessException)
            {
                error = "⚠️ Keine Berechtigung für IIS-AppPool-Operationen. Starten Sie die Anwendung als Administrator.";
                return false;
            }
            catch (System.ComponentModel.Win32Exception w32ex)
            {
                error = $"⚠️ Windows-Systemproblem bei IIS-Operation: {w32ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                error = FormatIisError(ex);
                return false;
            }
        }

        private Assembly GetOrLoadIisAssembly(out string error)
        {
            error = null;
            if (_cachedIisAssembly != null)
                return _cachedIisAssembly;

            lock (_assemblyLock)
            {
                if (_cachedIisAssembly != null)
                    return _cachedIisAssembly;

                try
                {
                    try
                    {
                        _cachedIisAssembly = Assembly.Load("Microsoft.Web.Administration");
                        return _cachedIisAssembly;
                    }
                    catch { }

                    var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                    var candidate = System.IO.Path.Combine(windir, "System32", "inetsrv", "Microsoft.Web.Administration.dll");
                    if (System.IO.File.Exists(candidate))
                    {
                        _cachedIisAssembly = Assembly.LoadFrom(candidate);
                        return _cachedIisAssembly;
                    }

                    error = "Microsoft.Web.Administration assembly not found on this machine.";
                    return null;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return null;
                }
            }
        }

        public bool TryStartIisAppPoolWithVerification(string poolName, out string message)
        {
            message = string.Empty;
            
            if (!TryStartIisAppPool(poolName, out var error))
            {
                message = error;
                return false;
            }

            // Verify the pool actually started
            System.Threading.Thread.Sleep(500); // Give IIS time to update state
            if (VerifyAppPoolState(poolName, "Started", out var actualState))
            {
                message = $"AppPool '{poolName}' erfolgreich gestartet.";
                return true;
            }
            else
            {
                message = $"AppPool-Start-Befehl gesendet, aber Status ist '{actualState}' statt 'Started'. " +
                         "Möglicherweise sind weitere Berechtigungen oder Zeit erforderlich.";
                return false;
            }
        }

        public bool TryStopIisAppPoolWithVerification(string poolName, out string message)
        {
            message = string.Empty;
            
            if (!TryStopIisAppPool(poolName, out var error))
            {
                message = error;
                return false;
            }

            // Verify the pool actually stopped
            System.Threading.Thread.Sleep(500); // Give IIS time to update state
            if (VerifyAppPoolState(poolName, "Stopped", out var actualState))
            {
                message = $"AppPool '{poolName}' erfolgreich gestoppt.";
                return true;
            }
            else
            {
                message = $"AppPool-Stop-Befehl gesendet, aber Status ist '{actualState}' statt 'Stopped'. " +
                         "Möglicherweise sind weitere Berechtigungen oder Zeit erforderlich.";
                return false;
            }
        }

        public bool TryRecycleIisAppPoolWithVerification(string poolName, out string message)
        {
            message = string.Empty;
            
            if (!TryRecycleIisAppPool(poolName, out var error))
            {
                message = error;
                return false;
            }

            // For recycle, we expect it to be Started after the operation
            System.Threading.Thread.Sleep(1000); // Recycle takes longer
            if (VerifyAppPoolState(poolName, "Started", out var actualState))
            {
                message = $"AppPool '{poolName}' erfolgreich recycelt und läuft wieder.";
                return true;
            }
            else
            {
                message = $"AppPool-Recycle-Befehl gesendet, aber Status ist '{actualState}'. " +
                         "Prüfen Sie den IIS-Manager für weitere Details.";
                return false;
            }
        }
        private bool VerifyAppPoolState(string poolName, string expectedState, out string actualState)
        {
            actualState = "Unknown";
            try
            {
                if (!TryListIisAppPools(out var pools, out _))
                    return false;

                var pool = pools.FirstOrDefault(p => string.Equals(p.Name, poolName, StringComparison.OrdinalIgnoreCase));
                if (pool.Name == null)
                    return false;

                actualState = pool.State;
                return string.Equals(pool.State, expectedState, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // Helper method to format IIS-related errors in a user-friendly way
        private string FormatIisError(Exception ex)
        {
            if (ex == null) return "Unbekannter IIS-Fehler.";

            var message = ex.Message?.ToLowerInvariant() ?? "";

            // Check for common IIS permission/configuration issues
            if (message.Contains("redirection.config"))
            {
                return "⚠️ IIS-Konfigurationsproblem: Die Datei 'redirection.config' kann nicht gelesen werden. " +
                       "Starten Sie die Anwendung als Administrator oder prüfen Sie die IIS-Installation.";
            }

            if (message.Contains("0x80070005") || (message.Contains("access") && message.Contains("denied")))
            {
                return "⚠️ Zugriff verweigert (0x80070005): Keine Berechtigung für IIS-Operationen. " +
                       "Starten Sie die Anwendung als Administrator.";
            }

            if (message.Contains("access") && message.Contains("denied"))
            {
                return "⚠️ Zugriff verweigert: Keine Berechtigung für IIS-Operationen. " +
                       "Starten Sie die Anwendung als Administrator.";
            }

            if (message.Contains("applicationhost.config"))
            {
                return "⚠️ IIS-Konfigurationsproblem: Die Hauptkonfigurationsdatei kann nicht gelesen werden. " +
                       "Prüfen Sie die IIS-Installation und Berechtigungen.";
            }

            if (message.Contains("insufficient") || message.Contains("privilege"))
            {
                return "⚠️ Unzureichende Berechtigungen für IIS-Zugriff. " +
                       "Starten Sie die Anwendung als Administrator.";
            }

            if (message.Contains("not found") || message.Contains("does not exist"))
            {
                return "⚠️ IIS-Komponente nicht gefunden. Prüfen Sie, ob IIS korrekt installiert ist.";
            }

            // Return original message with warning icon for unknown errors
            return $"⚠️ IIS-Fehler: {ex.Message}";
        }

        // Perform a quick, safe check whether IIS configuration files are readable and environment is sane.
        private bool TryCheckIisConfigAccess(out string error)
        {
            error = null;

            try
            {
                // IIS only available on Windows
                if (!OperatingSystem.IsWindows())
                {
                    error = "IIS ist nur unter Windows verfügbar.";
                    return false;
                }

                // Check Admin context (most IIS config operations require elevation)
                bool isAdmin = false;
                try
                {
                    var wp = new WindowsPrincipal(WindowsIdentity.GetCurrent());
                    isAdmin = wp.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch { /* ignore */ }

                var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                var configDir = Path.Combine(windir, "System32", "inetsrv", "config");
                var appHost = Path.Combine(configDir, "applicationHost.config");
                var redirection = Path.Combine(configDir, "redirection.config");

                // If config folder doesn't exist, IIS likely not installed
                if (!Directory.Exists(configDir))
                {
                    error = "⚠️ IIS scheint nicht installiert zu sein (Ordner 'inetsrv\\config' fehlt). Installieren Sie Internet Information Services.";
                    return false;
                }

                // Try to open applicationHost.config for read (shared lock)
                if (File.Exists(appHost))
                {
                    try
                    {
                        using var fs = File.Open(appHost, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        error = "⚠️ Zugriff auf 'applicationHost.config' verweigert. Starten Sie die Anwendung als Administrator.";
                        return false;
                    }
                }

                // redirection.config is present when Shared Configuration is configured; if present but unreadable, surface specific guidance
                if (File.Exists(redirection))
                {
                    try
                    {
                        using var fs = File.Open(redirection, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        error = "⚠️ IIS-Konfigurationsproblem: Die Datei 'redirection.config' kann nicht gelesen werden. " +
                                "Starten Sie die Anwendung als Administrator oder prüfen Sie die 'Shared Configuration' im IIS-Manager.";
                        return false;
                    }
                    catch (IOException ioex)
                    {
                        // Surface a helpful message but don't be overly specific
                        error = $"⚠️ Problem beim Lesen von 'redirection.config': {ioex.Message}";
                        return false;
                    }
                }

                // If not admin, warn early to reduce confusion, but allow operations to proceed in case environment permits
                if (!isAdmin)
                {
                    // Only warn for operations that will need elevation; here we just return true and let deeper calls attempt
                    // To centralize messaging, we don't set error here.
                }

                return true;
            }
            catch (Exception ex)
            {
                error = $"⚠️ Vorabprüfung der IIS-Konfiguration fehlgeschlagen: {ex.Message}";
                return false;
            }
        }
    }
}
