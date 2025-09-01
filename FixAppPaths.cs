/* using Microsoft.EntityFrameworkCore;
using AppManager.Data;
using AppManager.Models;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AppManager
{
    public static class FixAppPaths
    {
        public static async Task FixInvalidPathsAsync(AppDbContext context)
        {
            Console.WriteLine("🔧 Korrigiere fehlerhafte App-Pfade...");
            
            var apps = await context.Applications.ToListAsync();
            
            foreach (var app in apps)
            {
                Console.WriteLine($"📱 Prüfe App: {app.Name} - ExecutablePath: '{app.ExecutablePath}'");
                
                // Korrigiere bekannte fehlerhafte Pfade
                string correctedPath = app.ExecutablePath switch
                {
                    "/Desktop/Browser" => @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    "/Desktop/Notepad" => @"C:\Windows\System32\notepad.exe", 
                    "/Desktop/Calculator" => @"C:\Windows\System32\calc.exe",
                    "/Desktop/WetterApp" => @"C:\Windows\System32\mspaint.exe",
                    _ when app.Name.Contains("Browser") => @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                    _ when app.Name.Contains("Notepad") => @"C:\Windows\System32\notepad.exe",
                    _ when app.Name.Contains("Calculator") => @"C:\Windows\System32\calc.exe",
                    _ when app.Name.Contains("Paint") || app.Name.Contains("Wetter") => @"C:\Windows\System32\mspaint.exe",
                    _ when app.Name.Contains("Manager") => @"C:\Windows\System32\taskmgr.exe",
                    _ => app.ExecutablePath
                };
                
                if (correctedPath != app.ExecutablePath)
                {
                    Console.WriteLine($"✅ Korrigiere: '{app.ExecutablePath}' → '{correctedPath}'");
                    app.ExecutablePath = correctedPath;
                    
                    // Falls Working Directory leer ist, setze Systemverzeichnis
                    if (string.IsNullOrEmpty(app.WorkingDirectory))
                    {
                        app.WorkingDirectory = @"C:\Windows\System32";
                    }
                }
                else
                {
                    Console.WriteLine($"ℹ️ Pfad ist bereits korrekt oder unbekannt.");
                }
            }
            
            try
            {
                var changes = await context.SaveChangesAsync();
                Console.WriteLine($"💾 {changes} Änderungen gespeichert!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Fehler beim Speichern: {ex.Message}");
            }
        }
    }
}
 */