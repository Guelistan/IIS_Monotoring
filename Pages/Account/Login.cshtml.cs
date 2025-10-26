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
        public string ReturnUrl { get; set; }

        public class LoginInput
        {
            [Required(ErrorMessage = "Bitte geben Sie Ihren Benutzernamen ein")]
            public string Username { get; set; }

            [Required(ErrorMessage = "Bitte geben Sie Ihr Passwort ein")]
            [DataType(DataType.Password)]
            public string Password { get; set; }
            
            public bool RememberMe { get; set; }
            
            public string ReturnUrl { get; set; }
        }

        public void OnGet(string returnUrl = null, string message = null)
        {
            ReturnUrl = returnUrl ?? Url.Content("~/");
            if (!string.IsNullOrEmpty(message))
                Message = message;
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();
            
            ReturnUrl = Input.ReturnUrl ?? Url.Content("~/");

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
                ModelState.AddModelError(string.Empty, "Ungültige Anmeldedaten.");
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

            var result = await _signInManager.PasswordSignInAsync(user, Input.Password, Input.RememberMe, false);

            Console.WriteLine($"   SignIn Result: Succeeded={result.Succeeded}, RequiresTwoFactor={result.RequiresTwoFactor}, IsLockedOut={result.IsLockedOut}, IsNotAllowed={result.IsNotAllowed}");

            if (result.Succeeded)
            {
                Console.WriteLine($"✅ Login erfolgreich für: {user.UserName}");
                
                // Überprüfe, ob der Benutzer Admin-Rechte hat
                var isAdmin = await _userManager.IsInRoleAsync(user, "Admin");
                var isSuperAdmin = await _userManager.IsInRoleAsync(user, "SuperAdmin");
                
                if (isAdmin || isSuperAdmin)
                {
                    Console.WriteLine($"🛡️ Admin/SuperAdmin angemeldet: {user.UserName}");
                    return LocalRedirect(ReturnUrl.Contains("/Admin") ? ReturnUrl : "/Admin/Dashboard");
                }
                
                return LocalRedirect(ReturnUrl);
            }

            if (result.IsNotAllowed)
            {
                Console.WriteLine($"❌ Login nicht erlaubt für: {user.UserName}");
                ModelState.AddModelError(string.Empty, "Anmeldung nicht erlaubt.");
            }
            else if (result.IsLockedOut)
            {
                Console.WriteLine($"❌ Benutzer ist gesperrt: {user.UserName}");
                ModelState.AddModelError(string.Empty, "Benutzer ist gesperrt.");
            }
            else
            {
                Console.WriteLine($"❌ Falsches Passwort für: {user.UserName}");
                ModelState.AddModelError(string.Empty, "Ungültige Anmeldedaten.");
            }

            return Page();
        }
    }
}
