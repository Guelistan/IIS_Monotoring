using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using AppManager.Models;

namespace AppManager.Data
{
    public class AppUser : IdentityUser
    {
        // Basis-Eigenschaften
        public bool IsActive { get; set; } = true;
        public bool IsGlobalAdmin { get; set; }
        
        // Windows-Authentifizierung
        public string WindowsSid { get; set; }        // Windows SID für Login-Verknüpfung
        public string WindowsUsername { get; set; }   // Domain\Username
        public string DomainName { get; set; }       // Nur der Domainname
        
        // Persönliche Daten
        public string Vorname { get; set; }
        public string Nachname { get; set; }
        public string Abteilung { get; set; }
        
        // Audit und System
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string CreatedBy { get; set; }
        
        // Computed Properties
        public string FullName => $"{Vorname} {Nachname}".Trim();
        public string DisplayName => string.IsNullOrEmpty(FullName) ? Email : FullName;
    }

    public class AppDbContext : IdentityDbContext<AppUser>
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<Application> Applications { get; set; }
        public DbSet<HistoryModel.ActivityLog> Logs { get; set; }
        public DbSet<LogEntry> LogEntries { get; set; }
        public DbSet<AppLaunchHistory> AppLaunchHistories { get; set; }
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

            modelBuilder.Entity<AppLaunchHistory>()
                .HasOne(alh => alh.Application)
                .WithMany(a => a.LaunchHistory)
                .HasForeignKey(alh => alh.ApplicationId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
