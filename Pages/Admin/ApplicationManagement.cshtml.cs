using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AppManager.Data;
using AppManager.Models;
using AppManager.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;

namespace AppManager.Pages.Admin
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class ApplicationManagementModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly AppService _appService;
        private readonly ProgramManagerService _programManager;
        private readonly string _iisResetPath = @"C:\Windows\System32\inetsrv\iisreset.exe";
        private readonly IISService _iisService;

        public ApplicationManagementModel(AppDbContext context, AppService appService, ProgramManagerService programManager, IISService iisService)
        {
            _context = context;
            _appService = appService;
            _programManager = programManager;
            _iisService = iisService;
            // _iisResetPath = _programManager.GetIISResetPath(); // Removed because method does not exist
        }

        public List<Application> Applications { get; set; } = new();

        [BindProperty]
        public NewApplicationModel NewApplication { get; set; } = new();

        public class NewApplicationModel
        {
            [Required]
            public string Name { get; set; }
            // ...weitere Properties wie benötigt...
        }
        public async Task<IActionResult> OnPostRecycleAsync(Guid applicationId)
        {
            Console.WriteLine($"🔄 ApplicationManagement OnPostRecycleAsync wird ausgeführt... ID: {applicationId}");

            var application = await _context.Applications
                .FirstOrDefaultAsync(a => a.Id == applicationId);

            if (application == null)
            {
                TempData["ErrorMessage"] = "Anwendung nicht gefunden.";
                return RedirectToPage();
            }

            if (!application.IsIISApplication || string.IsNullOrWhiteSpace(application.IISAppPoolName))
            {
                TempData["ErrorMessage"] = "Die Anwendung ist keine IIS-Anwendung oder der AppPool-Name fehlt.";
                return RedirectToPage();
            }

            try
            {
                bool result = await _iisService.RecycleAppPoolAsync(application.IISAppPoolName);
                if (result)
                {
                    TempData["SuccessMessage"] = $"IIS Application Pool '{application.IISAppPoolName}' wurde erfolgreich recycelt.";
                }
                else
                {
                    TempData["ErrorMessage"] = $"Fehler beim Recyceln des IIS Application Pools '{application.IISAppPoolName}'.";
                }
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"Fehler beim Recyceln: {ex.Message}";
            }

            return RedirectToPage();
        }

        // Die Löschfunktion wurde entfernt. Hier ist kein Methodenkörper mehr vorhanden.

        public async Task<IActionResult> OnPostStartAsync(Guid applicationId, string customReason = "")
        {
            Console.WriteLine($"🔍 ApplicationManagement OnPostStartAsync wird ausgeführt... ID: {applicationId}");

            var application = await _context.Applications
                .FirstOrDefaultAsync(a => a.Id == applicationId);

            if (application == null)
            {
                TempData["ErrorMessage"] = "Anwendung nicht gefunden.";
                return RedirectToPage();
            }

            try
            {
                string reason = string.IsNullOrEmpty(customReason) ? "Manuell gestartet via Admin Interface" : customReason;

                // Versuche Anwendung zu starten
                _appService.StartApp(application);
                application.IsStarted = true;
                application.LastLaunchTime = DateTime.Now;
                application.LastLaunchReason = reason;

                await _context.SaveChangesAsync();

                // Audit-Log erstellen
                var auditLog = new AppLaunchHistory
                {
                    ApplicationId = application.Id,
                    UserId = _context.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id,
                    WindowsUsername = User.Identity.Name,
                    Action = "START",
                    Reason = reason,
                    LaunchTime = DateTime.Now
                };
                _context.AppLaunchHistories.Add(auditLog);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Anwendung '{application.Name}' erfolgreich gestartet!";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler beim Starten: {ex.Message}");
                TempData["ErrorMessage"] = $"Fehler beim Starten der Anwendung: {ex.Message}";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostStopAsync(Guid applicationId, string customReason = "")
        {
            Console.WriteLine($"🔍 ApplicationManagement OnPostStopAsync wird ausgeführt... ID: {applicationId}");

            var application = await _context.Applications
                .FirstOrDefaultAsync(a => a.Id == applicationId);

            if (application == null)
            {
                TempData["ErrorMessage"] = "Anwendung nicht gefunden.";
                return RedirectToPage();
            }

            try
            {
                string reason = string.IsNullOrEmpty(customReason) ? "Manuell gestoppt via Admin Interface" : customReason;

                if (_appService.StopApp(application, out string errorMessage))
                {
                    application.IsStarted = false;
                    await _context.SaveChangesAsync();

                    // Audit-Log erstellen
                    var auditLog = new AppLaunchHistory
                    {
                        ApplicationId = application.Id,
                        UserId = _context.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id,
                        WindowsUsername = User.Identity.Name,
                        Action = "STOP",
                        Reason = reason,
                        LaunchTime = DateTime.Now
                    };
                    _context.AppLaunchHistories.Add(auditLog);
                    await _context.SaveChangesAsync();

                    TempData["SuccessMessage"] = $"Anwendung '{application.Name}' erfolgreich gestoppt!";
                }
                else
                {
                    TempData["ErrorMessage"] = $"Fehler beim Stoppen der Anwendung: {errorMessage}";
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler beim Stoppen: {ex.Message}");
                TempData["ErrorMessage"] = $"Fehler beim Stoppen der Anwendung: {ex.Message}";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRestartAsync(Guid applicationId, string customReason = "")
        {
            Console.WriteLine($"🔍 ApplicationManagement OnPostRestartAsync wird ausgeführt... ID: {applicationId}");

            var application = await _context.Applications
                .FirstOrDefaultAsync(a => a.Id == applicationId);

            if (application == null)
            {
                TempData["ErrorMessage"] = "Anwendung nicht gefunden.";
                return RedirectToPage();
            }

            try
            {
                string reason = string.IsNullOrEmpty(customReason) ? "Manuell neugestartet via Admin Interface" : customReason;

                // Stoppe die Anwendung
                if (application.IsStarted)
                {
                    if (_appService.StopApp(application, out string stopErrorMessage))
                    {
                        application.IsStarted = false;
                        await _context.SaveChangesAsync();
                        await Task.Delay(2000); // 2 Sekunden warten
                    }
                    else
                    {
                        TempData["ErrorMessage"] = $"Fehler beim Stoppen der Anwendung: {stopErrorMessage}";
                        return RedirectToPage();
                    }
                }

                // Starte die Anwendung
                _appService.StartApp(application);
                application.IsStarted = true;
                application.LastLaunchTime = DateTime.Now;
                application.LastLaunchReason = reason;
                await _context.SaveChangesAsync();

                // Audit-Log erstellen
                var auditLog = new AppLaunchHistory
                {
                    ApplicationId = application.Id,
                    UserId = _context.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id,
                    WindowsUsername = User.Identity.Name,
                    Action = "RESTART",
                    Reason = reason,
                    LaunchTime = DateTime.Now
                };
                _context.AppLaunchHistories.Add(auditLog);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Anwendung '{application.Name}' erfolgreich neugestartet!";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler beim Neustarten: {ex.Message}");
                TempData["ErrorMessage"] = $"Fehler beim Neustarten der Anwendung: {ex.Message}";
            }

            return RedirectToPage();
        }
    }
}
