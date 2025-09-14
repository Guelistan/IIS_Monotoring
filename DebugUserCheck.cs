using AppManager.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using DataAppUser = AppManager.Data.AppUser;

namespace AppManager
{
    public static class DebugUserCheck
    {
        public static async Task CheckUsersInDatabase(AppDbContext context, UserManager<DataAppUser> userManager)
        {
            try
            {
                Console.WriteLine("🔍 === DEBUG: Benutzer-Datenbank-Überprüfung ===");

                var users = await context.Users.ToListAsync();
                Console.WriteLine($"📊 Anzahl Benutzer in Datenbank: {users.Count}");

                if (users.Any())
                {
                    Console.WriteLine("\n👥 Benutzer-Liste:");
                    foreach (var user in users)
                    {
                        var roles = await userManager.GetRolesAsync(user);
                        var roleList = roles.Any() ? string.Join(", ", roles) : "Keine Rollen";

                        Console.WriteLine($"  • {user.UserName} ({user.Email})");
                        Console.WriteLine($"    - Name: {user.Vorname} {user.Nachname}");
                        Console.WriteLine($"    - Aktiv: {(user.IsActive ? "✅" : "❌")}");
                        Console.WriteLine($"    - Rollen: {roleList}");
                        Console.WriteLine($"    - Erstellt: {user.CreatedAt:dd.MM.yyyy HH:mm}");
                        Console.WriteLine();
                    }
                }
                else
                {
                    Console.WriteLine("⚠️  Keine Benutzer in der Datenbank gefunden!");
                }

                Console.WriteLine("🔍 === Debug-Überprüfung abgeschlossen ===\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler bei Benutzer-Debug-Check: {ex.Message}");
            }
        }
    }
}