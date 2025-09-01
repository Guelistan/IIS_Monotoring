using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using AppManager.Data;
using AppManager.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace AppManager.Pages.Admin
{
    public class HistoryModel : PageModel
    {
        private readonly AppDbContext _context;

        public HistoryModel(AppDbContext context)
        {
            _context = context;
        }

        public List<AppLaunchHistory> History { get; set; }

        public async Task OnGetAsync()
        {
            History = await _context.AppLaunchHistories
                .Include(h => h.User)        // Benutzerinformationen laden
                .Include(h => h.Application) // App-Informationen laden
                .OrderByDescending(h => h.LaunchTime)
                .AsNoTracking()
                .ToListAsync();
        }


        public class ActivityLog
        {
            public int Id { get; set; }

            public string UserId { get; set; }

            [ForeignKey("UserId")]
            public AppUser User { get; set; }
        }

    }
}
