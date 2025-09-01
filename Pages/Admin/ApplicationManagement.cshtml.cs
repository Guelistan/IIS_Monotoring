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

            public string Description { get; set; }

            [Required]
            public string ExecutablePath { get; set; }

            public string Arguments { get; set; }

            public string WorkingDirectory { get; set; }

            public bool IsIISApplication { get; set; }

            public string IISAppPoolName { get; set; }

            public string IISSiteName { get; set; }

            public bool AutoStart { get; set; }

            public bool RestartRequired { get; set; }
        }

        public async Task OnGetAsync()
        {
            Console.WriteLine("🔍 ApplicationManagement OnGetAsync wird ausgeführt...");

            Applications = await _context.Applications
                .OrderBy(a => a.Name)
                .ToListAsync();

            Console.WriteLine($"📱 {Applications.Count} Anwendungen geladen");
        }

        public async Task<IActionResult> OnPostAddAsync()
        {
            Console.WriteLine("🔍 ApplicationManagement OnPostAddAsync wird ausgeführt...");
            Console.WriteLine($"   Name: {NewApplication.Name}");
            Console.WriteLine($"   ExecutablePath: {NewApplication.ExecutablePath}");
            Console.WriteLine($"   IsIISApplication: {NewApplication.IsIISApplication}");

            if (!ModelState.IsValid)
            {
                Console.WriteLine("❌ ModelState ist ungültig");
                await OnGetAsync();
                return Page();
            }

            // Prüfe, ob Anwendung bereits existiert
            var existingApp = await _context.Applications
                .FirstOrDefaultAsync(a => a.Name == NewApplication.Name);

            if (existingApp != null)
            {
                Console.WriteLine("❌ Anwendung mit diesem Namen existiert bereits");
                ModelState.AddModelError(string.Empty, "Eine Anwendung mit diesem Namen existiert bereits.");
                await OnGetAsync();
                return Page();
            }

            // Erstelle neue Anwendung
            var application = new Application
            {
                Id = Guid.NewGuid(),
                Name = NewApplication.Name,
                Description = NewApplication.Description,
                ExecutablePath = NewApplication.ExecutablePath,
                Arguments = NewApplication.Arguments,
                WorkingDirectory = NewApplication.WorkingDirectory,
                IsIISApplication = NewApplication.IsIISApplication,
                IISAppPoolName = NewApplication.IISAppPoolName,
                IISSiteName = NewApplication.IISSiteName,
                RestartRequired = NewApplication.RestartRequired,
                IsStarted = false,
                LastLaunchTime = DateTime.Now
            };

            _context.Applications.Add(application);
            await _context.SaveChangesAsync();

            Console.WriteLine($"✅ Neue Anwendung erstellt: {NewApplication.Name}");

            // Audit-Log erstellen
            var auditLog = new AppLaunchHistory
            {
                ApplicationId = application.Id,
                UserId = _context.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id,
                WindowsUsername = User.Identity.Name,
                Action = "APPLICATION_CREATED",
                Reason = $"Anwendung '{NewApplication.Name}' erstellt von {User.Identity.Name}",
                LaunchTime = DateTime.Now
            };

            _context.AppLaunchHistories.Add(auditLog);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Anwendung erfolgreich erstellt!";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(Guid applicationId)
        {
            Console.WriteLine($"🔍 ApplicationManagement OnPostDeleteAsync wird ausgeführt... ID: {applicationId}");

            var application = await _context.Applications
                .FirstOrDefaultAsync(a => a.Id == applicationId);

            if (application == null)
            {
                Console.WriteLine("❌ Anwendung nicht gefunden");
                TempData["ErrorMessage"] = "Anwendung nicht gefunden.";
                return RedirectToPage();
            }

            Console.WriteLine($"🗑️ Lösche Anwendung: {application.Name}");

            // Prüfe, ob noch App-Ownerships existieren
            var ownerships = await _context.AppOwnerships
                .Where(o => o.ApplicationId == applicationId)
                .CountAsync();

            if (ownerships > 0)
            {
                Console.WriteLine($"❌ Anwendung hat noch {ownerships} aktive Berechtigungen");
                TempData["ErrorMessage"] = $"Anwendung kann nicht gelöscht werden. Es existieren noch {ownerships} aktive Berechtigungen.";
                return RedirectToPage();
            }

            // Stoppe Anwendung falls sie läuft
            if (application.IsStarted)
            {
                try
                {
                    if (_appService.StopApp(application, out string errorMessage))
                    {
                        application.IsStarted = false;
                        await _context.SaveChangesAsync();
                        Console.WriteLine("🛑 Anwendung gestoppt vor dem Löschen");
                    }
                    else
                    {
                        Console.WriteLine($"⚠️ Fehler beim Stoppen der Anwendung: {errorMessage}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Fehler beim Stoppen der Anwendung: {ex.Message}");
                }
            }

            // Audit-Log erstellen vor dem Löschen
            var auditLog = new AppLaunchHistory
            {
                ApplicationId = application.Id,
                UserId = _context.Users.FirstOrDefault(u => u.UserName == User.Identity.Name)?.Id,
                WindowsUsername = User.Identity.Name,
                Action = "APPLICATION_DELETED",
                Reason = $"Anwendung '{application.Name}' gelöscht von {User.Identity.Name}",
                LaunchTime = DateTime.Now
            };

            _context.AppLaunchHistories.Add(auditLog);

            // Anwendung löschen
            _context.Applications.Remove(application);
            await _context.SaveChangesAsync();

            Console.WriteLine("✅ Anwendung und Audit-Log erfolgreich verarbeitet");

            TempData["SuccessMessage"] = "Anwendung erfolgreich gelöscht!";
            return RedirectToPage();
        }

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
