using System;
using System.Collections.Generic;
using System.Linq;
using AppManager.Data;
using AppManager.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AppManager
{
    public class DuplicateRemover
    {
        private readonly AppDbContext _context;
        private readonly ILogger<DuplicateRemover> _logger;

        public DuplicateRemover(AppDbContext context, ILogger<DuplicateRemover> logger)
        {
            _context = context;
            _logger = logger;
        }

        public DuplicateAnalysisResult AnalyzeDuplicates()
        {
            var result = new DuplicateAnalysisResult();

            _logger.LogInformation("🔍 Starte Duplikat-Analyse...");

            var duplicateApps = FindDuplicateApplications();
            result.DuplicateApplications = duplicateApps;

            var duplicateOwnerships = FindDuplicateOwnership();
            result.DuplicateOwnerships = duplicateOwnerships;

            var duplicateHistory = FindDuplicateLaunchHistory();
            result.DuplicateLaunchHistory = duplicateHistory;

            var orphanedRecords = FindOrphanedRecords();
            result.OrphanedRecords = orphanedRecords;

            return result;
        }

        private List<DuplicateGroup<Application>> FindDuplicateApplications()
        {
            _logger.LogInformation("📱 Suche nach doppelten Anwendungen...");

            var applications = _context.Applications.ToList();

            var duplicateGroups = applications
                .GroupBy(app => new { app.Name, app.ExecutablePath })
                .Where(group => group.Count() > 1)
                .Select(group => new DuplicateGroup<Application>
                {
                    Key = $"Name: {group.Key.Name}, Path: {group.Key.ExecutablePath}",
                    Items = group.ToList(),
                    Count = group.Count()
                })
                .ToList();

            _logger.LogInformation($"📊 Gefunden: {duplicateGroups.Count} Gruppen von doppelten Anwendungen");
            return duplicateGroups;
        }

        private List<DuplicateGroup<AppOwnership>> FindDuplicateOwnership()
        {
            _logger.LogInformation("👥 Suche nach doppelten App-Owner-Berechtigungen...");

            var ownerships = _context.AppOwnerships
                .Include(o => o.User)
                .Include(o => o.Application)
                .ToList();

            var duplicateGroups = ownerships
                .GroupBy(ownership => new { ownership.UserId, ownership.ApplicationId })
                .Where(group => group.Count() > 1)
                .Select(group => new DuplicateGroup<AppOwnership>
                {
                    Key = $"User: {group.First().User?.UserName}, App: {group.First().Application?.Name}",
                    Items = group.ToList(),
                    Count = group.Count()
                })
                .ToList();

            _logger.LogInformation($"📊 Gefunden: {duplicateGroups.Count} Gruppen von doppelten Berechtigungen");
            return duplicateGroups;
        }

        private List<DuplicateGroup<AppLaunchHistory>> FindDuplicateLaunchHistory()
        {
            _logger.LogInformation("📈 Suche nach doppelten Launch-Historie-Einträgen...");

            var history = _context.AppLaunchHistories
                .Include(h => h.User)
                .Include(h => h.Application)
                .ToList();

            var duplicateGroups = history
                .GroupBy(h => new {
                    h.UserId,
                    h.ApplicationId,
                    LaunchTimeMinute = new DateTime(h.LaunchTime.Year, h.LaunchTime.Month, h.LaunchTime.Day, h.LaunchTime.Hour, h.LaunchTime.Minute, 0)
                })
                .Where(group => group.Count() > 1)
                .Select(group => new DuplicateGroup<AppLaunchHistory>
                {
                    Key = $"User: {group.First().User?.UserName}, App: {group.First().Application?.Name}, Time: {group.Key.LaunchTimeMinute:yyyy-MM-dd HH:mm}",
                    Items = group.ToList(),
                    Count = group.Count()
                })
                .ToList();

            _logger.LogInformation($"📊 Gefunden: {duplicateGroups.Count} Gruppen von doppelten Historie-Einträgen");
            return duplicateGroups;
        }

        private OrphanedRecords FindOrphanedRecords()
        {
            _logger.LogInformation("🔗 Suche nach verwaisten Datensätzen...");

            var orphaned = new OrphanedRecords();

            orphaned.OrphanedOwnerships = _context.AppOwnerships
                .Where(o => o.User == null || o.Application == null)
                .Include(o => o.User)
                .Include(o => o.Application)
                .ToList();

            orphaned.OrphanedHistory = _context.AppLaunchHistories
                .Where(h => h.User == null || h.Application == null)
                .Include(h => h.User)
                .Include(h => h.Application)
                .ToList();

            _logger.LogInformation($"📊 Gefunden: {orphaned.OrphanedOwnerships.Count} verwaiste Berechtigungen, {orphaned.OrphanedHistory.Count} verwaiste Historie-Einträge");
            return orphaned;
        }

        public int RemoveDuplicates(DuplicateRemovalOptions options)
        {
            int totalRemoved = 0;

            _logger.LogInformation("🧹 Starte Duplikat-Entfernung...");

            var analysis = AnalyzeDuplicates();

            if (options.RemoveDuplicateApplications && analysis.DuplicateApplications.Any())
            {
                totalRemoved += RemoveDuplicateApplications(analysis.DuplicateApplications);
            }

            if (options.RemoveDuplicateOwnerships && analysis.DuplicateOwnerships.Any())
            {
                totalRemoved += RemoveDuplicateOwnerships(analysis.DuplicateOwnerships);
            }

            if (options.RemoveDuplicateHistory && analysis.DuplicateLaunchHistory.Any())
            {
                totalRemoved += RemoveDuplicateHistory(analysis.DuplicateLaunchHistory);
            }

            if (options.RemoveOrphanedRecords && (analysis.OrphanedRecords.OrphanedOwnerships.Any() || analysis.OrphanedRecords.OrphanedHistory.Any()))
            {
                totalRemoved += RemoveOrphanedRecords(analysis.OrphanedRecords);
            }

            _context.SaveChanges();
            _logger.LogInformation($"✅ Duplikat-Entfernung abgeschlossen. {totalRemoved} Einträge entfernt.");

            return totalRemoved;
        }

        private int RemoveDuplicateApplications(List<DuplicateGroup<Application>> duplicateGroups)
        {
            int removed = 0;
            foreach (var group in duplicateGroups)
            {
                var toKeep = group.Items.OrderBy(app => app.Id).First();
                var toRemove = group.Items.Skip(1).ToList();

                foreach (var app in toRemove)
                {
                    var ownerships = _context.AppOwnerships.Where(o => o.ApplicationId == app.Id).ToList();
                    foreach (var ownership in ownerships)
                    {
                        var existingOwnership = _context.AppOwnerships
                            .FirstOrDefault(o => o.UserId == ownership.UserId && o.ApplicationId == toKeep.Id);

                        if (existingOwnership == null)
                        {
                            ownership.ApplicationId = toKeep.Id;
                        }
                        else
                        {
                            _context.AppOwnerships.Remove(ownership);
                        }
                    }

                    var history = _context.AppLaunchHistories.Where(h => h.ApplicationId == app.Id).ToList();
                    foreach (var hist in history)
                    {
                        hist.ApplicationId = toKeep.Id;
                    }

                    _context.Applications.Remove(app);
                    removed++;
                    _logger.LogInformation($"🗑️ Doppelte Anwendung entfernt: {app.Name} (ID: {app.Id})");
                }
            }
            return removed;
        }

        private int RemoveDuplicateOwnerships(List<DuplicateGroup<AppOwnership>> duplicateGroups)
        {
            int removed = 0;
            foreach (var group in duplicateGroups)
            {
                var toKeep = group.Items.OrderBy(o => o.CreatedAt).First();
                var toRemove = group.Items.Skip(1).ToList();

                foreach (var ownership in toRemove)
                {
                    _context.AppOwnerships.Remove(ownership);
                    removed++;
                    _logger.LogInformation($"🗑️ Doppelte Berechtigung entfernt: {ownership.User?.UserName} -> {ownership.Application?.Name} (ID: {ownership.Id})");
                }
            }
            return removed;
        }

        private int RemoveDuplicateHistory(List<DuplicateGroup<AppLaunchHistory>> duplicateGroups)
        {
            int removed = 0;
            foreach (var group in duplicateGroups)
            {
                var toKeep = group.Items.OrderByDescending(h => h.LaunchTime).First();
                var toRemove = group.Items.Where(h => h.Id != toKeep.Id).ToList();

                foreach (var history in toRemove)
                {
                    _context.AppLaunchHistories.Remove(history);
                    removed++;
                    _logger.LogInformation($"🗑️ Doppelter Historie-Eintrag entfernt: {history.User?.UserName} -> {history.Application?.Name} @ {history.LaunchTime:yyyy-MM-dd HH:mm:ss} (ID: {history.Id})");
                }
            }
            return removed;
        }

        private int RemoveOrphanedRecords(OrphanedRecords orphaned)
        {
            int removed = 0;

            foreach (var ownership in orphaned.OrphanedOwnerships)
            {
                _context.AppOwnerships.Remove(ownership);
                removed++;
                _logger.LogInformation($"🗑️ Verwaiste Berechtigung entfernt (ID: {ownership.Id})");
            }

            foreach (var history in orphaned.OrphanedHistory)
            {
                _context.AppLaunchHistories.Remove(history);
                removed++;
                _logger.LogInformation($"🗑️ Verwaister Historie-Eintrag entfernt (ID: {history.Id})");
            }

            return removed;
        }
    }

    public class DuplicateAnalysisResult
    {
        public List<DuplicateGroup<Application>> DuplicateApplications { get; set; } = new();
        public List<DuplicateGroup<AppOwnership>> DuplicateOwnerships { get; set; } = new();
        public List<DuplicateGroup<AppLaunchHistory>> DuplicateLaunchHistory { get; set; } = new();
        public OrphanedRecords OrphanedRecords { get; set; } = new();

        public bool HasDuplicates =>
            DuplicateApplications.Any() ||
            DuplicateOwnerships.Any() ||
            DuplicateLaunchHistory.Any() ||
            OrphanedRecords.HasOrphans;
    }

    public class DuplicateGroup<T>
    {
        public string Key { get; set; } = string.Empty;
        public List<T> Items { get; set; } = new();
        public int Count { get; set; }
    }

    public class OrphanedRecords
    {
        public List<AppOwnership> OrphanedOwnerships { get; set; } = new();
        public List<AppLaunchHistory> OrphanedHistory { get; set; } = new();

        public bool HasOrphans => OrphanedOwnerships.Any() || OrphanedHistory.Any();
    }

    public class DuplicateRemovalOptions
    {
        public bool RemoveDuplicateApplications { get; set; } = true;
        public bool RemoveDuplicateOwnerships { get; set; } = true;
        public bool RemoveDuplicateHistory { get; set; } = true;
        public bool RemoveOrphanedRecords { get; set; } = true;
    }
}
