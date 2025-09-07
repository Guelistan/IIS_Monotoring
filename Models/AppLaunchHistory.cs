#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using AppManager.Data;

namespace AppManager.Models
{
    public class AppLaunchHistory
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid ApplicationId { get; set; }

        public Application? Application { get; set; }

        public string UserId { get; set; } = string.Empty;

        [StringLength(100)]
        public string WindowsUsername { get; set; } = string.Empty; // Windows-Anmeldename des App-Owners

        [StringLength(100)]
        public string IISAppPoolName { get; set; } = string.Empty; // Name des IIS App Pools

        public DateTime LaunchTime { get; set; } = DateTime.Now;

        public string Action { get; set; } = string.Empty; // "Start", "Stop", "Restart"

        [StringLength(500)]
        public string Reason { get; set; } = string.Empty; // Optionaler Grund vom App-Owner

        // Navigation Properties
        public virtual AppUser? User { get; set; }

    }
}
