using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using AppManager.Models;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using AppManager.Pages.Admin;


namespace AppManager.Data
{
    public class AppUser : IdentityUser
    {
        public bool IsGlobalAdmin { get; set; }

        public bool IsActive { get; set; }
        public string Vorname { get; set; }
        public string Nachname { get; set; }
        public string Abteilung { get; set; }
        public System.DateTime CreatedAt { get; set; } = System.DateTime.Now;
        public string CreatedBy { get; set; }
        public System.DateTime UpdatedAt { get; set; } = System.DateTime.Now;
        // weitere Felder nach Bedarf
    }

    public class AppDbContext : IdentityDbContext<AppUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        //public new DbSet<AppUser> Users { get; set; }
        public DbSet<Application> Applications { get; set; }
        public DbSet<HistoryModel.ActivityLog> Logs { get; set; }
        public DbSet<LogEntry> LogEntries { get; set; }
        public DbSet<AppLaunchHistory> AppLaunchHistories { get; set; }

        // 👥 NEU: App-Owner Berechtigungen
        public DbSet<AppOwnership> AppOwnerships { get; set; }


        public List<AppUser> GetUsersOrderedByCreationDate()
        {
            return Users.OrderByDescending(u => u.CreatedAt).ToList();
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<HistoryModel.ActivityLog>()
                .HasOne(log => log.User)
                .WithMany()
                .HasForeignKey(log => log.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // 👥 App-Owner Beziehungen
            modelBuilder.Entity<AppOwnership>()
                .HasOne(ao => ao.User)
                .WithMany()
                .HasForeignKey(ao => ao.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AppOwnership>()
                .HasOne(ao => ao.Application)
                .WithMany(a => a.Owners)
                .HasForeignKey(ao => ao.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);

            // 📊 App Launch History Beziehungen
            modelBuilder.Entity<AppLaunchHistory>()
                .HasOne(alh => alh.Application)
                .WithMany(a => a.LaunchHistory)
                .HasForeignKey(alh => alh.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        }

    }
}
