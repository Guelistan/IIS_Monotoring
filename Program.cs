using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Threading.Tasks;
using AppManager.Data;
using AppManager.Models;
using Microsoft.AspNetCore.Identity.UI.Services;
using AppManager.Services;
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication.Negotiate;



var builder = WebApplication.CreateBuilder(args);


// 📧 Fake E-Mail-Sender für Entwicklung
builder.Services.AddTransient<IEmailSender, ConsoleEmailSender>();

// 📦 Datenbank: SQLite (Datei-basiert)
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var cs = builder.Configuration.GetConnectionString("DefaultConnection");
    try
    {
        var csb = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(cs);
        var dataSource = csb.DataSource;
        if (!string.IsNullOrWhiteSpace(dataSource) && !System.IO.Path.IsPathRooted(dataSource))
        {
            var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(builder.Environment.ContentRootPath, dataSource.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            var dir = System.IO.Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            csb.DataSource = fullPath;
            cs = csb.ToString();
        }
    }
    catch { /* fallback: let EF try as-is */ }
    options.UseSqlite(cs);
});

// 🔐 Identity-Konfiguration
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedEmail = false;  // Username-Login ohne E-Mail-Bestätigung
        options.User.RequireUniqueEmail = false;       // Username als primärer Login

        // 🔓 Gelockerte Passwort-Richtlinien für einfache Registrierung
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 3;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// 🍪 Authentifizierung via Cookie
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
});

// 📄 Razor Pages aktivieren (configured later with options)

// 📋 HTTP-Logging aktivieren
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = HttpLoggingFields.All;
});

// Fehlende Service-Registrierung hinzufügen:
builder.Services.AddScoped<ProgramManagerService>();
builder.Services.AddScoped<IISService>();
builder.Services.AddScoped<AppService>();

// 🌐 URLs für Non-Development-Umgebung festlegen
if (!builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls("http://localhost:5130", "https://localhost:5007");
}

// ⚙️ HTTPS-Redirect konfigurierbar machen (Standard: in Production an, in Development aus)
var enforceHttps = builder.Configuration.GetValue<bool?>("EnforceHttpsRedirect") ?? (!builder.Environment.IsDevelopment());

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
       .AddNegotiate();

builder.Services.AddAuthorization(options =>
{
    // Nur Admin-Bereich schützen oder FallbackPolicy setzen
    // options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

// RazorPages: /Admin schützen (configure RazorPages with conventions)
builder.Services.AddRazorPages().AddRazorPagesOptions(opts =>
{
    opts.Conventions.AuthorizeFolder("/Admin");
});

var app = builder.Build();

// --- automatisch Migrationen anwenden (nur in Dev/Test, optional) ---
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.Migrate();
        logger.LogInformation("Database migrated/applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Fehler beim Anwenden der DB-Migrationen.");
        throw;
    }
}

// ⚠️ Fehlerbehandlung
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    if (enforceHttps)
    {
        app.UseHsts();
    }
}
// 🔒 HTTPS nur wenn aktiviert
if (enforceHttps)
{
    app.UseHttpsRedirection();
}

// 🧪 Initiales Datenbank-Seeding (Rollen, Admin, Anwendungen)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<AppDbContext>();
    var userManager = services.GetRequiredService<UserManager<AppUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

    context.Database.Migrate();

    // 🔧 Korrigiere fehlerhafte App-Pfade direkt in der Datenbank
    var existingApps = context.Applications.ToList();
    foreach (var appToFix in existingApps)
    {
        if (appToFix.ExecutablePath.StartsWith("/Desktop/") || !appToFix.ExecutablePath.Contains(@"\"))
        {
            string correctedPath = appToFix.ExecutablePath switch
            {
                "/Desktop/Browser" => @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                "/Desktop/Notepad" => @"C:\Windows\System32\notepad.exe",
                "/Desktop/Calculator" => @"C:\Windows\System32\calc.exe",
                "/Desktop/WetterApp" => @"C:\Windows\System32\mspaint.exe",
                _ when appToFix.Name.Contains("Browser") => @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                _ when appToFix.Name.Contains("Notepad") => @"C:\Windows\System32\notepad.exe",
                _ when appToFix.Name.Contains("Calculator") => @"C:\Windows\System32\calc.exe",
                _ when appToFix.Name.Contains("Paint") || appToFix.Name.Contains("Wetter") => @"C:\Windows\System32\mspaint.exe",
                _ when appToFix.Name.Contains("Manager") => @"C:\Windows\System32\taskmgr.exe",
                _ => appToFix.ExecutablePath
            };

            if (correctedPath != appToFix.ExecutablePath)
            {
                Console.WriteLine($"✅ Korrigiere: '{appToFix.ExecutablePath}' → '{correctedPath}'");
                appToFix.ExecutablePath = correctedPath;
                if (string.IsNullOrEmpty(appToFix.WorkingDirectory))
                {
                    appToFix.WorkingDirectory = @"C:\Windows\System32";
                }
            }
        }
    }
    context.SaveChanges();

    // Anwendungen seeden
    if (!context.Applications.Any())
    {
        var apps = new List<Application>
        {
            new() { Name = "Rechner", Description = "Windows Rechner", ExecutablePath = @"C:\Windows\System32\calc.exe", WorkingDirectory = @"C:\Windows\System32" },
            new() { Name = "Notepad", Description = "Windows Editor", ExecutablePath = @"C:\Windows\System32\notepad.exe", WorkingDirectory = @"C:\Windows\System32" },
            new() { Name = "Paint", Description = "Windows Paint", ExecutablePath = @"C:\Windows\System32\mspaint.exe", WorkingDirectory = @"C:\Windows\System32" },
            new() { Name = "Task Manager", Description = "Windows Task Manager", ExecutablePath = @"C:\Windows\System32\taskmgr.exe", WorkingDirectory = @"C:\Windows\System32", RequiresAdmin = true },
            new() { Name = "Command Prompt", Description = "Eingabeaufforderung", ExecutablePath = @"C:\Windows\System32\cmd.exe", WorkingDirectory = @"C:\Windows\System32" }
        };

        context.Applications.AddRange(apps);
        context.SaveChanges();
    }



    var allApps = context.Applications.ToList();
    Console.WriteLine("Apps in DB:");
    foreach (var a in allApps)
    {
        Console.WriteLine($"- {a.Name}");
    }

    // Rollen anlegen
    string[] roles = { "Admin", "SuperAdmin" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // Hauptadmin anlegen
    var username = "admin";
    var email = "admin@appmanager.local";
    var password = "Admin123!";

    var admin = await userManager.FindByNameAsync(username);
    if (admin == null)
    {
        admin = new AppUser
        {
            UserName = username,
            Email = email,
            EmailConfirmed = true,
            Vorname = "Administrator",
            Nachname = "System",
            IsActive = true,
            IsGlobalAdmin = true
        };
        await userManager.CreateAsync(admin, password);
    }

    if (!await userManager.IsInRoleAsync(admin, "SuperAdmin"))
    {
        await userManager.AddToRoleAsync(admin, "SuperAdmin");
    }

    // 🧪 Test-Daten nur für Development
    if (app.Environment.IsDevelopment())
    {
        await AppManager.TestDataSeeder.SeedTestDataAsync(services);
    }

    // 🚀 Produktions-Basisdaten für alle Umgebungen
    await AppManager.ProductionSeeder.SeedEssentialDataAsync(services);

    // 🔍 Debug: Benutzer-Datenbank überprüfen
    Console.WriteLine();
    using (var debugScope = services.CreateScope())
    {
        var debugContext = debugScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var debugUserManager = debugScope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        await AppManager.DebugUserCheck.CheckUsersInDatabase(debugContext, debugUserManager);
    }
}

app.UseStaticFiles();
app.UseRouting();
app.UseHttpLogging();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.MapControllers();

// 🌐 Browser automatisch öffnen (nur bei Release-Builds)
if (!app.Environment.IsDevelopment())
{
    Console.WriteLine($"🌐 Server wird gestartet...");
    Console.WriteLine($"🌐 Browser wird in 3 Sekunden geöffnet...");

    // Browser mit Verzögerung öffnen - nach dem Server-Start
    _ = Task.Run(async () =>
    {
        await Task.Delay(3000); // 3 Sekunden warten bis Server sicher bereit ist

        try
        {
            // Tatsächliche Server-URLs ermitteln
            var serverUrls = app.Urls.ToList();

            string url;
            if (serverUrls.Any())
            {
                // Bevorzuge HTTPS, falls verfügbar
                url = serverUrls.FirstOrDefault(u => u.StartsWith("https")) ?? serverUrls.First();
            }
            else
            {
                // Fallback zu Standardports
                url = "https://localhost:5007";
            }

            Console.WriteLine($"🌐 Öffne Browser: {url}");

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

            Console.WriteLine($"✅ Browser geöffnet: {url}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Browser konnte nicht geöffnet werden: {ex.Message}");
            Console.WriteLine($"📱 Bitte manuell öffnen - mögliche URLs:");
            Console.WriteLine($"   - https://localhost:5007");
            Console.WriteLine($"   - http://localhost:5130");
        }
    });
}

// 🚀 Anwendung starten
app.Run();
