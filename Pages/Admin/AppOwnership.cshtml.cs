using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using AppManager.Data;
using AppManager.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations;
using AppModel = AppManager.Models.Application;

namespace AppManager.Pages.Admin
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AppOwnershipModel : PageModel
    {
        private readonly AppDbContext _context;

        public AppOwnershipModel(AppDbContext context)
        {
            _context = context;
        }

        public List<AppOwnership> AppOwnerships { get; set; } = new();
        public List<AppUser> AvailableUsers { get; set; } = new();
        public List<Application> AvailableApplications { get; set; } = new();

        [BindProperty]
        public NewOwnershipModel NewOwnership { get; set; } = new();

        public class NewOwnershipModel
        {
            [Required]
            public string UserId { get; set; }

            [Required]
            public Guid ApplicationId { get; set; }

            [Required]
            public string WindowsUsername { get; set; }

            public string IISAppPoolName { get; set; }
        }

        public async Task OnGetAsync()
        {
            Console.WriteLine("🔍 AppOwnership OnGetAsync wird ausgeführt...");

            // Lade alle App-Ownerships mit Benutzer- und App-Daten
            AppOwnerships = await _context.AppOwnerships
                .Include(o => o.User)
                .Include(o => o.Application)
                .OrderBy(o => o.CreatedAt)
                .ToListAsync();

            Console.WriteLine($"📊 {AppOwnerships.Count} App-Ownerships geladen");

            // Lade verfügbare Benutzer und Apps für das Formular
            AvailableUsers = await _context.Users
                .Where(u => u.IsActive)
                .OrderBy(u => u.Vorname)
                .ToListAsync();

            AvailableApplications = await _context.Applications
                .OrderBy(a => a.Name)
                .ToListAsync();

            Console.WriteLine($"👥 {AvailableUsers.Count} aktive Benutzer verfügbar");
            Console.WriteLine($"📱 {AvailableApplications.Count} Anwendungen verfügbar");
        }

        public async Task<IActionResult> OnPostAddAsync()
        {
            Console.WriteLine("🔍 AppOwnership OnPostAddAsync wird ausgeführt...");
            Console.WriteLine($"   UserId: {NewOwnership.UserId}");
            Console.WriteLine($"   ApplicationId: {NewOwnership.ApplicationId}");
            Console.WriteLine($"   WindowsUsername: {NewOwnership.WindowsUsername}");

            if (!ModelState.IsValid)
            {
                Console.WriteLine("❌ ModelState ist ungültig");
                await OnGetAsync(); // Daten neu laden
                return Page();
            }

            // Prüfe, ob die Berechtigung bereits existiert
            var existingOwnership = await _context.AppOwnerships
                .FirstOrDefaultAsync(o => o.UserId == NewOwnership.UserId &&
                                         o.ApplicationId == NewOwnership.ApplicationId);

            if (existingOwnership != null)
            {
                Console.WriteLine("❌ Berechtigung existiert bereits");
                ModelState.AddModelError(string.Empty, "Diese Berechtigung existiert bereits.");
                await OnGetAsync();
                return Page();
            }

            // Erstelle neue Berechtigung
            var ownership = new AppOwnership
            {
                UserId = NewOwnership.UserId,
                ApplicationId = NewOwnership.ApplicationId,
                WindowsUsername = NewOwnership.WindowsUsername,
                IISAppPoolName = NewOwnership.IISAppPoolName,
                CreatedAt = DateTime.Now,
                CreatedBy = User.Identity.Name ?? "System"
            };

            _context.AppOwnerships.Add(ownership);
            await _context.SaveChangesAsync();

            Console.WriteLine($"✅ Neue App-Owner Berechtigung erstellt: {NewOwnership.WindowsUsername}");

            // Audit-Log erstellen
            var auditLog = new AppLaunchHistory
            {
                ApplicationId = NewOwnership.ApplicationId,
                UserId = NewOwnership.UserId,
                WindowsUsername = NewOwnership.WindowsUsername,
                IISAppPoolName = NewOwnership.IISAppPoolName,
                Action = "OWNERSHIP_CREATED",
                Reason = $"App-Owner Berechtigung erstellt von {User.Identity.Name}",
                LaunchTime = DateTime.Now
            };

            _context.AppLaunchHistories.Add(auditLog);
            await _context.SaveChangesAsync();

            Console.WriteLine("📝 Audit-Log für Berechtigung erstellt");

            TempData["SuccessMessage"] = "App-Owner Berechtigung erfolgreich erstellt!";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int ownershipId)
        {
            Console.WriteLine($"🔍 AppOwnership OnPostDeleteAsync wird ausgeführt... ID: {ownershipId}");

            var ownership = await _context.AppOwnerships
                .Include(o => o.User)
                .Include(o => o.Application)
                .FirstOrDefaultAsync(o => o.Id == ownershipId);

            if (ownership == null)
            {
                Console.WriteLine("❌ Berechtigung nicht gefunden");
                TempData["ErrorMessage"] = "Berechtigung nicht gefunden.";
                return RedirectToPage();
            }

            Console.WriteLine($"🗑️ Lösche Berechtigung: {ownership.User.UserName} -> {ownership.Application.Name}");

            // Audit-Log erstellen vor dem Löschen
            var auditLog = new AppLaunchHistory
            {
                ApplicationId = ownership.ApplicationId,
                UserId = ownership.UserId,
                WindowsUsername = ownership.WindowsUsername,
                IISAppPoolName = ownership.IISAppPoolName,
                Action = "OWNERSHIP_DELETED",
                Reason = $"App-Owner Berechtigung entfernt von {User.Identity.Name}",
                LaunchTime = DateTime.Now
            };

            _context.AppLaunchHistories.Add(auditLog);

            // Berechtigung löschen
            _context.AppOwnerships.Remove(ownership);
            await _context.SaveChangesAsync();

            Console.WriteLine("✅ Berechtigung und Audit-Log erfolgreich verarbeitet");

            TempData["SuccessMessage"] = "App-Owner Berechtigung erfolgreich entfernt!";
            return RedirectToPage();
        }
    }
}
