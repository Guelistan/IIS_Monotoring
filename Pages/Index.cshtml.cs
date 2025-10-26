using AppManager.Data;
using AppManager.Models;
using AppManager.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace AppManager.Pages
{
    [Authorize(Policy = "AuthenticatedUser")]
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly CpuMonitoringService _cpuService;

        public IndexModel(AppDbContext context, UserManager<AppUser> userManager, CpuMonitoringService cpuService)
        {
            _context = context;
            _userManager = userManager;
            _cpuService = cpuService;
        }

        public List<Application> Applications { get; set; } = new();
        public double SystemCpuUsage { get; set; }
        public string CurrentUsername { get; set; } = "";
        public bool IsAdmin { get; set; }
        public bool IsAppOwner { get; set; }
        public List<string> UserRoles { get; set; } = new();
        public Dictionary<int, PerformanceData> AppPerformance { get; set; } = new();

        public async Task OnGetAsync()
        {
            // Benutzer-Informationen
            CurrentUsername = User.Identity?.Name ?? "Unbekannt";
            IsAdmin = User.IsInRole("Admin") || User.IsInRole("SuperAdmin");
            IsAppOwner = User.IsInRole("AppOwner");
            UserRoles = User.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();

            // Alle Apps laden (nur sichtbare für normale Benutzer)
            if (IsAdmin)
            {
                // Admins sehen alle Apps
                Applications = await _context.Applications
                    .OrderBy(a => a.Name)
                    .ToListAsync();
            }
            else if (IsAppOwner)
            {
                // AppOwner sehen ihre zugewiesenen Apps + alle anderen (nur lesend)
                var userId = User.FindFirst("AppUserId")?.Value;
                var ownedAppIds = await _context.AppOwnerships
                    .Where(ao => ao.UserId == userId)
                    .Select(ao => ao.ApplicationId)
                    .ToListAsync();

                Applications = await _context.Applications
                    .OrderBy(a => a.Name)
                    .ToListAsync();

                // Markierung welche Apps der Benutzer besitzt
                foreach (var app in Applications)
                {
                    app.Tags = ownedAppIds.Contains(app.Id) ? "Owned" : "ReadOnly";
                }
            }
            else
            {
                // Normale Benutzer sehen alle Apps (nur lesend)
                Applications = await _context.Applications
                    .OrderBy(a => a.Name)
                    .ToListAsync();
            }

            // System-Performance laden
            try
            {
                SystemCpuUsage = await _cpuService.GetSystemCpuUsageAsync();
                
                // Performance-Daten für laufende Apps
                foreach (var app in Applications.Where(a => a.IsStarted && a.ProcessId.HasValue))
                {
                    var perfData = new PerformanceData
                    {
                        ProcessId = app.ProcessId.Value,
                        ProcessName = app.Name,
                        CpuUsage = _cpuService.GetProcessCpuUsage(app.ProcessId.Value),
                        MemoryUsageMB = _cpuService.GetProcessMemoryUsage(app.ProcessId.Value)
                    };
                    AppPerformance[app.ProcessId.Value] = perfData;
                }
            }
            catch (System.Exception ex)
            {
                // Performance-Monitoring-Fehler nicht fatal
                System.Console.WriteLine($"⚠️ Performance-Monitoring Fehler: {ex.Message}");
            }
        }
    }
}
