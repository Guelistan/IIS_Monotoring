using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using AppManager.Data;
using AppManager.Models;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity.UI.Services;
using System;

namespace AppManager.Pages.Account
{
    public class RegisterModel : PageModel
    {


        private readonly UserManager<AppUser> _userManager;
        private readonly SignInManager<AppUser> _signInManager;
        private readonly IEmailSender _emailSender; // Du brauchst eine Implementierung

        public RegisterModel(UserManager<AppUser> userManager, SignInManager<AppUser> signInManager, IEmailSender emailSender)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _emailSender = emailSender;
        }

        [BindProperty]
        public RegisterInput Input { get; set; } = new();

        public class RegisterInput
        {
            [Required]
            [EmailAddress]
            public string Email { get; set; }

            [Required]
            public string Name { get; set; }

            [Required]
            public string Vorname { get; set; }

            [Required]
            public string Abteilung { get; set; }

            [Required]
            [DataType(DataType.Password)]
            public string Passwort { get; set; }
        }

        public void OnGet() { }

        public async Task<IActionResult> OnPostAsync()
        {
            Console.WriteLine($"🔍 Registrierungs-Versuch gestartet...");
            Console.WriteLine($"   ModelState.IsValid: {ModelState.IsValid}");

            if (!ModelState.IsValid)
            {
                Console.WriteLine("❌ ModelState ist ungültig:");
                foreach (var modelError in ModelState)
                {
                    foreach (var error in modelError.Value.Errors)
                    {
                        Console.WriteLine($"   - {modelError.Key}: {error.ErrorMessage}");
                    }
                }
                return Page();
            }

            Console.WriteLine($"✅ ModelState ist gültig. Benutzer-Daten:");
            Console.WriteLine($"   Benutzername: '{Input.Name}'");
            Console.WriteLine($"   E-Mail: '{Input.Email}'");
            Console.WriteLine($"   Vorname: '{Input.Vorname}'");
            Console.WriteLine($"   Abteilung: '{Input.Abteilung}'");

            var user = new AppUser
            {
                UserName = Input.Name, // Verwende den Namen als Username
                Email = Input.Email,
                Nachname = Input.Name, // Setze auch Nachname
                Vorname = Input.Vorname,
                Abteilung = Input.Abteilung,
                IsActive = true,
                EmailConfirmed = true  // Direkt bestätigen, da wir keine E-Mail-Bestätigung benötigen
            };

            Console.WriteLine($"🔧 Erstelle Benutzer in Datenbank...");
            var result = await _userManager.CreateAsync(user, Input.Passwort);

            Console.WriteLine($"   CreateAsync Result: Succeeded = {result.Succeeded}");

            if (result.Succeeded)
            {
                Console.WriteLine($"✅ Benutzer erfolgreich erstellt: {user.UserName}");
                Console.WriteLine($"🔐 Automatische Anmeldung...");

                // Direkt anmelden ohne E-Mail-Bestätigung
                await _signInManager.SignInAsync(user, isPersistent: false);

                Console.WriteLine($"✅ Automatische Anmeldung erfolgreich");
                return RedirectToPage("/Admin/Dashboard");
            }

            Console.WriteLine($"❌ Benutzer-Erstellung fehlgeschlagen:");
            foreach (var error in result.Errors)
            {
                Console.WriteLine($"   - {error.Code}: {error.Description}");
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }
    }
}
