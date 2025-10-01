using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using AppManager.Models;
using System.Collections.Generic;
using System.Linq;
using System;
using AppManager.Services;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace AppManager.Pages.Admin
{
    [Authorize]
    public class ApplicationManagementModel : PageModel
    {
        private readonly ILogger<ApplicationManagementModel> _logger;
        private readonly AppManager.Data.AppDbContext _db;
        private readonly UserManager<AppManager.Data.AppUser> _userManager;
    private readonly AppService _appService;
    private readonly ProgramManagerService _programManager;

        public ApplicationManagementModel(ILogger<ApplicationManagementModel> logger, AppManager.Data.AppDbContext db, UserManager<AppManager.Data.AppUser> userManager, AppService appService, ProgramManagerService programManager)
        {
            _logger = logger;
            _db = db;
            _userManager = userManager;
            _appService = appService;
            _programManager = programManager;
        }

        public List<AppManager.Models.Application> Applications { get; set; } = new();
        public AppManager.Models.Application NewApplication { get; set; } = new();
    // CPU load display for Windows apps was removed per request
        public string IisErrorMessage { get; set; } = string.Empty;
        public bool IisAvailable { get; set; } = false;
        // Load page data and authorization info
        public async Task OnGetAsync()
        {
            try
            {
                // Only load IIS-managed applications here. Non-IIS apps are managed in IIS Manager.
                Applications = await _db.Applications
                    .Where(a => a.IsIISApplication)
                    .OrderBy(a => a.Name)
                    .ToListAsync();

                // Also include any IIS app pools discovered directly from IIS that aren't yet persisted
                var iisApps = await GetIISApplicationsAsync();
                Applications.AddRange(iisApps.Where(iis => !Applications.Any(db => db.IISAppPoolName == iis.IISAppPoolName)));

                // CPU data collection removed — IIS app controls remain (start/stop/recycle)

                // Load users for owner selection
                Users = await _userManager.Users.Where(u => u.IsActive).OrderBy(u => u.Vorname).ToListAsync();

                // Populate current user and ownership cache for UI authorization checks
                var currentUser = await ResolveCurrentAppUserAsync();
                if (currentUser != null)
                {
                    CurrentUserId = currentUser.Id;
                    CurrentUserIsGlobalAdmin = currentUser.IsGlobalAdmin;
                    var owned = await _db.AppOwnerships
                        .Where(o => o.UserId == currentUser.Id)
                        .Select(o => o.ApplicationId)
                        .ToListAsync();
                    OwnedApplicationIds = new HashSet<Guid>(owned);

                    // Load owners for all loaded applications (async, batched)
                    var appIds = Applications.Select(a => a.Id).ToList();
                    if (appIds.Count > 0)
                    {
                        var ownerEntries = await _db.AppOwnerships
                            .Where(o => appIds.Contains(o.ApplicationId))
                            .ToListAsync();

                        var userIds = ownerEntries.Select(o => o.UserId).Distinct().ToList();
                        var users = await _userManager.Users
                            .Where(u => userIds.Contains(u.Id))
                            .Select(u => new { u.Id, u.Vorname, u.Nachname, u.UserName })
                            .ToListAsync();

                        var userDisplay = users.ToDictionary(u => u.Id, u => (u.Vorname + " " + u.Nachname).Trim() + (string.IsNullOrEmpty(u.UserName) ? "" : " (" + u.UserName + ")"));

                        ApplicationOwners = ownerEntries
                            .GroupBy(o => o.ApplicationId)
                            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(o => userDisplay.TryGetValue(o.UserId, out var d) ? d : o.WindowsUsername)));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading application management page");
                TempData["ErrorMessage"] = "Fehler beim Laden der Seite. Details im Log.";
            }
        }

    public List<AppManager.Data.AppUser> Users { get; set; } = new();
    
    // Mapping of ApplicationId -> comma-separated owner display names
    public Dictionary<Guid, string> ApplicationOwners { get; set; } = new();

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
            try
            {
                // Input validation
                if (string.IsNullOrWhiteSpace(BindNewApplication.Name))
                {
                    TempData["ErrorMessage"] = "Name ist erforderlich.";
                    return RedirectToPage();
                }

                // Sanitize and validate inputs
                var name = BindNewApplication.Name.Trim();
                var poolName = BindNewApplication.IISAppPoolName?.Trim() ?? string.Empty;
                var execPath = BindNewApplication.ExecutablePath?.Trim() ?? string.Empty;

                // Basic security validation for pool name (alphanumeric, dash, underscore)
                if (!string.IsNullOrEmpty(poolName) && !IsValidPoolName(poolName))
                {
                    TempData["ErrorMessage"] = "IIS AppPool-Name enthält ungültige Zeichen.";
                    return RedirectToPage();
                    
                    
                }

                // Basic path validation
                if (!string.IsNullOrEmpty(execPath) && !IsValidPath(execPath))
                {
                    TempData["ErrorMessage"] = "Pfad enthält ungültige Zeichen.";
                    return RedirectToPage();
                }

                var app = new AppManager.Models.Application
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    IISAppPoolName = poolName,
                    IsIISApplication = BindNewApplication.IsIISApplication,
                    ExecutablePath = execPath,
                    LastLaunchTime = DateTime.Now
                };

                _db.Applications.Add(app);
                await _db.SaveChangesAsync();
                
                _logger.LogInformation("Application {Name} added by user {User}", name, User?.Identity?.Name ?? "Unknown");
                TempData["SuccessMessage"] = "Anwendung hinzugefügt.";
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding application");
                TempData["ErrorMessage"] = "Fehler beim Hinzufügen der Anwendung. Details im Log.";
                return RedirectToPage();
            }
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
                IISAppPoolName = (await _db.Applications.Where(a => a.Id == OwnerApplicationId).Select(a => a.IISAppPoolName).FirstOrDefaultAsync()) ?? string.Empty,
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
            try
            {
                // Authorization: only App-Owner or global admin allowed
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                {
                    TempData["ErrorMessage"] = "Sie müssen angemeldet sein, um diese Aktion durchzuführen.";
                    return RedirectToPage();
                }

                bool allowed = currentUser.IsGlobalAdmin || 
                    await _db.AppOwnerships.AnyAsync(o => o.ApplicationId == applicationId && o.UserId == currentUser.Id);
                if (!allowed)
                {
                    TempData["ErrorMessage"] = "Nur der App-Owner oder ein Administrator darf diese Aktion ausführen.";
                    return RedirectToPage();
                }

                // Load the application from the DB to ensure we have a fresh instance
                var app = await _db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId);
                if (app == null)
                {
                    TempData["ErrorMessage"] = "Anwendung nicht gefunden.";
                    return RedirectToPage();
                }

                _logger.LogInformation("OnPostStart invoked for app {AppId} (IIS={IsIIS}) by user {User}", app.Id, app.IsIISApplication, currentUser.UserName);

                if (app.IsIISApplication)
                {
                    if (string.IsNullOrWhiteSpace(app.IISAppPoolName))
                    {
                        TempData["ErrorMessage"] = "IIS-AppPool-Name fehlt für diese Anwendung.";
                        return RedirectToPage();
                    }

                    if (!_appService.TryStartIisAppPoolWithVerification(app.IISAppPoolName, out var message))
                    {
                        _logger.LogWarning("IIS AppPool start failed for {Pool}: {Message}", app.IISAppPoolName, message);
                        TempData["ErrorMessage"] = message;
                    }
                    else
                    {
                        _logger.LogInformation("AppPool {Pool} successfully started by user {User}", app.IISAppPoolName, currentUser.UserName);
                        TempData["SuccessMessage"] = message;
                        await _programManager.LogAppActivityAsync(app, "IIS-Start", message);
                    }
                }
                else
                {
                    // Non-IIS: delegate to ProgramManagerService to start and record state
                    if (string.IsNullOrWhiteSpace(app.ExecutablePath))
                    {
                        TempData["ErrorMessage"] = "ExecutablePath fehlt für diese Anwendung.";
                        return RedirectToPage();
                    }

                    var started = await _programManager.StartProgramAsync(app);
                    if (!started)
                    {
                        TempData["ErrorMessage"] = "Fehler beim Starten der Anwendung. Details im Log.";
                    }
                    else
                    {
                        TempData["SuccessMessage"] = "Anwendung gestartet.";
                    }
                }
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting application {ApplicationId}", applicationId);
                TempData["ErrorMessage"] = "Fehler beim Starten der Anwendung. Details im Log.";
                return RedirectToPage();
            }
        }

        public async Task<IActionResult> OnPostStop(Guid applicationId)
        {
            try
            {
                var currentUser = await _userManager.GetUserAsync(User);
                if (currentUser == null)
                {
                    TempData["ErrorMessage"] = "Sie müssen angemeldet sein, um diese Aktion durchzuführen.";
                    return RedirectToPage();
                }

                bool allowed = currentUser.IsGlobalAdmin || 
                    await _db.AppOwnerships.AnyAsync(o => o.ApplicationId == applicationId && o.UserId == currentUser.Id);
                if (!allowed)
                {
                    TempData["ErrorMessage"] = "Nur der App-Owner oder ein Administrator darf diese Aktion ausführen.";
                    return RedirectToPage();
                }

                var app = await _db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId);
                if (app == null)
                {
                    TempData["ErrorMessage"] = "Anwendung nicht gefunden.";
                    return RedirectToPage();
                }

                _logger.LogInformation("OnPostStop invoked for app {AppId} (IIS={IsIIS}) by user {User}", app.Id, app.IsIISApplication, currentUser.UserName);

                if (app.IsIISApplication)
                {
                    if (string.IsNullOrWhiteSpace(app.IISAppPoolName))
                    {
                        TempData["ErrorMessage"] = "IIS-AppPool-Name fehlt für diese Anwendung.";
                        return RedirectToPage();
                    }

                    if (!_appService.TryStopIisAppPoolWithVerification(app.IISAppPoolName, out var message))
                    {
                        _logger.LogWarning("IIS AppPool stop failed for {Pool}: {Message}", app.IISAppPoolName, message);
                        TempData["ErrorMessage"] = message;
                    }
                    else
                    {
                        _logger.LogInformation("AppPool {Pool} successfully stopped by user {User}", app.IISAppPoolName, currentUser.UserName);
                        TempData["SuccessMessage"] = message;
                        await _programManager.LogAppActivityAsync(app, "IIS-Stop", message);
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(app.ExecutablePath))
                    {
                        TempData["ErrorMessage"] = "ExecutablePath fehlt für diese Anwendung.";
                        return RedirectToPage();
                    }

                    var stopped = await _programManager.StopProgramAsync(app);
                    if (!stopped)
                    {
                        TempData["ErrorMessage"] = "Fehler beim Stoppen der Anwendung. Details im Log.";
                    }
                    else
                    {
                        TempData["SuccessMessage"] = "Anwendung gestoppt.";
                    }
                }
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping application {ApplicationId}", applicationId);
                TempData["ErrorMessage"] = "Fehler beim Stoppen der Anwendung. Details im Log.";
                return RedirectToPage();
            }
        }

        public async Task<IActionResult> OnPostRestart(Guid applicationId)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null)
            {
                TempData["ErrorMessage"] = "Sie müssen angemeldet sein, um diese Aktion durchzuführen.";
                return RedirectToPage();
            }

            bool allowed = currentUser.IsGlobalAdmin || await _db.AppOwnerships.AnyAsync(o => o.ApplicationId == applicationId && o.UserId == currentUser.Id);
            if (!allowed)
            {
                TempData["ErrorMessage"] = "Nur der App-Owner oder ein Administrator darf diese Aktion ausführen.";
                return RedirectToPage();
            }

            var app = await _db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId);
            if (app == null)
            {
                TempData["ErrorMessage"] = "Anwendung nicht gefunden.";
                return RedirectToPage();
            }

            _logger.LogInformation("OnPostRestart invoked for app {AppId} (IIS={IsIIS}) by user {User}", app.Id, app.IsIISApplication, currentUser.UserName);

            if (app.IsIISApplication)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(app.IISAppPoolName))
                    {
                        TempData["ErrorMessage"] = "IIS-AppPool-Name fehlt für diese Anwendung.";
                        return RedirectToPage();
                    }

                    if (!_appService.TryRecycleIisAppPoolWithVerification(app.IISAppPoolName, out var message))
                    {
                        _logger.LogWarning("IIS AppPool recycle failed for {Pool}: {Message}", app.IISAppPoolName, message);
                        TempData["ErrorMessage"] = message;
                    }
                    else
                    {
                        _logger.LogInformation("AppPool {Pool} successfully recycled by user {User}", app.IISAppPoolName, currentUser.UserName);
                        TempData["SuccessMessage"] = message;
                        await _programManager.LogAppActivityAsync(app, "IIS-Recycle", message);
                    }
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
                if (string.IsNullOrWhiteSpace(app.ExecutablePath))
                {
                    TempData["ErrorMessage"] = "ExecutablePath fehlt für diese Anwendung.";
                    return RedirectToPage();
                }

                var restarted = await _programManager.RestartProgramAsync(app);
                if (!restarted)
                {
                    TempData["ErrorMessage"] = "Fehler beim Neustarten der Anwendung. Details im Log.";
                }
                else
                {
                    TempData["SuccessMessage"] = "Anwendung neugestartet.";
                }
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

            bool allowed = currentUser.IsGlobalAdmin || await _db.AppOwnerships.AnyAsync(o => o.ApplicationId == applicationId && o.UserId == currentUser.Id);
            if (!allowed)
            {
                TempData["ErrorMessage"] = "Nur der App-Owner oder ein Administrator darf diese Aktion ausführen.";
                return RedirectToPage();
            }

            var app = await _db.Applications.FirstOrDefaultAsync(a => a.Id == applicationId);
            if (app != null && app.IsIISApplication)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(app.IISAppPoolName))
                    {
                        TempData["ErrorMessage"] = "IIS-AppPool-Name fehlt für diese Anwendung.";
                        return RedirectToPage();
                    }

                    if (!_appService.TryRecycleIisAppPool(app.IISAppPoolName, out var err))
                    {
                        _logger.LogWarning("TryRecycleIisAppPool failed for {Pool}: {Error}", app.IISAppPoolName, err);
                        TempData["ErrorMessage"] = string.IsNullOrWhiteSpace(err) ? "Fehler beim Recycle des AppPools." : err;
                    }
                    else
                    {
                        TempData["SuccessMessage"] = "AppPool recycelt.";
                        await _programManager.LogAppActivityAsync(app, "IIS-Recycle", "AppPool recycelt");
                    }
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

        private Task<List<AppManager.Models.Application>> GetIISApplicationsAsync()
        {
            var result = new List<AppManager.Models.Application>();
            try
            {
                if (!_appService.TryListIisAppPools(out var pools, out var err))
                {
                    _logger.LogWarning("TryListIisAppPools failed: {Error}", err);
                    IisErrorMessage = string.IsNullOrWhiteSpace(err) ? "Fehler beim Laden der IIS-Anwendungen." : err;
                    IisAvailable = false;
                    return Task.FromResult(result);
                }

                IisAvailable = true;  // IIS ist verfügbar
                foreach (var p in pools)
                {
                    result.Add(new AppManager.Models.Application
                    {
                        Id = Guid.NewGuid(),
                        Name = p.Name,
                        IsIISApplication = true,
                        IISAppPoolName = p.Name,
                        IsStarted = string.Equals(p.State, "Started", StringComparison.OrdinalIgnoreCase),
                        LastLaunchTime = DateTime.Now
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler beim Laden der IIS-Anwendungen");
                IisErrorMessage = "Fehler beim Laden der IIS-Anwendungen. Details im Log.";
                IisAvailable = false;
            }
            return Task.FromResult(result);
        }

        // Helper used by the Razor view to show/hide action buttons
        public bool CanManage(Guid applicationId)
        {
            if (CurrentUserIsGlobalAdmin) return true;
            return OwnedApplicationIds.Contains(applicationId);
        }

        // Try to resolve the current AppUser from the ClaimsPrincipal.
        // Note: Auto-provisioning is disabled by default for security.
        // Enable only in development or with explicit configuration.
        private async Task<AppManager.Data.AppUser> ResolveCurrentAppUserAsync()
        {
            // First, normal Identity-backed user
            var identityUser = await _userManager.GetUserAsync(User);
            if (identityUser != null) return identityUser;

            // If not present, try to use the Windows identity name (e.g. DOMAIN\user)
            var windowsName = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(windowsName)) return null;

            // Try to find a matching AppUser by UserName
            var existing = await _userManager.Users.FirstOrDefaultAsync(u => u.UserName == windowsName);
            if (existing != null) return existing;

            // Auto-provisioning: Only enable in development or with explicit setting
            // This prevents unauthorized account creation in production
            var allowAutoProvision = false; // TODO: Read from configuration
            if (!allowAutoProvision)
            {
                _logger.LogInformation("Auto-provisioning disabled for Windows user {WindowsName}", windowsName);
                return null;
            }

            try
            {
                // Auto-provision a minimal user record (no password, local account marker)
                var newUser = new AppManager.Data.AppUser
                {
                    UserName = windowsName,
                    Vorname = windowsName.Split('\\').LastOrDefault() ?? windowsName,
                    Nachname = "Auto-provisioned",
                    Email = $"{windowsName.Replace('\\', '.')}@local",
                    EmailConfirmed = true,
                    IsActive = true,
                    IsGlobalAdmin = false
                };

                // Create with a random password (account won't be used for login when Windows Auth is enabled)
                var pwd = Guid.NewGuid().ToString() + "aA1!";
                var createResult = await _userManager.CreateAsync(newUser, pwd);
                if (createResult.Succeeded)
                {
                    _logger.LogInformation("Auto-provisioned AppUser for Windows principal {Name}", windowsName);
                    return newUser;
                }

                // If creation fails, log and fallback to null
                _logger.LogWarning("Could not auto-provision AppUser for Windows principal {Name}: {Errors}", 
                    windowsName, string.Join(";", createResult.Errors.Select(e => e.Description)));
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error auto-provisioning user for {WindowsName}", windowsName);
                return null;
            }
        }

        private float GetCpuUsageForAppPool(string appPoolName)
        {
            // CPU collection removed; method retained for compatibility but returns 0
            return 0;
        }

        // Input validation helpers
        private static bool IsValidPoolName(string poolName)
        {
            if (string.IsNullOrWhiteSpace(poolName)) return false;
            // Allow alphanumeric, dash, underscore, dot
            return poolName.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.');
        }

        private static bool IsValidPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            try
            {
                // Basic validation - check for invalid path characters
                var invalidChars = System.IO.Path.GetInvalidPathChars();
                return !path.Any(c => invalidChars.Contains(c));
            }
            catch
            {
                return false;
            }
        }
    }
}