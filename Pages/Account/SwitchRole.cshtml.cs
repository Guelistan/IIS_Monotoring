using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using AppManager.Data;
using System.Threading.Tasks;
using System.Security.Claims;
using System.Collections.Generic;

namespace AppManager.Pages.Account
{
    [Authorize]
    public class SwitchRoleModel : PageModel
    {
        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;

        public SwitchRoleModel(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }

        public bool IsAdmin { get; set; }
        public bool IsSuperAdmin { get; set; }
        public bool IsCurrentlyInAdminMode { get; set; }

        public async Task<IActionResult> OnGetAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToPage("/Account/Login");
            }

            // Überprüfe, ob der Benutzer Admin-Rechte hat
            IsAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            IsSuperAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");

            if (!IsAdmin && !IsSuperAdmin)
            {
                // Benutzer hat keine Admin-Rechte
                return RedirectToPage("/Index");
            }

            // Überprüfe aktuellen Modus
            IsCurrentlyInAdminMode = User.HasClaim("AdminMode", "true");

            return Page();
        }

        public async Task<IActionResult> OnPostToggleAdminModeAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
            {
                return RedirectToPage("/Account/Login");
            }

            // Überprüfe, ob der Benutzer Admin-Rechte hat
            var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
            var isSuperAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");

            if (!isAdmin && !isSuperAdmin)
            {
                return RedirectToPage("/Index");
            }

            // Aktueller Admin-Modus Status
            var currentAdminMode = User.HasClaim("AdminMode", "true");

            // Claims aktualisieren
            var claims = new List<Claim>();
            
            if (currentAdminMode)
            {
                // Admin-Modus deaktivieren
                claims.Add(new Claim("AdminMode", "false"));
            }
            else
            {
                // Admin-Modus aktivieren
                claims.Add(new Claim("AdminMode", "true"));
            }

            // Benutzer neu anmelden mit aktualisierten Claims
            await _signInManager.SignOutAsync();
            await _signInManager.SignInWithClaimsAsync(user, false, claims);

            // Zurück zur gewünschten Seite
            if (!currentAdminMode)
            {
                // Wechsel zu Admin-Modus -> Admin Dashboard
                return RedirectToPage("/Admin/Dashboard");
            }
            else
            {
                // Wechsel zu normalem Modus -> Hauptseite
                return RedirectToPage("/Index");
            }
        }
    }
}
