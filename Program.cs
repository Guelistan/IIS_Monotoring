using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
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
using Microsoft.AspNetCore.Server.IISIntegration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using System.Security.Principal;
using System.DirectoryServices.AccountManagement;
using AppUser = AppManager.Data.AppUser;

var builder = WebApplication.CreateBuilder(args);
// builder.WebHost.ConfigureKestrel(options => {
//     throw new InvalidOperationException("Kestrel ist deaktiviert. Bitte nur über IIS/IIS Express starten.");
// });

builder.WebHost.UseIIS();

//  Datenbank: SQLite (Datei-basiert)
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

// 🔐 Identity-Konfiguration für Windows-Auth
builder.Services.AddIdentityCore<AppManager.Data.AppUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// Windows-Authentifizierung konfigurieren
builder.Services.AddAuthentication(options => {
    options.DefaultScheme = "Windows";
    options.DefaultAuthenticateScheme = "Windows";
    options.DefaultChallengeScheme = "Windows";
})
.AddNegotiate("Windows", options => {
    options.Events = new NegotiateEvents {
        OnAuthenticated = context => {
            if (context.Principal?.Identity is WindowsIdentity winIdentity)
            {
                var claims = new List<System.Security.Claims.Claim>
                {
                    new System.Security.Claims.Claim("windows_username", winIdentity.Name),
                    new System.Security.Claims.Claim("windows_sid", winIdentity.User?.Value ?? "unknown")
                };
                var identity = new System.Security.Claims.ClaimsIdentity(claims, "Windows");
                context.Principal.AddIdentity(identity);
            }
            return Task.CompletedTask;
        }
    };
});

// Claims Transformation für zusätzliche Identity-Integration
// builder.Services.AddScoped<IClaimsTransformation, WindowsUserClaimsTransformation>(); // ❌ Temporär auskommentiert

// 📧 Fake E-Mail-Sender für Entwicklung
// builder.Services.AddTransient<IEmailSender, ConsoleEmailSender>(); // ❌ Temporär auskommentiert

// HTTP Context Accessor für Service-basierte User-Erkennung
builder.Services.AddHttpContextAccessor();

// DI
builder.Services.AddScoped<ProgramManagerService>();
builder.Services.AddScoped<IISService>();
builder.Services.AddScoped<AppService>();

// 📋 HTTP-Logging
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = HttpLoggingFields.All;
});

// ⚙️ HTTPS-Redirect (konfigurierbar)
// Default: disabled to avoid unerwartete Umleitungen auf WTS/Terminalserver.
// Setze in appsettings.Production.json oder Umgebung: "EnforceHttpsRedirect": true
var enforceHttps = builder.Configuration.GetValue<bool?>("EnforceHttpsRedirect") ?? false;

builder.Services.AddAuthorization(options =>
{
    // Grundlegende Windows-Auth Policy
    var defaultPolicy = new AuthorizationPolicyBuilder()
        .AddAuthenticationSchemes("Windows")
        .RequireAuthenticatedUser()
        .Build();
    
    options.DefaultPolicy = defaultPolicy;
    options.FallbackPolicy = defaultPolicy;

    // Admin-Policy
    options.AddPolicy("Admin", policy =>
        policy.RequireRole("Admin", "SuperAdmin"));
});

// HSTS service registration (options configured but only applied when enforceHttps==true)
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = false;
    options.Preload = false;
});

// Razor Pages: /Admin schützen
builder.Services.AddRazorPages().AddRazorPagesOptions(opts =>
{
    opts.Conventions.AuthorizeFolder("/Admin");
});

// Windows Authentication - keine Cookie-Konfiguration notwendig

var app = builder.Build();

// --- Migrationen anwenden ---
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
    // HSTS: Only enable if explicit configuration requests HTTPS enforcement.
    if (enforceHttps)
    {
        app.UseHsts();
    }
}

// HTTPS-Redirect: only when configured/enabled. Default remains false for WTS.

if (enforceHttps)
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();


// 🧪 Initiales Datenbank-Seeding (unverändert)
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<AppDbContext>();
    var userManager = services.GetRequiredService<UserManager<AppUser>>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

    context.Database.Migrate();

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

    string[] roles = { "Admin", "SuperAdmin" };
    foreach (var role in roles)
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

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

    if (app.Environment.IsDevelopment())
    {
        await AppManager.TestDataSeeder.SeedTestDataAsync(services);
    }

    await AppManager.ProductionSeeder.SeedEssentialDataAsync(services);

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
app.UseAuthentication(); // Aktiviert Auth-Middleware
app.UseAuthorization();
app.MapRazorPages();
app.MapControllers();

if (!app.Environment.IsDevelopment())
{
    Console.WriteLine($"🌐 Server wird gestartet...");
    Console.WriteLine($"🌐 Browser wird in 3 Sekunden geöffnet...");
    _ = Task.Run(async () =>
    {
        await Task.Delay(3000);
        try
        {
            var serverUrls = app.Urls.ToList();
            string url = serverUrls.Any()
                ? (serverUrls.FirstOrDefault(u => u.StartsWith("https")) ?? serverUrls.First())
                : "https://localhost:5007";
            Console.WriteLine($"🌐 Öffne Browser: {url}");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
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

app.Run();