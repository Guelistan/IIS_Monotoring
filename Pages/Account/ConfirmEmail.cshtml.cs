/* using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using AppManager.Models;
using System.Threading.Tasks;
using AppManager.Data;

namespace AppManager.Pages.Account
{
    public class ConfirmEmailModel : PageModel
    {
        private readonly UserManager<AppUser> _userManager;

        public ConfirmEmailModel(UserManager<AppUser> userManager)
        {
            _userManager = userManager;
        }

        public async Task<IActionResult> OnGetAsync(string userId, string token)
        {
            if (userId == null || token == null)
                return RedirectToPage("/Index");

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return NotFound($"Kein Benutzer mit der ID '{userId}' gefunden.");

            var result = await _userManager.ConfirmEmailAsync(user, token);

            return Page();
        }
    }
}
 */