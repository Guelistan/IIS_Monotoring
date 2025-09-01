using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using AppManager.Data;

namespace AppManager.Pages.Account
{
    public class ResetPasswordModel : PageModel
    {
        private readonly UserManager<AppUser> _userManager;

        public ResetPasswordModel(UserManager<AppUser> userManager)
        {
            _userManager = userManager;
        }

        [BindProperty]
        public ResetPasswordInput Input { get; set; }

        public string StatusMessage { get; set; }

        public class ResetPasswordInput
        {
            [Required]
            public string UserId { get; set; }

            [Required]
            public string Token { get; set; }

            [Required]
            [DataType(DataType.Password)]
            public string NewPassword { get; set; }

            [Required]
            [DataType(DataType.Password)]
            [Compare("NewPassword", ErrorMessage = "Die Passwörter stimmen nicht überein.")]
            public string ConfirmPassword { get; set; }
        }

        public void OnGet(string userId, string token)
        {
            Input = new ResetPasswordInput
            {
                UserId = userId,
                Token = token
            };
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            var user = await _userManager.FindByIdAsync(Input.UserId);
            if (user == null)
            {
                ModelState.AddModelError(string.Empty, "Benutzer nicht gefunden.");
                return Page();
            }

            var result = await _userManager.ResetPasswordAsync(user, Input.Token, Input.NewPassword);
            if (result.Succeeded)
            {
                StatusMessage = "Dein Passwort wurde erfolgreich zurückgesetzt.";
                return RedirectToPage("/Account/Login", new { Message = StatusMessage });
            }

            foreach (var error in result.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            return Page();
        }
    }
}
