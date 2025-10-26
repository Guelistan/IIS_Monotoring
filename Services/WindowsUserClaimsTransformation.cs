<<<<<<< HEAD
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Principal;
using System.DirectoryServices.AccountManagement;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using AppManager.Data;

namespace AppManager.Services
{
    // Diese Klasse transformiert die Claims eines Windows-Benutzers für die Authentifizierung
    public class WindowsUserClaimsTransformation : IClaimsTransformation
    {
        private readonly UserManager<AppManager.Data.AppUser> _userManager;
        private readonly ILogger<WindowsUserClaimsTransformation> _logger;

        // Konstruktor mit Dependency Injection für UserManager und Logger
        public WindowsUserClaimsTransformation(
            UserManager<AppManager.Data.AppUser> userManager,
            ILogger<WindowsUserClaimsTransformation> logger)
        {
=======
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.Tasks;
using AppManager.Data;
using System.DirectoryServices.AccountManagement;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;

namespace AppManager.Services
{
    /// <summary>
    /// Windows Authentication Claims Transformation
    /// Erweitert den Windows-Benutzer um AppManager-spezifische Rollen und Claims
    /// </summary>
    public class WindowsUserClaimsTransformation : IClaimsTransformation
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly ILogger<WindowsUserClaimsTransformation> _logger;

        public WindowsUserClaimsTransformation(
            AppDbContext context,
            UserManager<AppUser> userManager,
            ILogger<WindowsUserClaimsTransformation> logger)
        {
            _context = context;
>>>>>>> 1e808df
            _userManager = userManager;
            _logger = logger;
        }

<<<<<<< HEAD
        // Transformiert die Claims des übergebenen ClaimsPrincipal
        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            // Nur für authentifizierte Windows-Benutzer fortfahren
            if (principal.Identity == null || !principal.Identity.IsAuthenticated ||
                !(principal.Identity is WindowsIdentity))
                return principal;

            var windowsIdentity = (WindowsIdentity)principal.Identity;
            var sid = windowsIdentity.User?.Value;

            // Prüfen, ob SID vorhanden ist
            if (string.IsNullOrEmpty(sid))
            {
                _logger.LogWarning("Keine Windows SID gefunden für {Name}", principal.Identity.Name);
                return principal;
            }

            // Existierenden Benutzer suchen oder neu erstellen
            var user = await GetOrCreateUser(windowsIdentity, sid);
            if (user == null)
                return principal;

            // Neue ClaimsIdentity für den Benutzer erstellen
            var identity = new ClaimsIdentity("Windows");

            // Standard Claims hinzufügen
            identity.AddClaim(new Claim(ClaimTypes.Name, user.UserName));
            identity.AddClaim(new Claim(ClaimTypes.Role, "User")); // Default Rolle

            // Rollen aus Identity hinzufügen
            var roles = await _userManager.GetRolesAsync(user);
            foreach (var role in roles)
                identity.AddClaim(new Claim(ClaimTypes.Role, role));

            // Windows-spezifische Informationen als Claims hinzufügen
            if (!string.IsNullOrEmpty(sid))
                identity.AddClaim(new Claim(ClaimTypes.Sid, sid));
            if (!string.IsNullOrEmpty(windowsIdentity.Name))
                identity.AddClaim(new Claim("windows_username", windowsIdentity.Name));

            // Rückgabe eines neuen ClaimsPrincipal mit den erweiterten Claims
            return new ClaimsPrincipal(identity);
        }

        // Sucht einen existierenden Benutzer oder erstellt einen neuen anhand der Windows-Identität
        private async Task<AppManager.Data.AppUser> GetOrCreateUser(WindowsIdentity windowsIdentity, string sid)
        {
            try
            {
                // Existierenden Benutzer anhand des Windows-Logins suchen
                var user = await _userManager.FindByLoginAsync("Windows", sid);
                if (user != null)
                    return user;

                // Benutzerinformationen aus Active Directory holen
                using (var context = new PrincipalContext(ContextType.Domain))
                {
                    var userPrincipal = UserPrincipal.FindByIdentity(context, IdentityType.Sid, sid);
                    if (userPrincipal == null)
                    {
                        _logger.LogWarning("Kein AD-Benutzer gefunden für {Sid}", sid);
                        return null;
                    }

                    // Neuen Benutzer anlegen und mit AD-Informationen füllen
                    user = new AppManager.Data.AppUser
                    {
                        UserName = windowsIdentity.Name,
                        WindowsSid = sid,
                        WindowsUsername = windowsIdentity.Name,
                        DomainName = windowsIdentity.Name?.Split('\\').FirstOrDefault(),
                        IsActive = true
                    };

                    // Benutzer in der Datenbank speichern
                    var result = await _userManager.CreateAsync(user);
                    if (!result.Succeeded)
                    {
                        _logger.LogError("Fehler beim Erstellen: {Errors}",
                            string.Join(", ", result.Errors.Select(e => e.Description)));
                        return null;
                    }

                    // Windows-Login mit dem Benutzer verknüpfen
                    await _userManager.AddLoginAsync(user,
                        new UserLoginInfo("Windows", sid, windowsIdentity.Name));

                    _logger.LogInformation("Neuer Windows-Benutzer: {Name}", user.UserName);
                    return user;
                }
            }
            catch (Exception ex)
            {
                // Fehlerbehandlung und Logging
                _logger.LogError(ex, "Fehler bei AD-Abfrage für {Name}", windowsIdentity.Name);
                return null;
            }
        }
    }
}

/*Claims:
Claims sind Aussagen über eine Identität, z. B. Name, Rolle oder andere Merkmale, die von einer vertrauenswürdigen 
Instanz (z. B. einem Identity Provider) ausgestellt werden. Sie werden zur Autorisierung und Authentifizierung in Anwendungen genutzt.

SID (Security Identifier):
SID steht für Security Identifier und ist ein eindeutiger Bezeichner, der in Windows-Systemen jedem Benutzer, 
jeder Gruppe oder jedem Sicherheitsprinzipal zugewiesen wird. Er dient zur Identifikation von Sicherheitsobjekten unabhängig von ihrem Anzeigenamen.*/

=======
        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            try
            {
                if (!principal.Identity?.IsAuthenticated == true)
                    return principal;

                var windowsIdentity = principal.Identity;
                var username = windowsIdentity.Name;

                if (string.IsNullOrEmpty(username))
                    return principal;

                // Windows-Username normalisieren (DOMAIN\User -> User)
                var normalizedUsername = username.Contains('\\') 
                    ? username.Split('\\')[1] 
                    : username;

                _logger.LogInformation($"🔍 Windows-User authentifiziert: {username} -> {normalizedUsername}");

                // AppUser aus Datenbank suchen oder erstellen
                var appUser = await _userManager.FindByNameAsync(normalizedUsername);
                if (appUser == null)
                {
                    // Automatisch neuen Benutzer erstellen
                    appUser = await CreateUserFromWindowsAccount(normalizedUsername, username);
                }
                else
                {
                    // Benutzer-Status prüfen
                    if (!appUser.IsActive)
                    {
                        _logger.LogWarning($"❌ Benutzer {normalizedUsername} ist deaktiviert");
                        return principal;
                    }
                }

                // Claims hinzufügen
                var claimsIdentity = new ClaimsIdentity(principal.Identity);
                
                // Benutzer-ID für spätere Verwendung
                claimsIdentity.AddClaim(new Claim(ClaimTypes.NameIdentifier, appUser.Id));
                claimsIdentity.AddClaim(new Claim("AppUserId", appUser.Id));
                claimsIdentity.AddClaim(new Claim("WindowsUsername", username));
                claimsIdentity.AddClaim(new Claim("NormalizedUsername", normalizedUsername));

                // Rollen hinzufügen
                if (appUser.IsGlobalAdmin)
                {
                    claimsIdentity.AddClaim(new Claim(ClaimTypes.Role, "Admin"));
                    claimsIdentity.AddClaim(new Claim(ClaimTypes.Role, "SuperAdmin"));
                    _logger.LogInformation($"🛡️ Admin-Rechte für {normalizedUsername} gesetzt");
                }

                // App-Owner Berechtigungen prüfen
                var ownedApps = await _context.AppOwnerships
                    .Where(ao => ao.UserId == appUser.Id)
                    .Select(ao => ao.ApplicationId)
                    .ToListAsync();

                if (ownedApps.Any())
                {
                    claimsIdentity.AddClaim(new Claim(ClaimTypes.Role, "AppOwner"));
                    foreach (var appId in ownedApps)
                    {
                        claimsIdentity.AddClaim(new Claim("OwnedApp", appId.ToString()));
                    }
                    _logger.LogInformation($"📱 AppOwner-Rechte für {normalizedUsername}: {ownedApps.Count} Apps");
                }

                // Standardrolle: User (nur lesen)
                claimsIdentity.AddClaim(new Claim(ClaimTypes.Role, "User"));

                return new ClaimsPrincipal(claimsIdentity);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, $"❌ Fehler beim Claims-Transformation für {principal.Identity?.Name}");
                return principal;
            }
        }

        private async Task<AppUser> CreateUserFromWindowsAccount(string normalizedUsername, string fullWindowsName)
        {
            try
            {
                // Active Directory Informationen abrufen (falls verfügbar)
                string vorname = "", nachname = "", email = "", abteilung = "";
                
                try
                {
                    // Nur auf Windows ausführen
                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                    {
                        using var context = new PrincipalContext(ContextType.Domain);
                        var userPrincipal = UserPrincipal.FindByIdentity(context, normalizedUsername);
                        if (userPrincipal != null)
                        {
                            vorname = userPrincipal.GivenName ?? "";
                            nachname = userPrincipal.Surname ?? "";
                            email = userPrincipal.EmailAddress ?? $"{normalizedUsername}@company.local";
                            abteilung = userPrincipal.Description ?? "";
                        }
                    }
                    else
                    {
                        email = $"{normalizedUsername}@company.local";
                    }
                }
                catch
                {
                    // Fallback wenn AD nicht verfügbar
                    email = $"{normalizedUsername}@company.local";
                }

                var newUser = new AppUser
                {
                    UserName = normalizedUsername,
                    Email = email,
                    EmailConfirmed = true,
                    Vorname = vorname.IsNullOrEmpty() ? normalizedUsername : vorname,
                    Nachname = nachname,
                    Abteilung = abteilung,
                    IsActive = true,
                    IsGlobalAdmin = false, // Neue Benutzer sind standardmäßig keine Admins
                    CreatedAt = DateTime.Now,
                    CreatedBy = "System (Windows Auth)"
                };

                var result = await _userManager.CreateAsync(newUser);
                if (result.Succeeded)
                {
                    _logger.LogInformation($"✅ Neuer Benutzer aus Windows AD erstellt: {normalizedUsername}");
                    return newUser;
                }
                else
                {
                    _logger.LogError($"❌ Fehler beim Erstellen des Benutzers {normalizedUsername}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                    throw new Exception($"Benutzer konnte nicht erstellt werden: {string.Join(", ", result.Errors.Select(e => e.Description))}");
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, $"❌ Fehler beim Erstellen des Benutzers aus Windows-Account: {normalizedUsername}");
                throw;
            }
        }
    }

    public static class StringExtensions
    {
        public static bool IsNullOrEmpty(this string value)
        {
            return string.IsNullOrEmpty(value);
        }
    }
}
>>>>>>> 1e808df
