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
            _userManager = userManager;
            _logger = logger;
        }

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

