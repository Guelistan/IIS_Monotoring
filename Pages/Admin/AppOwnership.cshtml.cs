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
using AppManager.Services;
using DataAppUser = AppManager.Data.AppUser;

namespace AppManager.Pages.Admin
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class AppOwnershipModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly IISService _iisService;
        private readonly ProgramManagerService _programManager;

        public AppOwnershipModel(AppDbContext context, IISService iisService, ProgramManagerService programManager)
        {
            _context = context;
            _iisService = iisService;
            _programManager = programManager;
        }

        public List<AppOwnership> AppOwnerships { get; set; } = new();
        public List<DataAppUser> AvailableUsers { get; set; } = new();
        public List<Application> AvailableApplications { get; set; } = new();
        public List<IISAppInfo> AvailableIISApps { get; set; } = new();
        public List<string> AvailableAppPools { get; set; } = new();

        public Dictionary<Guid, double?> CpuLoads { get; set; } = new();

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

            AppOwnerships = await _context.AppOwnerships
                .Include(o => o.User)
                .Include(o => o.Application)
                .OrderBy(o => o.CreatedAt)
                .ToListAsync();

            AvailableUsers = await _context.Users
                .Where(u => u.IsActive)
                .OrderBy(u => u.Vorname)
                .ToListAsync();

            // Lade existierende Apps aus DB
            AvailableApplications = await _context.Applications
                .OrderBy(a => a.Name)
                .ToListAsync();

            // IIS: Apps und Pools holen
            try { AvailableIISApps = await _iisService.GetAllApplicationsAsync(); }
            catch { AvailableIISApps = new List<IISAppInfo>(); }
            try { AvailableAppPools = await _iisService.GetApplicationPoolsAsync(); }
            catch { AvailableAppPools = new List<string>(); }

            // Sync: Fehlt eine IIS-App/Pool in DB -> als Application anlegen
            bool created = false;
            var knownPools = new HashSet<string>(
                AvailableApplications.Where(a => a.IsIISApplication && !string.IsNullOrWhiteSpace(a.IISAppPoolName))
                                      .Select(a => a.IISAppPoolName!),
                StringComparer.OrdinalIgnoreCase);

            // 1) IIS Applications (Site+Path) -> Application-Einträge
            foreach (var iis in AvailableIISApps)
            {
                if (string.IsNullOrWhiteSpace(iis.AppPoolName)) continue;
                if (knownPools.Contains(iis.AppPoolName)) continue;

                var app = new Application
                {
                    Id = Guid.NewGuid(),
                    Name = $"{iis.SiteName}{iis.AppPath}",
                    Description = $"IIS App ({iis.SiteName}{iis.AppPath})",
                    IsIISApplication = true,
                    IISAppPoolName = iis.AppPoolName,
                    IISSiteName = iis.SiteName,
                    ExecutablePath = string.Empty,
                    LastLaunchTime = DateTime.Now
                };
                _context.Applications.Add(app);
                knownPools.Add(iis.AppPoolName);
                created = true;
            }

            // 2) Reine AppPools ohne zugeordnete IIS-App -> Platzhalter-App
            foreach (var pool in AvailableAppPools)
            {
                if (string.IsNullOrWhiteSpace(pool)) continue;
                if (knownPools.Contains(pool)) continue;

                var app = new Application
                {
                    Id = Guid.NewGuid(),
                    Name = $"AppPool: {pool}",
                    Description = "Automatisch aus IIS AppPool erzeugt",
                    IsIISApplication = true,
                    IISAppPoolName = pool,
                    ExecutablePath = string.Empty,
                    LastLaunchTime = DateTime.Now
                };
                _context.Applications.Add(app);
                knownPools.Add(pool);
                created = true;
            }

            if (created)
            {
                await _context.SaveChangesAsync();
                // Nach dem Sync neu laden, damit Dropdown alle enthält
                AvailableApplications = await _context.Applications
                    .OrderBy(a => a.Name)
                    .ToListAsync();
            }

            // CPU je App ermitteln
            CpuLoads = new Dictionary<Guid, double?>();
            foreach (var app in AvailableApplications)
            {
                double? cpu = null;
                try
                {
                    if (app.IsIISApplication && !string.IsNullOrWhiteSpace(app.IISAppPoolName))
                    {
                        var pids = _iisService.GetWorkerProcessIds(app.IISAppPoolName);
                        cpu = _programManager.GetCpuUsageForAppPool(pids);
                    }
                    else if (app.ProcessId.HasValue)
                    {
                        cpu = _programManager.GetCpuUsageForProcess(app.ProcessId.Value);
                    }
                }
                catch { cpu = null; }
                CpuLoads[app.Id] = cpu;
            }
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
                await OnGetAsync();
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
