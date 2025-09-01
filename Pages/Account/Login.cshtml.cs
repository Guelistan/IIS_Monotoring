using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using AppManager.Data;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Security.Claims;

namespace AppManager.Pages.Account
{
    public class LoginModel : PageModel
    {
        private readonly SignInManager<AppUser> _signInManager;
        private readonly UserManager<AppUser> _userManager;

        public LoginModel(SignInManager<AppUser> signInManager, UserManager<AppUser> userManager)
        {
            _signInManager = signInManager;
            _userManager = userManager;
        }

        [BindProperty]
        public LoginInput Input { get; set; }

        public string Message { get; set; }

        public class LoginInput
        {
            [Required]
            public string Username { get; set; }

            [Required]
            [DataType(DataType.Password)]
            public string Password { get; set; }
        }

        public void OnGet(string message = null)
        {
            if (!string.IsNullOrEmpty(message))
                Message = message;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            Console.WriteLine($"🔍 Login-Versuch für: '{Input.Username}'");

            // Versuche zuerst mit Username zu finden
            var user = await _userManager.FindByNameAsync(Input.Username);
            Console.WriteLine($"   FindByNameAsync: {(user != null ? "Gefunden" : "Nicht gefunden")}");

            // Falls nicht gefunden, versuche mit E-Mail
            if (user == null)
            {
                user = await _userManager.FindByEmailAsync(Input.Username);
                Console.WriteLine($"   FindByEmailAsync: {(user != null ? "Gefunden" : "Nicht gefunden")}");
            }

            if (user == null)
            {
                Console.WriteLine($"❌ Benutzer '{Input.Username}' wurde nicht gefunden");
                ModelState.AddModelError(string.Empty, "Ungültige Anmeldedaten. Benutzer wurde nicht gefunden.");
                return Page();
            }

            if (!user.IsActive)
            {
                Console.WriteLine($"❌ Benutzer '{user.UserName}' ist inaktiv");
                ModelState.AddModelError(string.Empty, "Benutzer ist inaktiv.");
                return Page();
            }

            Console.WriteLine($"✅ Benutzer gefunden: {user.UserName} (Email: {user.Email})");
            Console.WriteLine($"   Versuche Passwort-Überprüfung...");

            var result = await _signInManager.PasswordSignInAsync(user, Input.Password, false, false);

            Console.WriteLine($"   SignIn Result: Succeeded={result.Succeeded}, RequiresTwoFactor={result.RequiresTwoFactor}, IsLockedOut={result.IsLockedOut}, IsNotAllowed={result.IsNotAllowed}");

            if (result.Succeeded)
            {
                Console.WriteLine($"✅ Login erfolgreich für: {user.UserName}");
                
                // Überprüfe, ob der Benutzer Admin-Rechte hat
                var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
                var isSuperAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
                
                if (isAdmin || isSuperAdmin)
                {
                    // Für Admins: Standardmäßig Admin-Modus aktivieren
                    await _signInManager.SignOutAsync();
                    var claims = new List<Claim>
                    {
                        new Claim("AdminMode", "true")
                    };
                    await _signInManager.SignInWithClaimsAsync(user, false, claims);
                    Console.WriteLine($"🛡️ Admin-Modus aktiviert für: {user.UserName}");
                }
                
                return RedirectToPage("/Admin/Dashboard");
            }

            if (result.IsNotAllowed)
            {
                Console.WriteLine($"❌ Login nicht erlaubt für: {user.UserName} (EmailConfirmed: {user.EmailConfirmed})");
                ModelState.AddModelError(string.Empty, "Anmeldung nicht erlaubt. Möglicherweise ist die E-Mail nicht bestätigt.");
            }
            else if (result.IsLockedOut)
            {
                Console.WriteLine($"❌ Benutzer ist gesperrt: {user.UserName}");
                ModelState.AddModelError(string.Empty, "Benutzer ist gesperrt.");
            }
            else
            {
                Console.WriteLine($"❌ Falsches Passwort für: {user.UserName}");
                ModelState.AddModelError(string.Empty, "Anmeldung fehlgeschlagen. Bitte überprüfen Sie Benutzername und Passwort.");
            }

            return Page();
        }
    }
}
