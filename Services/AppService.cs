using System;
using System.Diagnostics;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
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
                error = tie.InnerException?.Message ?? tie.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
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
                error = tie.InnerException?.Message ?? tie.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
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
    }
}
