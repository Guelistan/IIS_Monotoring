using System;
using AppManager.Data;

namespace AppManager.Models 
{
    public static class HistoryModel
    {
        public class ActivityLog
        {
            public int Id { get; set; }
            public DateTime Timestamp { get; set; }
            public string Action { get; set; }
            public string Details { get; set; }
            public string UserId { get; set; }
            public virtual AppUser User { get; set; }
        }
    }
}