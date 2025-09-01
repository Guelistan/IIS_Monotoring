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

namespace AppManager.Pages.Admin
{
    [Authorize]
    public class DashboardModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly ProgramManagerService _programManager;

        public DashboardModel(AppDbContext context, UserManager<AppUser> userManager, ProgramManagerService programManager)
        {
            _context = context;
            _userManager = userManager;
            _programManager = programManager;
        }

        public List<Application> Applications { get; set; } = new();
        public List<AppLaunchHistory> LaunchHistory { get; set; } = new();
        public Dictionary<Guid, bool> RestartRequiredMap { get; set; } = new();

        public async Task OnGetAsync()
        {
            Console.WriteLine("🔍 Dashboard OnGetAsync wird ausgeführt...");

            Applications = await _context.Applications.ToListAsync();
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

        //DEBUG-VERSION: START-HANDLER
        public async Task<IActionResult> OnPostStartAsync(Guid appId, string customReason = "")
        {
            Console.WriteLine($"🚦🚘 START-Handler aufgerufen für App: {appId}");
            Console.WriteLine($"🙎 🙎‍♀️ CustomReason: '{customReason}'");

            var app = await _context.Applications.FindAsync(appId);
            if (app == null)
            {
                Console.WriteLine($"❌ App mit ID {appId} nicht gefunden!");
                TempData["Error"] = "Anwendung nicht gefunden!";
                return RedirectToPage();
            }

            Console.WriteLine($" 🛰️ App gefunden: {app.Name} - {app.ExecutablePath}");

            // Echtes Programm starten
            bool success = await _programManager.StartProgramAsync(app);
            Console.WriteLine($"🎯 Start-Ergebnis: {success}");

            var currentUserId = _userManager.GetUserId(User) ?? string.Empty;
            Console.WriteLine($"🙎‍♀️ Current User ID: {currentUserId}");

            var history = new AppLaunchHistory
            {
                ApplicationId = appId,
                UserId = currentUserId,
                LaunchTime = DateTime.Now,
                Action = "Start",
                Reason = success
                    ? (!string.IsNullOrWhiteSpace(customReason) ? customReason : "Manuell gestartet")
                    : "Start fehlgeschlagen"
            };

            Console.WriteLine($"📝 Historie-Eintrag erstellt:");
            Console.WriteLine($"   - ApplicationId: {history.ApplicationId}");
            Console.WriteLine($"   - UserId: {history.UserId}");
            Console.WriteLine($"   - Action: {history.Action}");
            Console.WriteLine($"   - Reason: {history.Reason}");
            Console.WriteLine($"   - LaunchTime: {history.LaunchTime}");

            try
            {
                _context.AppLaunchHistories.Add(history);
                var saveResult = await _context.SaveChangesAsync();
                Console.WriteLine($"💾 SaveChanges Result: {saveResult} Zeilen betroffen");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ FEHLER beim Speichern der Historie: {ex.Message}");
                Console.WriteLine($"❌ StackTrace: {ex.StackTrace}");
            }

            if (success)
            {
                TempData["Success"] = $"'{app.Name}' wurde erfolgreich gestartet!";
                Console.WriteLine($"✅ Success-Message gesetzt");
            }
            else
            {
                TempData["Error"] = $"'{app.Name}' konnte nicht gestartet werden.";
                Console.WriteLine($"❌ Error-Message gesetzt");
            }

            Console.WriteLine($"🔄 Redirect to Page...");
            return RedirectToPage();
        }

        // 🔍 DEBUG-VERSION: STOP-HANDLER
        public async Task<IActionResult> OnPostStopAsync(Guid appId, string customReason = "")
        {
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

            var history = new AppLaunchHistory
            {
                ApplicationId = appId,
                UserId = _userManager.GetUserId(User) ?? string.Empty,
                LaunchTime = DateTime.Now,
                Action = "Stop",
                Reason = success
                    ? (!string.IsNullOrWhiteSpace(customReason) ? customReason : "Manuell gestoppt")
                    : "Stop fehlgeschlagen"
            };

            Console.WriteLine($"📝 Stop-Historie-Eintrag erstellt");

            _context.AppLaunchHistories.Add(history);
            await _context.SaveChangesAsync();

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

            var app = await _context.Applications.FindAsync(appId);
            if (app == null) return NotFound();

            Console.WriteLine($"📱 App gefunden: {app.Name}");

            bool success = await _programManager.RestartProgramAsync(app);
            Console.WriteLine($"🔄 Restart-Ergebnis: {success}");

            var history = new AppLaunchHistory
            {
                ApplicationId = appId,
                UserId = _userManager.GetUserId(User) ?? string.Empty,
                LaunchTime = DateTime.Now,
                Action = "Restart",
                Reason = success
                    ? (!string.IsNullOrWhiteSpace(customReason) ? customReason : "Manuell neugestartet")
                    : "Restart fehlgeschlagen"
            };

            Console.WriteLine($"📝 Restart-Historie-Eintrag erstellt");

            _context.AppLaunchHistories.Add(history);
            await _context.SaveChangesAsync();

            if (success)
            {
                TempData["Success"] = $"'{app.Name}' wurde erfolgreich neugestartet!";
            }
            else
            {
                TempData["Error"] = $"'{app.Name}' konnte nicht neugestartet werden.";
            }

            return RedirectToPage();
        }
    }
}
