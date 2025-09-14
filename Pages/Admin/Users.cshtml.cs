using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Authorization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppManager.Data;
using System.IO;

namespace AppManager.Pages.Admin
{
    [Authorize(Policy = "Admin")] // Nur für Admins (basierend auf Windows-Gruppe)
    public class UsersModel : PageModel
    {
        private readonly AppDbContext _context;
        private readonly UserManager<AppUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly IEmailSender _emailSender;

        public UsersModel(AppDbContext context,
                          UserManager<AppUser> userManager,
                          RoleManager<IdentityRole> roleManager,
                          IEmailSender emailSender)
        {
            _context = context;
            _userManager = userManager;
            _roleManager = roleManager;
            _emailSender = emailSender;
        }

        public List<AppUser> Users { get; set; } = new();

        [BindProperty]
        public AppUser NewUser { get; set; }

        [BindProperty]
        public AppUser EditUser { get; set; } = new AppUser();

        public void OnGet()
        {
            Users = _context.Users.OrderByDescending(u => u.CreatedAt).ToList();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid) return Page();

            var user = new AppUser
            {
                UserName = NewUser.Email,
                Email = NewUser.Email,
                Vorname = NewUser.Vorname,
                Abteilung = NewUser.Abteilung,
                IsActive = true,
                EmailConfirmed = false,
                CreatedAt = DateTime.Now
            };

            var tempPassword = Path.GetRandomFileName().Replace(".", "").Substring(0, 10) + "!";

            var result = await _userManager.CreateAsync(user, tempPassword);
            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                    ModelState.AddModelError(string.Empty, error.Description);
                return Page();
            }

            if (!await _roleManager.RoleExistsAsync("Admin"))
            {
                await _roleManager.CreateAsync(new IdentityRole("Admin"));
            }

            await _userManager.AddToRoleAsync(user, "Admin");

            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetUrl = Url.Page(
                "/Account/ResetPassword",
                null,
                new { userId = user.Id, token = resetToken },
                Request.Scheme);

            await _emailSender.SendEmailAsync(
                user.Email,
                "Passwort festlegen",
                $"Bitte lege dein Passwort fest: <a href='{resetUrl}'>Jetzt festlegen</a>");

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user != null)
            {
                await _userManager.DeleteAsync(user);
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostEditAsync()
        {
            if (!ModelState.IsValid) return Page();

            var user = await _userManager.FindByIdAsync(EditUser.Id);
            if (user != null)
            {
                user.UserName = EditUser.Email;
                user.Email = EditUser.Email;
                user.IsActive = EditUser.IsActive;
                user.Vorname = EditUser.Vorname;
                user.Abteilung = EditUser.Abteilung;
                user.UpdatedAt = DateTime.Now;

                await _userManager.UpdateAsync(user);
            }

            return RedirectToPage();
        }
    }
}
