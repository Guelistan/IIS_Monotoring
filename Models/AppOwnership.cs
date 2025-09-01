using System;
using System.ComponentModel.DataAnnotations;
using AppManager.Data;

namespace AppManager.Models
{
    /// <summary>
    /// App-Owner Berechtigung: Welcher Benutzer darf welche App verwalten
    /// </summary>
    public class AppOwnership
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = string.Empty; // Identity User ID

        [Required]
        public Guid ApplicationId { get; set; } // Verweis auf Application (Guid)

        [Required]
        [StringLength(100)]
        public string WindowsUsername { get; set; } = string.Empty; // Windows-Anmeldename

        [StringLength(100)]
        public string IISAppPoolName { get; set; } = string.Empty; // Name des IIS App Pools

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string CreatedBy { get; set; } = string.Empty;

        // Navigation Properties
        public virtual AppUser User { get; set; } = null!;
        public virtual Application Application { get; set; } = null!;
    }
}
