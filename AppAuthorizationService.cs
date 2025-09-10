using System;
using System.Linq;
using AppManager.Data;
using AppManager.Models;

namespace AppManager
{
    public class AppAuthorizationService
    {
        private readonly AppDbContext _context;

        public AppAuthorizationService(AppDbContext context)
        {
            _context = context;
        }

        public bool HasAppAccess(AppUser user, Guid appId)
        {
            if (user.IsGlobalAdmin)
                return true;

            return _context.AppOwnerships.Any(o => o.UserId == user.Id && o.ApplicationId == appId);
        }
    }
}