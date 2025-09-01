using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AppManager.Data;
using AppManager.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AppManager
{
    public static class TestDataSeeder
    {
        public static async Task SeedTestDataAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Test App-Owner Daten einfügen
            if (!context.AppOwnerships.Any())
            {
                // Beispiel-User aus der Datenbank holen
                var adminUser = await context.Users.FirstOrDefaultAsync(u => u.UserName == "admin");

                if (adminUser != null)
                {
                    // Beispiel-Apps holen
                    var paintApp = await context.Applications.FirstOrDefaultAsync(a => a.Name == "Paint");
                    var calcApp = await context.Applications.FirstOrDefaultAsync(a => a.Name == "Rechner");

                    if (paintApp != null && calcApp != null)
                    {
                        var ownerships = new[]
                        {
                            new AppOwnership
                            {
                                UserId = adminUser.Id,
                                ApplicationId = paintApp.Id,
                                WindowsUsername = Environment.UserName, // Aktueller Windows-User
                                IISAppPoolName = "PaintAppPool",
                                CreatedAt = DateTime.Now,
                                CreatedBy = "System"
                            },
                            new AppOwnership
                            {
                                UserId = adminUser.Id,
                                ApplicationId = calcApp.Id,
                                WindowsUsername = Environment.UserName, // Aktueller Windows-User
                                IISAppPoolName = "CalculatorAppPool",
                                CreatedAt = DateTime.Now,
                                CreatedBy = "System"
                            }
                        };

                        context.AppOwnerships.AddRange(ownerships);
                        await context.SaveChangesAsync();

                        Console.WriteLine($"✅ Test App-Owner Daten für User '{adminUser.UserName}' erstellt:");
                        Console.WriteLine($"   - Paint App → PaintAppPool");
                        Console.WriteLine($"   - Calculator App → CalculatorAppPool");
                        Console.WriteLine($"   - Windows User: {Environment.UserName}");
                    }
                }
            }

            // Test Launch History einfügen
            var sampleApp = await context.Applications.FirstOrDefaultAsync();
            var sampleUser = await context.Users.FirstOrDefaultAsync();

            if (sampleApp != null && sampleUser != null && !context.AppLaunchHistories.Any())
            {
                var histories = new[]
                {
                    new AppLaunchHistory
                    {
                        ApplicationId = sampleApp.Id,
                        UserId = sampleUser.Id,
                        WindowsUsername = Environment.UserName,
                        IISAppPoolName = "TestAppPool",
                        Action = "Start",
                        Reason = "Test der neuen SQL Server Konfiguration",
                        LaunchTime = DateTime.Now.AddMinutes(-30)
                    },
                    new AppLaunchHistory
                    {
                        ApplicationId = sampleApp.Id,
                        UserId = sampleUser.Id,
                        WindowsUsername = Environment.UserName,
                        IISAppPoolName = "TestAppPool",
                        Action = "Restart",
                        Reason = "Konfiguration aktualisiert",
                        LaunchTime = DateTime.Now.AddMinutes(-15)
                    }
                };

                context.AppLaunchHistories.AddRange(histories);
                await context.SaveChangesAsync();

                Console.WriteLine($"✅ Test Launch History erstellt für App '{sampleApp.Name}'");
            }
        }
    }
}
