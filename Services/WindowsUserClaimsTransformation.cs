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
            _userManager = userManager;
            _logger = logger;
        }
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
