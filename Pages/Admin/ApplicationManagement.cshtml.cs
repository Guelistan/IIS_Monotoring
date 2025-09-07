using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AppManager.Models;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Diagnostics;
using Microsoft.Web.Administration; // Für IIS-Verwaltung
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
namespace AppManager.Pages.Admin
{
    public class ApplicationManagementModel : PageModel
    {
    private readonly ILogger<ApplicationManagementModel> _logger;
    private readonly AppManager.Data.AppDbContext _db;
    private readonly UserManager<AppManager.Data.AppUser> _userManager;

        public ApplicationManagementModel(ILogger<ApplicationManagementModel> logger, AppManager.Data.AppDbContext db, UserManager<AppManager.Data.AppUser> userManager)
        {
            _logger = logger;
            _db = db;
            _userManager = userManager;
        }

        public List<AppManager.Models.Application> Applications { get; set; } = new();
        public AppManager.Models.Application NewApplication { get; set; } = new();
        public List<float> CpuLoads { get; set; } = new();
        public List<string> AppPoolNames { get; set; } = new();
        public string IisErrorMessage { get; set; } = string.Empty;
        // Load page data and authorization info
        public async Task OnGetAsync()
        {
            // Load DB applications (prefer persisted entries) and IIS list as fallback
            Applications = _db.Applications.OrderBy(a => a.Name).ToList();
            if (Applications == null || Applications.Count == 0)
            {
                Applications = GetIISApplications();
            }

            LoadCpuData();

            // Load users for owner selection
            Users = _userManager.Users.Where(u => u.IsActive).OrderBy(u => u.Vorname).ToList();

            // Populate current user and ownership cache for UI authorization checks
            var currentUser = await ResolveCurrentAppUserAsync();
            if (currentUser != null)
            {
                CurrentUserId = currentUser.Id;
                CurrentUserIsGlobalAdmin = currentUser.IsGlobalAdmin;
                var owned = _db.AppOwnerships.Where(o => o.UserId == currentUser.Id).Select(o => o.ApplicationId).ToList();
                OwnedApplicationIds = new HashSet<Guid>(owned);
            }

            await Task.CompletedTask;
        }

    public List<AppManager.Data.AppUser> Users { get; set; } = new();

    // Current user info for UI and quick checks
    public string CurrentUserId { get; set; } = string.Empty;
    public bool CurrentUserIsGlobalAdmin { get; set; } = false;
    public HashSet<Guid> OwnedApplicationIds { get; set; } = new HashSet<Guid>();

        [BindProperty]
        public AppManager.Models.Application BindNewApplication { get; set; } = new();

        [BindProperty]
        public Guid OwnerApplicationId { get; set; }

        [BindProperty]
        public string OwnerUserId { get; set; }

    public async Task<IActionResult> OnPostAddAsync()
        {
            if (string.IsNullOrWhiteSpace(BindNewApplication.Name))
            {
                TempData["ErrorMessage"] = "Name ist erforderlich.";
                return RedirectToPage();
            }

            var app = new AppManager.Models.Application
            {
                Id = Guid.NewGuid(),
                Name = BindNewApplication.Name,
                IISAppPoolName = BindNewApplication.IISAppPoolName,
                IsIISApplication = BindNewApplication.IsIISApplication,
                ExecutablePath = BindNewApplication.ExecutablePath ?? string.Empty,
                LastLaunchTime = DateTime.Now
            };

            _db.Applications.Add(app);
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "Anwendung hinzugefügt.";
            return RedirectToPage();
        }

    public async Task<IActionResult> OnPostAddOwnerAsync()
        {
            if (OwnerApplicationId == Guid.Empty || string.IsNullOrWhiteSpace(OwnerUserId))
            {
                TempData["ErrorMessage"] = "App und Besitzer müssen gewählt werden.";
                return RedirectToPage();
            }

            var user = await _userManager.FindByIdAsync(OwnerUserId);
            if (user == null)
            {
                TempData["ErrorMessage"] = "Benutzer nicht gefunden.";
                return RedirectToPage();
            }

            var ownership = new AppManager.Models.AppOwnership
            {
                ApplicationId = OwnerApplicationId,
                UserId = OwnerUserId,
                WindowsUsername = user.UserName ?? string.Empty,
                IISAppPoolName = _db.Applications.Where(a => a.Id == OwnerApplicationId).Select(a => a.IISAppPoolName).FirstOrDefault() ?? string.Empty,
                CreatedAt = DateTime.Now,
                CreatedBy = User?.Identity?.Name ?? "system"
            };

            _db.AppOwnerships.Add(ownership);
            await _db.SaveChangesAsync();
            TempData["SuccessMessage"] = "App-Owner zugewiesen.";
            return RedirectToPage();
        }

        // Korrigiere die Vergleiche von Guid und int zu Guid und Guid
        public async Task<IActionResult> OnPostStart(Guid applicationId)
        {
            // Authorization: only App-Owner or global admin allowed
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                TempData["ErrorMessage"] = "Sie müssen angemeldet sein, um diese Aktion durchzuführen.";
                return RedirectToPage();
            }

            bool allowed = currentUser.IsGlobalAdmin || _db.AppOwnerships.Any(o => o.ApplicationId == applicationId && o.UserId == currentUser.Id);
            if (!allowed)
            {
                TempData["ErrorMessage"] = "Nur der App-Owner oder ein Administrator darf diese Aktion ausführen.";
                return RedirectToPage();
            }

            // Load the application from the DB to ensure we have a fresh instance
            var app = _db.Applications.FirstOrDefault(a => a.Id == applicationId);
            if (app != null && app.IsIISApplication)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(app.IISAppPoolName))
                    {
                        TempData["ErrorMessage"] = "IIS-AppPool-Name fehlt für diese Anwendung.";
                        return RedirectToPage();
                    }

                    using var server = new ServerManager();
                    var pool = server.ApplicationPools[app.IISAppPoolName];
                    pool?.Start();
                    TempData["SuccessMessage"] = "Anwendung gestartet.";
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    _logger.LogError(uaEx, "Keine Berechtigung zum Starten des AppPools {Pool}", app.IISAppPoolName);
                    TempData["ErrorMessage"] = "Keine Berechtigung, IIS-Konfiguration zu ändern. Starten Sie die App als Benutzer mit ausreichenden Rechten auf dem IIS-Host.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fehler beim Starten des AppPools {Pool}", app.IISAppPoolName);
                    TempData["ErrorMessage"] = "Fehler beim Starten des AppPools. Details im Log.";
                }
            }
            else
            {
                TempData["ErrorMessage"] = "Anwendung nicht gefunden oder ist keine IIS-Anwendung.";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostStop(Guid applicationId)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                TempData["ErrorMessage"] = "Sie müssen angemeldet sein, um diese Aktion durchzuführen.";
                return RedirectToPage();
            }

            bool allowed = currentUser.IsGlobalAdmin || _db.AppOwnerships.Any(o => o.ApplicationId == applicationId && o.UserId == currentUser.Id);
            if (!allowed)
            {
                TempData["ErrorMessage"] = "Nur der App-Owner oder ein Administrator darf diese Aktion ausführen.";
                return RedirectToPage();
            }

            var app = _db.Applications.FirstOrDefault(a => a.Id == applicationId);
            if (app != null && app.IsIISApplication)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(app.IISAppPoolName))
                    {
                        TempData["ErrorMessage"] = "IIS-AppPool-Name fehlt für diese Anwendung.";
                        return RedirectToPage();
                    }

                    using var server = new ServerManager();
                    var pool = server.ApplicationPools[app.IISAppPoolName];
                    pool?.Stop();
                    TempData["SuccessMessage"] = "Anwendung gestoppt.";
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    _logger.LogError(uaEx, "Keine Berechtigung zum Stoppen des AppPools {Pool}", app.IISAppPoolName);
                    TempData["ErrorMessage"] = "Keine Berechtigung, IIS-Konfiguration zu ändern.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fehler beim Stoppen des AppPools {Pool}", app.IISAppPoolName);
                    TempData["ErrorMessage"] = "Fehler beim Stoppen des AppPools. Details im Log.";
                }
            }
            else
            {
                TempData["ErrorMessage"] = "Anwendung nicht gefunden oder ist keine IIS-Anwendung.";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRestart(Guid applicationId)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                TempData["ErrorMessage"] = "Sie müssen angemeldet sein, um diese Aktion durchzuführen.";
                return RedirectToPage();
            }

            bool allowed = currentUser.IsGlobalAdmin || _db.AppOwnerships.Any(o => o.ApplicationId == applicationId && o.UserId == currentUser.Id);
            if (!allowed)
            {
                TempData["ErrorMessage"] = "Nur der App-Owner oder ein Administrator darf diese Aktion ausführen.";
                return RedirectToPage();
            }

            var app = _db.Applications.FirstOrDefault(a => a.Id == applicationId);
            if (app != null && app.IsIISApplication)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(app.IISAppPoolName))
                    {
                        TempData["ErrorMessage"] = "IIS-AppPool-Name fehlt für diese Anwendung.";
                        return RedirectToPage();
                    }

                    using var server = new ServerManager();
                    var pool = server.ApplicationPools[app.IISAppPoolName];
                    pool?.Recycle();
                    TempData["SuccessMessage"] = "Anwendung neugestartet.";
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    _logger.LogError(uaEx, "Keine Berechtigung zum Recycle des AppPools {Pool}", app.IISAppPoolName);
                    TempData["ErrorMessage"] = "Keine Berechtigung, IIS-Konfiguration zu ändern.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fehler beim Recycle des AppPools {Pool}", app.IISAppPoolName);
                    TempData["ErrorMessage"] = "Fehler beim Recycle des AppPools. Details im Log.";
                }
            }
            else
            {
                TempData["ErrorMessage"] = "Anwendung nicht gefunden oder ist keine IIS-Anwendung.";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRecycle(Guid applicationId)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                TempData["ErrorMessage"] = "Sie müssen angemeldet sein, um diese Aktion durchzuführen.";
                return RedirectToPage();
            }

            bool allowed = currentUser.IsGlobalAdmin || _db.AppOwnerships.Any(o => o.ApplicationId == applicationId && o.UserId == currentUser.Id);
            if (!allowed)
            {
                TempData["ErrorMessage"] = "Nur der App-Owner oder ein Administrator darf diese Aktion ausführen.";
                return RedirectToPage();
            }

            var app = _db.Applications.FirstOrDefault(a => a.Id == applicationId);
            if (app != null && app.IsIISApplication)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(app.IISAppPoolName))
                    {
                        TempData["ErrorMessage"] = "IIS-AppPool-Name fehlt für diese Anwendung.";
                        return RedirectToPage();
                    }

                    using var server = new ServerManager();
                    var pool = server.ApplicationPools[app.IISAppPoolName];
                    pool?.Recycle();
                    TempData["SuccessMessage"] = "AppPool recycelt.";
                }
                catch (UnauthorizedAccessException uaEx)
                {
                    _logger.LogError(uaEx, "Keine Berechtigung zum Recycle des AppPools {Pool}", app.IISAppPoolName);
                    TempData["ErrorMessage"] = "Keine Berechtigung, IIS-Konfiguration zu ändern.";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fehler beim Recycle des AppPools {Pool}", app.IISAppPoolName);
                    TempData["ErrorMessage"] = "Fehler beim Recycle des AppPools. Details im Log.";
                }
            }
            else
            {
                TempData["ErrorMessage"] = "Anwendung nicht gefunden oder ist keine IIS-Anwendung.";
            }
            return RedirectToPage();
        }

        private List<AppManager.Models.Application> GetIISApplications()
        {
            var result = new List<AppManager.Models.Application>();
            try
            {
                using var server = new ServerManager();
                foreach (var site in server.Sites)
                {
                    foreach (var app in site.Applications)
                    {
                        bool isStarted = false;
                        try
                        {
                            isStarted = site.State == ObjectState.Started;
                        }
                        catch (NotImplementedException)
                        {
                            // Fallback: Status nicht verfügbar
                            isStarted = false;
                        }

                        result.Add(new AppManager.Models.Application
                        {
                            Id = Guid.NewGuid(),
                            Name = app.Path,
                            IsIISApplication = true,
                            IISAppPoolName = app.ApplicationPoolName,
                            IsStarted = isStarted,
                            LastLaunchTime = DateTime.Now
                        });
                    }
                }
            }
            catch (UnauthorizedAccessException uaEx)
            {
                _logger.LogError(uaEx, "Fehler beim Laden der IIS-Anwendungen aufgrund fehlender Berechtigungen");
                IisErrorMessage = "Fehler: Die IIS-Konfigurationsdatei kann nicht gelesen werden (unzureichende Berechtigungen).";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Laden der IIS-Anwendungen");
                IisErrorMessage = "Fehler beim Laden der IIS-Anwendungen. Details im Log.";
            }
            return result;
        }

        private void LoadCpuData()
        {
            CpuLoads.Clear();
            AppPoolNames.Clear();
            try
            {
                using var server = new ServerManager();
                foreach (var pool in server.ApplicationPools)
                {
                    AppPoolNames.Add(pool.Name);
                    float cpu = GetCpuUsageForAppPool(pool.Name);
                    CpuLoads.Add(cpu);
                }
            }
            catch (UnauthorizedAccessException uaEx)
            {
                _logger.LogError(uaEx, "Keine Berechtigung zum Lesen der IIS-AppPools");
                IisErrorMessage = "Fehler: Keine Berechtigung, IIS-AppPools zu lesen. Führen Sie die App auf dem IIS-Host als Admin aus.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Laden der CPU-Daten für IIS-AppPools");
                IisErrorMessage = "Fehler beim Laden der CPU-Daten. Details im Log.";
            }
        }

        // Helper used by the Razor view to show/hide action buttons
        public bool CanManage(Guid applicationId)
        {
            if (CurrentUserIsGlobalAdmin) return true;
            return OwnedApplicationIds.Contains(applicationId);
        }

        // Try to resolve the current AppUser from the ClaimsPrincipal.
        // If Identity isn't available but Windows authentication provides a name,
        // attempt to auto-provision a minimal local AppUser so ownership checks work.
    private async Task<AppManager.Data.AppUser> ResolveCurrentAppUserAsync()
        {
            // First, normal Identity-backed user
            var identityUser = await _userManager.GetUserAsync(User);
            if (identityUser != null) return identityUser;

            // If not present, try to use the Windows identity name (e.g. DOMAIN\user)
            var windowsName = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(windowsName)) return null;

            // Try to find a matching AppUser by UserName
            var existing = _userManager.Users.FirstOrDefault(u => u.UserName == windowsName);
            if (existing != null) return existing;

            // Auto-provision a minimal user record (no password, local account marker)
            var newUser = new AppManager.Data.AppUser
            {
                UserName = windowsName,
                Vorname = windowsName,
                Nachname = string.Empty,
                Email = string.Empty,
                EmailConfirmed = true,
                IsActive = true,
                IsGlobalAdmin = false
            };

            // Create with a random password (account won't be used for login when Windows Auth is enabled)
            var pwd = Guid.NewGuid().ToString() + "aA1!";
            var createResult = await _userManager.CreateAsync(newUser, pwd);
            if (createResult.Succeeded)
            {
                return newUser;
            }

            // If creation fails, fallback to null
            _logger.LogWarning("Could not auto-provision AppUser for Windows principal {Name}: {Errors}", windowsName, string.Join(";", createResult.Errors.Select(e => e.Description)));
            return null;
        }

        private float GetCpuUsageForAppPool(string appPoolName)
        {
#if WINDOWS
            // Beispiel: PerformanceCounter für IIS AppPool CPU-Last
            try
            {
                using var cpuCounter = new PerformanceCounter("Process", "% Processor Time", appPoolName, true);
                return cpuCounter.NextValue();
            }
            catch (System.Runtime.InteropServices.COMException comEx)
            {
                _logger.LogDebug(comEx, "PerformanceCounter für {Pool} nicht verfügbar", appPoolName);
                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Fehler beim Auslesen der CPU-Last für {Pool}", appPoolName);
                return 0;
            }
#else
            // Nicht unterstützt auf anderen Plattformen
            return 0;
#endif
        }
    }
}