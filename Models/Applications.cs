using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AppManager.Models
{
    // Die Application-Klasse repräsentiert eine verwaltete Anwendung im System
    public class Application
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid(); // Eindeutige ID der Anwendung

        [Required]
        public string Name { get; set; } = string.Empty; // Name der Anwendung

        public string Description { get; set; } = string.Empty; // Beschreibung der Anwendung

        public bool IsStarted { get; set; } = false; // Gibt an, ob die Anwendung aktuell läuft

        public bool RestartRequired { get; set; } = false; // Gibt an, ob ein Neustart erforderlich ist

        // Pfad zur ausführbaren Datei der Anwendung
        [Required]
        public string ExecutablePath { get; set; } = string.Empty;

        // Prozess-ID des laufenden Programms (falls gestartet)
        public int? ProcessId { get; set; }

        // Arbeitsverzeichnis, in dem die Anwendung ausgeführt wird (optional)
        public string WorkingDirectory { get; set; } = string.Empty;

        // Kommandozeilen-Argumente für den Start der Anwendung (optional)
        public string Arguments { get; set; } = string.Empty;

        // Gibt an, ob Administratorrechte zum Ausführen benötigt werden
        public bool RequiresAdmin { get; set; } = false;

        // IIS Integration: Name des zugehörigen IIS App Pools (optional)
        [StringLength(100)]
        public string IISAppPoolName { get; set; } = string.Empty;

        // IIS Integration: Name der zugehörigen IIS-Website (optional)
        [StringLength(200)]
        public string IISSiteName { get; set; } = string.Empty;

        // Gibt an, ob es sich um eine IIS-Anwendung handelt
        public bool IsIISApplication { get; set; } = false;

        // Zeitpunkt des letzten Starts der Anwendung
        public DateTime LastLaunchTime { get; set; }

        // Grund für den letzten Start (z.B. "Neustart", "Update", etc.)
        public string LastLaunchReason { get; set; }

        // Historie aller Starts/Stopps/Neustarts der Anwendung
        public List<AppLaunchHistory> LaunchHistory { get; set; } = new List<AppLaunchHistory>();

        // Liste der Benutzer, die als Owner/Berechtigte für die Anwendung eingetragen sind
        public List<AppOwnership> Owners { get; set; } = new List<AppOwnership>();

        // Pfad zum Icon der Anwendung (optional)
        public string IconPath { get; set; }

        // Versionsnummer der Anwendung (optional)
        public string Version { get; set; }

        // Kategorie der Anwendung (optional, z.B. "Web", "Service", "Desktop")
        public string Category { get; set; }

        // Tags zur Kategorisierung/Suche (optional, z.B. "Finance;HR;Tool")
        public string Tags { get; set; } 

        // Hinweis: 'Path' entfernt, bitte ExecutablePath verwenden
    }
}
