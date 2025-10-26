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
using Microsoft.AspNetCore.Server.IISIntegration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;

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

// 🔐 Identity-Konfiguration mit Cookie-basiertem Login
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedEmail = false;
        options.User.RequireUniqueEmail = false;
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 3;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
    })
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();

// Cookie-Konfiguration für Login
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
    options.SlidingExpiration = true;
});

// 📋 HTTP-Logging
builder.Services.AddHttpLogging(logging =>
{
    logging.LoggingFields = HttpLoggingFields.All;
});

// DI
builder.Services.AddScoped<ProgramManagerService>();
builder.Services.AddScoped<IISService>();
builder.Services.AddScoped<AppService>();
builder.Services.AddScoped<AppManager.AppAuthorizationService>();
builder.Services.AddScoped<CpuMonitoringService>();
builder.Services.AddScoped<WindowsUserClaimsTransformation>();
builder.Services.AddScoped<IClaimsTransformation, WindowsUserClaimsTransformation>();

// 🌐 URLs für Non-Development-Umgebung (bei IIS egal, wird ignoriert)
if (!builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls("http://localhost:5130", "https://localhost:5007");
}

// ⚙️ HTTPS-Redirect
var enforceHttps = builder.Configuration.GetValue<bool?>("EnforceHttpsRedirect") ?? (!builder.Environment.IsDevelopment());

// 🔐 Authentication: Cookie-basiertes Identity Login
// (Windows Authentication entfernt, da nur Cookie-Login benötigt wird)

// 🔐 Authorization Policies
builder.Services.AddAuthorization(options =>
{
    // Admin-Policy: Nur SuperAdmin und Admin
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole("Admin", "SuperAdmin"));
    
    // AppOwner-Policy: Admin, SuperAdmin oder AppOwner
    options.AddPolicy("AppOwnerOrAdmin", policy =>
        policy.RequireRole("Admin", "SuperAdmin", "AppOwner"));
    
    // User-Policy: Alle authentifizierten Benutzer (nur in Produktion)
    options.AddPolicy("AuthenticatedUser", policy =>
        policy.RequireAuthenticatedUser());
    // Keine FallbackPolicy, damit anonyme Benutzer zugelassen werden
});

// Razor Pages: Erweiterte Autorisierung
builder.Services.AddRazorPages().AddRazorPagesOptions(opts =>
{
    // Admin-Bereich nur für Admins
    opts.Conventions.AuthorizeFolder("/Admin", "AdminOnly");

    // Startseite und Privacy sind öffentlich (anonym)
    opts.Conventions.AllowAnonymousToPage("/Index");
    opts.Conventions.AllowAnonymousToPage("/Privacy");
    
    // Login/Logout/Register sind öffentlich
    opts.Conventions.AllowAnonymousToPage("/Account/Login");
    opts.Conventions.AllowAnonymousToPage("/Account/Register");
    opts.Conventions.AllowAnonymousToPage("/Account/Logout");
    opts.Conventions.AllowAnonymousToPage("/Account/AccessDenied");
});

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
    if (enforceHttps)
    {
        app.UseHsts();
    }
}
if (enforceHttps)
{
    app.UseHttpsRedirection();
}

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

// 🔐 Authentication & Authorization Pipeline
app.UseAuthentication();
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