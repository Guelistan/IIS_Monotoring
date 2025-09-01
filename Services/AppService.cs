using System;
using System.Diagnostics;
using AppManager.Models;

namespace AppManager.Services
{
    public class AppService
    {
        public void StartApp(Application app)
        {
            Process.Start(app.Path);
        }

        public void StopApp(Application app)
        {
            // Alle Prozesse mit dem Namen der App beenden
            // Beispiel: Wenn app.Path = "C:\\Programme\\BeispielApp.exe"
            // Dann holen wir den Prozessnamen ohne ".exe"
            string processName = System.IO.Path.GetFileNameWithoutExtension(app.Path);

            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill();
                    process.WaitForExit();
                }
                catch
                {
                    Console.WriteLine($"Fehler beim Beenden des Prozesses '{processName}'");
                        // Fehlerbehandlung, falls Prozess nicht beendet werden kann
                }
            }
        }

        public bool StopApp(Application app, out string errorMessage)
        {
            errorMessage = null;
            string processName = System.IO.Path.GetFileNameWithoutExtension(app.Path);
            var processes = Process.GetProcessesByName(processName);

            if (processes.Length == 0)
            {
                errorMessage = $"Kein laufender Prozess mit dem Namen '{processName}' gefunden.";
                return false;
            }

            foreach (var process in processes)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit();
                }
                catch (Exception ex)
                {
                    errorMessage = $"Prozess '{processName}' konnte nicht beendet werden: {ex.Message}";
                    return false;
                }
            }
            return true;
        }
    }
}
