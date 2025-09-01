using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using AppManager.Models;
using Microsoft.Web.Administration;

namespace AppManager.Services
{
    public class IISService
    {
        public async Task<bool> StartApplicationPoolAsync(string appPoolName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var serverManager = new ServerManager())
                    {
                        var appPool = serverManager.ApplicationPools[appPoolName];
                        if (appPool != null)
                        {
                            if (appPool.State == ObjectState.Stopped)
                            {
                                appPool.Start();
                                Console.WriteLine($"✅ IIS Application Pool '{appPoolName}' gestartet");
                                return true;
                            }
                            else
                            {
                                Console.WriteLine($"ℹ️ IIS Application Pool '{appPoolName}' ist bereits aktiv (Status: {appPool.State})");
                                return true;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"❌ IIS Application Pool '{appPoolName}' nicht gefunden");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Fehler beim Starten des IIS Application Pools '{appPoolName}': {ex.Message}");
                    return false;
                }
            });
        }

        public async Task<bool> StopApplicationPoolAsync(string appPoolName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var serverManager = new ServerManager())
                    {
                        var appPool = serverManager.ApplicationPools[appPoolName];
                        if (appPool != null)
                        {
                            if (appPool.State == ObjectState.Started)
                            {
                                appPool.Stop();
                                Console.WriteLine($"✅ IIS Application Pool '{appPoolName}' gestoppt");
                                return true;
                            }
                            else
                            {
                                Console.WriteLine($"ℹ️ IIS Application Pool '{appPoolName}' ist bereits gestoppt (Status: {appPool.State})");
                                return true;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"❌ IIS Application Pool '{appPoolName}' nicht gefunden");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Fehler beim Stoppen des IIS Application Pools '{appPoolName}': {ex.Message}");
                    return false;
                }
            });
        }

        public async Task<bool> RestartApplicationPoolAsync(string appPoolName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var serverManager = new ServerManager())
                    {
                        var appPool = serverManager.ApplicationPools[appPoolName];
                        if (appPool != null)
                        {
                            appPool.Recycle();
                            Console.WriteLine($"✅ IIS Application Pool '{appPoolName}' recycelt");
                            return true;
                        }
                        else
                        {
                            Console.WriteLine($"❌ IIS Application Pool '{appPoolName}' nicht gefunden");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Fehler beim Recyceln des IIS Application Pools '{appPoolName}': {ex.Message}");
                    return false;
                }
            });
        }

        public async Task<ObjectState?> GetApplicationPoolStatusAsync(string appPoolName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var serverManager = new ServerManager())
                    {
                        var appPool = serverManager.ApplicationPools[appPoolName];
                        if (appPool != null)
                        {
                            return appPool.State;
                        }
                        else
                        {
                            Console.WriteLine($"❌ IIS Application Pool '{appPoolName}' nicht gefunden");
                            return (ObjectState?)null;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Fehler beim Abrufen des Status für IIS Application Pool '{appPoolName}': {ex.Message}");
                    return null;
                }
            });
        }

        public async Task<List<string>> GetApplicationPoolsAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var serverManager = new ServerManager())
                    {
                        return serverManager.ApplicationPools
                            .Select(pool => pool.Name)
                            .ToList();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Fehler beim Abrufen der IIS Application Pools: {ex.Message}");
                    return new List<string>();
                }
            });
        }

        public async Task<List<string>> GetWebsitesAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var serverManager = new ServerManager())
                    {
                        return serverManager.Sites
                            .Select(site => site.Name)
                            .ToList();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Fehler beim Abrufen der IIS Websites: {ex.Message}");
                    return new List<string>();
                }
            });
        }

        public async Task<bool> IsIISAvailableAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var serverManager = new ServerManager())
                    {
                        var pools = serverManager.ApplicationPools.Count;
                        Console.WriteLine($"✅ IIS ist verfügbar. {pools} Application Pools gefunden.");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ IIS ist nicht verfügbar: {ex.Message}");
                    return false;
                }
            });
        }

        public async Task<IISApplicationInfo> GetApplicationInfoAsync(string appPoolName, string siteName)
        {
            return await Task.Run(() =>
            {
                var info = new IISApplicationInfo
                {
                    AppPoolName = appPoolName,
                    SiteName = siteName,
                    IsAvailable = false
                };

                try
                {
                    using (var serverManager = new ServerManager())
                    {
                        var appPool = serverManager.ApplicationPools[appPoolName];
                        if (appPool != null)
                        {
                            info.AppPoolStatus = appPool.State.ToString();
                            info.IsAppPoolRunning = appPool.State == ObjectState.Started;
                        }

                        var site = serverManager.Sites[siteName];
                        if (site != null)
                        {
                            info.SiteStatus = site.State.ToString();
                            info.IsSiteRunning = site.State == ObjectState.Started;
                            info.SiteBindings = site.Bindings.Select(b => $"{b.Protocol}://{b.Host}:{b.EndPoint.Port}").ToList();
                        }

                        info.IsAvailable = true;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Fehler beim Abrufen der IIS Application Info: {ex.Message}");
                    info.ErrorMessage = ex.Message;
                }

                return info;
            });
        }

        public async Task<List<IISAppInfo>> GetAllApplicationsAsync()
        {
            return await Task.Run(() =>
            {
                var result = new List<IISAppInfo>();
                try
                {
                    using (var serverManager = new ServerManager())
                    {
                        foreach (var site in serverManager.Sites)
                        {
                            foreach (var app in site.Applications)
                            {
                                result.Add(new IISAppInfo
                                {
                                    SiteName = site.Name,
                                    AppPath = app.Path,
                                    AppPoolName = app.ApplicationPoolName
                                });
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Fehler beim Abrufen der IIS Anwendungen: {ex.Message}");
                }
                // Doppelte entfernen
                return result
                    .GroupBy(x => new { x.SiteName, x.AppPath, x.AppPoolName })
                    .Select(g => g.First())
                    .ToList();
            });
        }

        public async Task<bool> RecycleAppPoolAsync(string appPoolName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var serverManager = new ServerManager())
                    {
                        var appPool = serverManager.ApplicationPools[appPoolName];
                        if (appPool != null)
                        {
                            appPool.Recycle();
                            Console.WriteLine($"✅ IIS Application Pool '{appPoolName}' recycelt");
                            return true;
                        }
                        else
                        {
                            Console.WriteLine($"❌ IIS Application Pool '{appPoolName}' nicht gefunden");
                            return false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Fehler beim Recyceln des IIS Application Pools '{appPoolName}': {ex.Message}");
                    return false;
                }
            });
        }

        public List<string> GetAllAppPoolNames()
        {
            var appPoolNames = new List<string>();
            using (var serverManager = new ServerManager())
            {
                foreach (var appPool in serverManager.ApplicationPools)
                {
                    appPoolNames.Add(appPool.Name);
                }
            }
            return appPoolNames;
        }
    }

    public class IISApplicationInfo
    {
        public string AppPoolName { get; set; } = string.Empty;
        public string SiteName { get; set; } = string.Empty;
        public string AppPoolStatus { get; set; } = string.Empty;
        public string SiteStatus { get; set; } = string.Empty;
        public bool IsAppPoolRunning { get; set; }
        public bool IsSiteRunning { get; set; }
        public List<string> SiteBindings { get; set; } = new();
        public bool IsAvailable { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }

    public class IISAppInfo
    {
        public string SiteName { get; set; }
        public string AppPath { get; set; }
        public string AppPoolName { get; set; }
    }
}
