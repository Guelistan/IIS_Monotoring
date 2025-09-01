using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using AppManager.Data;
using AppManager.Models;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Web.Administration;

namespace AppManager
{
    public static class ProductionSeeder
    {
        public static async Task SeedEssentialDataAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Nur essenzielle Standard-Anwendungen für alle Umgebungen
            await SeedStandardApplicationsAsync(context);

            Console.WriteLine("✅ Produktions-Basisdaten wurden überprüft/erstellt");
        }

        private static async Task SeedStandardApplicationsAsync(AppDbContext context)
        {
            // Standard Windows-Tools die immer verfügbar sein sollten
            var standardApps = new[]
            {
                new AppManager.Models.Application
                {
                    Id = Guid.NewGuid(),
                    Name = "Windows Explorer",
                    ExecutablePath = "explorer.exe",
                    Description = "Windows Datei-Explorer",
                    Category = "System",
                    RequiresAdmin = false,
                    IsIISApplication = false
                },
                new AppManager.Models.Application
                {
                    Id = Guid.NewGuid(),
                    Name = "Notepad",
                    ExecutablePath = "notepad.exe",
                    Description = "Windows Text-Editor",
                    Category = "Productivity",
                    RequiresAdmin = false,
                    IsIISApplication = false
                },
                new AppManager.Models.Application
                {
                    Id = Guid.NewGuid(),
                    Name = "Command Prompt",
                    ExecutablePath = "cmd.exe",
                    Description = "Windows Eingabeaufforderung",
                    Category = "System",
                    RequiresAdmin = true, // CMD kann Admin-Rechte benötigen
                    IsIISApplication = false
                }
            };

            foreach (var app in standardApps)
            {
                // Nur hinzufügen wenn nicht bereits vorhanden
                var exists = await context.Applications
                    .AnyAsync(a => a.Name == app.Name || a.ExecutablePath == app.ExecutablePath);

                if (!exists)
                {
                    context.Applications.Add(app);
                    Console.WriteLine($"   + Standard-App hinzugefügt: {app.Name}");
                }
            }

            await context.SaveChangesAsync();
        }

        // Für spätere Erweiterung: IIS App Pools importieren
        private static async Task SeedIISApplicationsAsync(AppDbContext context)
        {
            // Todo: Echte IIS App Pools aus dem System lesen
             ServerManager manager = new ServerManager();
            foreach (ApplicationPool pool in manager.ApplicationPools)
            {
                // Hier könnten Sie die App-Pools in die Datenbank einfügen
            }

            await Task.CompletedTask; // Placeholder
        }

        // Für spätere Erweiterung: Active Directory User importieren
        private static async Task SeedActiveDirectoryUsersAsync(AppDbContext context)
        {
            // Todo: AD-Integration für automatischen User-Import
            await Task.CompletedTask; // Placeholder
        }
    }
}
