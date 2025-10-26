using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AppManager.Data;
using AppManager.Models;
using AppManager.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System;
<<<<<<< HEAD
using DataAppUser = AppManager.Data.AppUser;

namespace AppManager.Pages.Admin
{
    [Authorize] // Erlaubt alle authentifizierten Windows-Benutzer
=======
using System.Security.Claims;

namespace AppManager.Pages.Admin
{
    [Authorize(Policy = "AppOwnerOrAdmin")]
>>>>>>> 1e808df
    public class DashboardModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly UserManager<DataAppUser> _userManager;
        private readonly ProgramManagerService _programManager;
        private readonly AppManager.AppAuthorizationService _authService;

<<<<<<< HEAD
        public DashboardModel(AppDbContext context, UserManager<DataAppUser> userManager, ProgramManagerService programManager)
=======
        public DashboardModel(AppDbContext context, UserManager<AppUser> userManager, ProgramManagerService programManager, AppManager.AppAuthorizationService authService)
>>>>>>> 1e808df
        {
            _context = context;
            _userManager = userManager;
            _programManager = programManager;
            _authService = authService;
        }

        public List<Application> Applications { get; set; } = new();
        public List<AppLaunchHistory> LaunchHistory { get; set; } = new();
        public Dictionary<Guid, bool> RestartRequiredMap { get; set; } = new();

        public async Task OnGetAsync()
        {
            Console.WriteLine("🔍 Dashboard OnGetAsync wird ausgeführt...");

            // Only show IIS-managed applications on the admin dashboard
            Applications = await _context.Applications
                .Where(a => a.IsIISApplication)
                .OrderBy(a => a.Name)
                .ToListAsync();
            Console.WriteLine($"📱 {Applications.Count} Anwendungen geladen");

            LaunchHistory = await _context.AppLaunchHistories
                .Include(h => h.Application)
                .Include(h => h.User)
                .OrderByDescending(h => h.LaunchTime)
                .Take(100) // Lade mehr Historie für bessere Analyse
                .ToListAsync();
            Console.WriteLine($"📝 {LaunchHistory.Count} Historie-Einträge geladen");

            // 🧠 Intelligente "Neustart erforderlich" Logik basierend auf Historie
            foreach (var app in Applications)
            {
                RestartRequiredMap[app.Id] = IsRestartRequired(app, LaunchHistory);
                // Aktualisiere das Application-Objekt mit der berechneten Logik
                app.RestartRequired = RestartRequiredMap[app.Id];
            }
        }

        // NOTE: The previous handler that removed Windows apps has been intentionally removed
        // to ensure applications are not deleted from the database. Management of non-IIS
        // applications remains available via the ApplicationManagement page and IISManager.

        /// <summary>
        ///Intelligente Logik: Bestimmt ob ein Neustart erforderlich ist
        /// basierend auf der Start-Historie der letzten Aktionen
        /// </summary>
        private bool IsRestartRequired(Application app, List<AppLaunchHistory> allHistory)
        {
            // Filtere die letzten 5 Einträge für diese spezifische App
            var recentHistory = allHistory
                .Where(h => h.ApplicationId == app.Id)
                .OrderByDescending(h => h.LaunchTime)
                .Take(5)
                .ToList();

            if (!recentHistory.Any())
            {
                // Keine Historie = kein Neustart erforderlich
                return false;
            }

            // 🔍 Prüfe verschiedene Bedingungen für "Neustart erforderlich"
            bool hasFailedStart = recentHistory.Any(h =>
                h.Action == "Start" && h.Reason.Contains("fehlgeschlagen", StringComparison.OrdinalIgnoreCase));

            bool hasFailedRestart = recentHistory.Any(h =>
                h.Action == "Restart" && h.Reason.Contains("fehlgeschlagen", StringComparison.OrdinalIgnoreCase));

            bool hasMultipleFailures = recentHistory.Count(h =>
                h.Reason.Contains("fehlgeschlagen", StringComparison.OrdinalIgnoreCase)) >= 2;

            //Neustart erforderlich wenn:
            // - Letzter Start fehlgeschlagen ODER
            // - Letzter Restart fehlgeschlagen ODER  
            // - Mehrere Fehlschläge in letzten 5 Aktionen
            bool restartRequired = hasFailedStart || hasFailedRestart || hasMultipleFailures;

            if (restartRequired)
            {
                Console.WriteLine($"🏁🚦{app.Name}: Neustart erforderlich (Failed Start: {hasFailedStart}, Failed Restart: {hasFailedRestart}, Multiple Failures: {hasMultipleFailures})");
            }

            return restartRequired;
        }

        // START-HANDLER mit Berechtigungs-Prüfung
        public async Task<IActionResult> OnPostStartAsync(Guid appId, string customReason = "")
        {
            Console.WriteLine($"🚦🚘 START-Handler aufgerufen für App: {appId}");

            var app = await _context.Applications.FindAsync(appId);
            if (app == null)
            {
                TempData["Error"] = "Anwendung nicht gefunden!";
                return RedirectToPage();
            }

            // Berechtigungs-Prüfung
            var currentUserId = User.FindFirst("AppUserId")?.Value;
            if (currentUserId == null)
            {
                TempData["Error"] = "Benutzer-ID nicht gefunden!";
                return RedirectToPage();
            }

            var currentUser = await _userManager.FindByIdAsync(currentUserId);
            if (currentUser == null || !_authService.HasAppAccess(currentUser, appId))
            {
                TempData["Error"] = "Keine Berechtigung für diese Anwendung!";
                return RedirectToPage();
            }

            // Echtes Programm starten
            bool success = await _programManager.StartProgramAsync(app);
            Console.WriteLine($"🎯 Start-Ergebnis: {success}");

<<<<<<< HEAD
=======
            var history = new AppLaunchHistory
            {
                ApplicationId = appId,
                UserId = currentUserId,
                LaunchTime = DateTime.Now,
                Action = "Start",
                Reason = success
                    ? (!string.IsNullOrWhiteSpace(customReason) ? customReason : "Dashboard-Start")
                    : "Start fehlgeschlagen",
                WindowsUsername = User.Identity?.Name ?? ""
            };

            _context.AppLaunchHistories.Add(history);
            await _context.SaveChangesAsync();

>>>>>>> 1e808df
            if (success)
            {
                TempData["Success"] = $"'{app.Name}' wurde erfolgreich gestartet!";
            }
            else
            {
                TempData["Error"] = $"'{app.Name}' konnte nicht gestartet werden.";
            }

            return RedirectToPage();
        }

        // STOP-HANDLER mit Berechtigungs-Prüfung
        public async Task<IActionResult> OnPostStopAsync(Guid appId, string customReason = "")
        {
<<<<<<< HEAD
            Console.WriteLine($"⏹️ STOP-Handler aufgerufen für App: {appId}");

            var app = await _context.Applications.FindAsync(appId);
            if (app == null)
            {
                Console.WriteLine($"❌ App mit ID {appId} nicht gefunden!");
                return NotFound();
            }

            Console.WriteLine($"📱 App gefunden: {app.Name}");

            bool success = await _programManager.StopProgramAsync(app);
            Console.WriteLine($"⏹️ Stop-Ergebnis: {success}");

            // ❌ ENTFERNT: Doppelte History-Erstellung!
            // Der ProgramManagerService.StopProgramAsync() macht das bereits automatisch

            if (success)
            {
                TempData["Success"] = $"'{app.Name}' wurde erfolgreich gestoppt!";
            }
            else
            {
                TempData["Error"] = $"'{app.Name}' konnte nicht gestoppt werden.";
            }

            return RedirectToPage();
        }

        // 🔍 DEBUG-VERSION: RESTART-HANDLER
        public async Task<IActionResult> OnPostRestartAsync(Guid appId, string customReason = "")
        {
            Console.WriteLine($"🔄 RESTART-Handler aufgerufen für App: {appId}");

=======
>>>>>>> 1e808df
            var app = await _context.Applications.FindAsync(appId);
            if (app == null) return NotFound();

            // Berechtigungs-Prüfung
            var currentUserId = User.FindFirst("AppUserId")?.Value;
            if (currentUserId == null)
            {
                TempData["Error"] = "Benutzer-ID nicht gefunden!";
                return RedirectToPage();
            }

            var currentUser = await _userManager.FindByIdAsync(currentUserId);
            if (currentUser == null || !_authService.HasAppAccess(currentUser, appId))
            {
                TempData["Error"] = "Keine Berechtigung für diese Anwendung!";
                return RedirectToPage();
            }

            bool success = await _programManager.StopProgramAsync(app);

<<<<<<< HEAD
            if (success)
=======
            var history = new AppLaunchHistory
            {
                ApplicationId = appId,
                UserId = currentUserId,
                LaunchTime = DateTime.Now,
                Action = "Stop",
                Reason = success
                    ? (!string.IsNullOrWhiteSpace(customReason) ? customReason : "Dashboard-Stop")
                    : "Stop fehlgeschlagen",
                WindowsUsername = User.Identity?.Name ?? ""
            };

            _context.AppLaunchHistories.Add(history);
            await _context.SaveChangesAsync();

            TempData[success ? "Success" : "Error"] = $"'{app.Name}' " + 
                (success ? "wurde erfolgreich gestoppt!" : "konnte nicht gestoppt werden.");

            return RedirectToPage();
        }

        // RESTART-HANDLER mit Berechtigungs-Prüfung
        public async Task<IActionResult> OnPostRestartAsync(Guid appId, string customReason = "")
        {
            var app = await _context.Applications.FindAsync(appId);
            if (app == null) return NotFound();

            // Berechtigungs-Prüfung
            var currentUserId = User.FindFirst("AppUserId")?.Value;
            if (currentUserId == null)
>>>>>>> 1e808df
            {
                TempData["Error"] = "Benutzer-ID nicht gefunden!";
                return RedirectToPage();
            }

            var currentUser = await _userManager.FindByIdAsync(currentUserId);
            if (currentUser == null || !_authService.HasAppAccess(currentUser, appId))
            {
                TempData["Error"] = "Keine Berechtigung für diese Anwendung!";
                return RedirectToPage();
            }

            bool success = await _programManager.RestartProgramAsync(app);

            var history = new AppLaunchHistory
            {
                ApplicationId = appId,
                UserId = currentUserId,
                LaunchTime = DateTime.Now,
                Action = "Restart",
                Reason = success
                    ? (!string.IsNullOrWhiteSpace(customReason) ? customReason : "Dashboard-Restart")
                    : "Restart fehlgeschlagen",
                WindowsUsername = User.Identity?.Name ?? ""
            };

            _context.AppLaunchHistories.Add(history);
            await _context.SaveChangesAsync();

            TempData[success ? "Success" : "Error"] = $"'{app.Name}' " + 
                (success ? "wurde erfolgreich neugestartet!" : "konnte nicht neugestartet werden.");

            return RedirectToPage();
        }
    }
}
