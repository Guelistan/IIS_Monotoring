using System;
using System.Collections.Generic;
using System.Linq;
using AppManager.Data;
using AppManager.Models;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
    
namespace AppManager.Models
{
    public class LogEntry
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public string Username { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
