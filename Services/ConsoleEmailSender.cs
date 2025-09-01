// Datei: Services/ConsoleEmailSender.cs

using Microsoft.AspNetCore.Identity.UI.Services;
using System.Diagnostics;
using System.Threading.Tasks;

namespace AppManager.Services
{
    public class ConsoleEmailSender : IEmailSender
    {
        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            Debug.WriteLine($"📧 To: {email}");
            Debug.WriteLine($"📌 Subject: {subject}");
            Debug.WriteLine($"📄 Message: {htmlMessage}");
            return Task.CompletedTask;
        }
    }
}
