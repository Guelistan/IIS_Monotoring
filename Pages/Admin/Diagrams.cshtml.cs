using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AppManager.Pages.Admin
{
    [Authorize(Roles = "Admin,SuperAdmin")]
    public class DiagramsModel : PageModel
    {
        public void OnGet()
        {
            // Diagramme werden direkt in der Razor Page angezeigt
        }
    }
}
