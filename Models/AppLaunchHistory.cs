#nullable enable
using System;
using System.ComponentModel.DataAnnotations;
using AppManager.Data;

namespace AppManager.Models
{
    public class AppLaunchHistory
    {
        public Guid Id { get; set; } = Guid.NewGuid();// Eindeutige ID des Historieneintrags

        public Guid ApplicationId { get; set; }// Fremdschlüssel zur Application

        public Application? Application { get; set; }// Navigation Property zur Application Klasse

        public string UserId { get; set; } = string.Empty;// ID des Benutzers, der die App gestartet hat

        [StringLength(100)]// Windows-Anmeldename des App-Owners
        public string WindowsUsername { get; set; } = string.Empty; // Windows-Anmeldename des App-Owners

        [StringLength(100)]// ist nützlich für IIS Apps und begrenzt die Länge des Pool-Namens
        public string IISAppPoolName { get; set; } = string.Empty; // Name des IIS App Pools

        public DateTime LaunchTime { get; set; } = DateTime.Now;// Zeitpunkt des Starts
        public string Action { get; set; } = string.Empty; // "Start", "Stop", "Restart"

        [StringLength(500)]// Optionaler Grund vom App-Owner
        public string Reason { get; set; } = string.Empty;

        // Navigation Properties
        public virtual AppUser? User { get; set; }// Verknüpfung zum AppUser

    }
}
