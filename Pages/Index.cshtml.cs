using AppManager.Data;
using AppManager.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Collections.Generic;
using System.Linq;

namespace AppManager.Pages
{
    public class IndexModel : PageModel
    {
        private readonly AppDbContext _context;

        public IndexModel(AppDbContext context)
        {
            _context = context;
        }

        public List<Application> Applications { get; set; } = new();

        public void OnGet()
        {
            Applications = _context.Applications.ToList();
        }
    }
}
