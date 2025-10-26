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
using System.Security.Claims;

namespace AppManager.Pages.Admin
{
    [Authorize(Policy = "AppOwnerOrAdmin")]
    public class DashboardModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly ProgramManagerService _programManager;
        private readonly AppManager.AppAuthorizationService _authService;

        public DashboardModel(AppDbContext context, UserManager<AppUser> userManager, ProgramManagerService programManager, AppManager.AppAuthorizationService authService)
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
            // Only show IIS-managed applications on the admin dashboard
            Applications = await _context.Applications
                .Where(a => a.IsIISApplication)
                .OrderBy(a => a.Name)
                .ToListAsync();

            LaunchHistory = await _context.AppLaunchHistories
                .Include(h => h.Application)
                .Include(h => h.User)
                .OrderByDescending(h => h.LaunchTime)
                .Take(100)
                .ToListAsync();

            foreach (var app in Applications)
            {
                RestartRequiredMap[app.Id] = IsRestartRequired(app, LaunchHistory);
                app.RestartRequired = RestartRequiredMap[app.Id];
            }
        }

        private bool IsRestartRequired(Application app, List<AppLaunchHistory> allHistory)
        {
            var recentHistory = allHistory
                .Where(h => h.ApplicationId == app.Id)
                .OrderByDescending(h => h.LaunchTime)
                .Take(5)
                .ToList();

            if (!recentHistory.Any()) return false;

            bool hasFailedStart = recentHistory.Any(h => h.Action == "Start" && h.Reason.Contains("fehlgeschlagen", StringComparison.OrdinalIgnoreCase));
            bool hasFailedRestart = recentHistory.Any(h => h.Action == "Restart" && h.Reason.Contains("fehlgeschlagen", StringComparison.OrdinalIgnoreCase));
            bool hasMultipleFailures = recentHistory.Count(h => h.Reason.Contains("fehlgeschlagen", StringComparison.OrdinalIgnoreCase)) >= 2;

            return hasFailedStart || hasFailedRestart || hasMultipleFailures;
        }

        public async Task<IActionResult> OnPostStartAsync(Guid appId, string customReason = "")
        {
            var app = await _context.Applications.FindAsync(appId);
            if (app == null) return NotFound();

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

            bool success = await _programManager.StartProgramAsync(app);

            var history = new AppLaunchHistory
            {
                ApplicationId = appId,
                UserId = currentUserId,
                LaunchTime = DateTime.Now,
                Action = "Start",
                Reason = success ? (!string.IsNullOrWhiteSpace(customReason) ? customReason : "Dashboard-Start") : "Start fehlgeschlagen",
                WindowsUsername = User.Identity?.Name ?? string.Empty
            };
            _context.AppLaunchHistories.Add(history);
            await _context.SaveChangesAsync();

            TempData[success ? "Success" : "Error"] = success
                ? $"'{app.Name}' wurde erfolgreich gestartet!"
                : $"'{app.Name}' konnte nicht gestartet werden.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostStopAsync(Guid appId, string customReason = "")
        {
            var app = await _context.Applications.FindAsync(appId);
            if (app == null) return NotFound();

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

            var history = new AppLaunchHistory
            {
                ApplicationId = appId,
                UserId = currentUserId,
                LaunchTime = DateTime.Now,
                Action = "Stop",
                Reason = success ? (!string.IsNullOrWhiteSpace(customReason) ? customReason : "Dashboard-Stop") : "Stop fehlgeschlagen",
                WindowsUsername = User.Identity?.Name ?? string.Empty
            };
            _context.AppLaunchHistories.Add(history);
            await _context.SaveChangesAsync();

            TempData[success ? "Success" : "Error"] = success
                ? $"'{app.Name}' wurde erfolgreich gestoppt!"
                : $"'{app.Name}' konnte nicht gestoppt werden.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRestartAsync(Guid appId, string customReason = "")
        {
            var app = await _context.Applications.FindAsync(appId);
            if (app == null) return NotFound();

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

            bool success = await _programManager.RestartProgramAsync(app);

            var history = new AppLaunchHistory
            {
                ApplicationId = appId,
                UserId = currentUserId,
                LaunchTime = DateTime.Now,
                Action = "Restart",
                Reason = success ? (!string.IsNullOrWhiteSpace(customReason) ? customReason : "Dashboard-Restart") : "Restart fehlgeschlagen",
                WindowsUsername = User.Identity?.Name ?? string.Empty
            };
            _context.AppLaunchHistories.Add(history);
            await _context.SaveChangesAsync();

            TempData[success ? "Success" : "Error"] = success
                ? $"'{app.Name}' wurde erfolgreich neugestartet!"
                : $"'{app.Name}' konnte nicht neugestartet werden.";
            return RedirectToPage();
        }
    }
}
