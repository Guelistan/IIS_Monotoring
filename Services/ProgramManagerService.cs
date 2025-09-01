using System;
using System.Diagnostics;
using System.Threading.Tasks;
using AppManager.Models;

namespace AppManager.Services
{
    public class ProgramManagerService
    {
        public async Task<bool> StartProgramAsync(Application app)
        {
            return await Task.Run(() =>
            {
                try
                {
                    Console.WriteLine($"🚀 Versuche zu starten: {app.ExecutablePath}");
                    
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = app.ExecutablePath,
                        UseShellExecute = true,
                        CreateNoWindow = false,
                        WorkingDirectory = string.IsNullOrEmpty(app.WorkingDirectory) 
                            ? Environment.GetFolderPath(Environment.SpecialFolder.System) 
                            : app.WorkingDirectory
                    };

                    if (!string.IsNullOrEmpty(app.Arguments))
                    {
                        startInfo.Arguments = app.Arguments;
                    }

                    var process = Process.Start(startInfo);
                    
                    if (process != null)
                    {
                        app.ProcessId = process.Id;
                        app.IsStarted = true;
                        Console.WriteLine($"✅ Erfolgreich gestartet! PID: {process.Id}");
                        return true;
                    }

                    Console.WriteLine("❌ Process.Start() gab null zurück");
                    return false;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ FEHLER beim Starten von {app.Name}: {ex.Message}");
                    return false;
                }
            });
        }

        public async Task<bool> StopProgramAsync(Application app)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (app.ProcessId.HasValue)
                    {
                        var process = Process.GetProcessById(app.ProcessId.Value);
                        if (!process.HasExited)
                        {
                            process.CloseMainWindow();
                            if (!process.WaitForExit(3000))
                            {
                                process.Kill();
                            }
                        }
                    }

                    app.IsStarted = false;
                    app.ProcessId = null;
                    Console.WriteLine($"⏹️ {app.Name} gestoppt");
                    return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ Fehler beim Stoppen: {ex.Message}");
                    app.IsStarted = false;
                    app.ProcessId = null;
                    return false;
                }
            });
        }

        public async Task<bool> RestartProgramAsync(Application app)
        {
            Console.WriteLine($"🔄 Neustart von {app.Name}...");
            await StopProgramAsync(app);
            await Task.Delay(2000);
            return await StartProgramAsync(app);
        }
    }
}