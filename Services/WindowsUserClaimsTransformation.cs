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
    public class WindowsUserClaimsTransformation : IClaimsTransformation
    {
        private readonly UserManager<AppManager.Data.AppUser> _userManager;
        private readonly ILogger<WindowsUserClaimsTransformation> _logger;

        public WindowsUserClaimsTransformation(
            UserManager<AppManager.Data.AppUser> userManager,
            ILogger<WindowsUserClaimsTransformation> logger)
        {
            _userManager = userManager;
            _logger = logger;
        }

        public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
        {
            // Nur für authentifizierte Windows-Benutzer fortfahren
            if (principal.Identity == null || !principal.Identity.IsAuthenticated || 
                !(principal.Identity is WindowsIdentity))
                return principal;

            var windowsIdentity = (WindowsIdentity)principal.Identity;
            var sid = windowsIdentity.User?.Value;

            if (string.IsNullOrEmpty(sid))
            {
                _logger.LogWarning("Keine Windows SID gefunden für {Name}", principal.Identity.Name);
                return principal;
            }

            // Existierenden Benutzer suchen oder neu erstellen
            var user = await GetOrCreateUser(windowsIdentity, sid);
            if (user == null)
                return principal;

            // Claims Liste erstellen
            var identity = new ClaimsIdentity("Windows");

            // Standard Claims
            identity.AddClaim(new Claim(ClaimTypes.Name, user.UserName));
            identity.AddClaim(new Claim(ClaimTypes.Role, "User")); // Default Rolle

            // Rollen
            var roles = await _userManager.GetRolesAsync(user);
            foreach (var role in roles)
                identity.AddClaim(new Claim(ClaimTypes.Role, role));

            // Windows Info
            if (!string.IsNullOrEmpty(sid))
                identity.AddClaim(new Claim(ClaimTypes.Sid, sid));
            if (!string.IsNullOrEmpty(windowsIdentity.Name))
                identity.AddClaim(new Claim("windows_username", windowsIdentity.Name));

            return new ClaimsPrincipal(identity);
        }

        private async Task<AppManager.Data.AppUser> GetOrCreateUser(WindowsIdentity windowsIdentity, string sid)
        {
            try
            {
                // Existierenden Benutzer suchen
                var user = await _userManager.FindByLoginAsync("Windows", sid);
                if (user != null)
                    return user;

                // Neuen Benutzer aus AD-Info erstellen
                using (var context = new PrincipalContext(ContextType.Domain))
                {
                    var userPrincipal = UserPrincipal.FindByIdentity(context, IdentityType.Sid, sid);
                    if (userPrincipal == null)
                    {
                        _logger.LogWarning("Kein AD-Benutzer gefunden für {Sid}", sid);
                        return null;
                    }

                    // Neuen Benutzer anlegen
                    user = new AppManager.Data.AppUser
                    {
                        UserName = windowsIdentity.Name,
                        WindowsSid = sid,
                        WindowsUsername = windowsIdentity.Name,
                        DomainName = windowsIdentity.Name?.Split('\\').FirstOrDefault(),
                        IsActive = true
                    };

                    var result = await _userManager.CreateAsync(user);
                    if (!result.Succeeded)
                    {
                        _logger.LogError("Fehler beim Erstellen: {Errors}", 
                            string.Join(", ", result.Errors.Select(e => e.Description)));
                        return null;
                    }

                    // Windows-Login verknüpfen
                    await _userManager.AddLoginAsync(user, 
                        new UserLoginInfo("Windows", sid, windowsIdentity.Name));

                    _logger.LogInformation("Neuer Windows-Benutzer: {Name}", user.UserName);
                    return user;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fehler bei AD-Abfrage für {Name}", windowsIdentity.Name);
                return null;
            }
        }
    }
}