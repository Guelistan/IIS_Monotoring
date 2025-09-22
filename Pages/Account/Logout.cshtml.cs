using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using AppManager.Data;
using System.Threading.Tasks;

namespace AppManager.Pages.Account
{
    public class LogoutModel : PageModel
    {
        private readonly SignInManager<AppUser> _signInManager;

        public LogoutModel(SignInManager<AppUser> signInManager)
        {
            _signInManager = signInManager;
        }

        public async Task<IActionResult> OnGetAsync()
        {
            // Benutzer wirklich abmelden, indem der SignOutAsync aufgerufen wird
            await _signInManager.SignOutAsync();
            
            // Benutzer nach erfolgreichem Logout zur Startseite umleiten
            return RedirectToPage("/Index");
        }
    }
}
// ich brauche den log out um den user abzumelden, wenn er sich mit windows auth angemeldet hat. damit die seite nicht mehr zugreifbar ist.