using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Kartist.Services
{
    public class MailService : IMailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<MailService> _logger;

        public MailService(IConfiguration configuration, ILogger<MailService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task GonderAsync(string toEmail, string subject, string body)
        {
            var emailSettings = _configuration.GetSection("EmailSettings");
            var smtpSettings = _configuration.GetSection("Smtp");

            bool emailSettingsHazir = IsConfigured(emailSettings["Mail"]) &&
                                      IsConfigured(emailSettings["Password"]);
            bool smtpSettingsHazir = IsConfigured(smtpSettings["User"]) &&
                                     IsConfigured(smtpSettings["Pass"]);

            string host, gonderenMail, kullanici, uygulamaSifresi, gonderenAd;
            int port;

            if (smtpSettingsHazir)
            {
                host = smtpSettings["Host"] ?? "smtp.gmail.com";
                port = int.TryParse(smtpSettings["Port"], out var p) ? p : 587;
                gonderenMail = (smtpSettings["From"] ?? smtpSettings["User"]!).Trim();
                kullanici = smtpSettings["User"]!.Trim();
                uygulamaSifresi = NormalizeSmtpPassword(smtpSettings["Pass"]!);
                gonderenAd = smtpSettings["FromName"] ?? "Kartist";
            }
            else if (emailSettingsHazir)
            {
                host = emailSettings["Host"] ?? "smtp.gmail.com";
                port = int.TryParse(emailSettings["Port"], out var p) ? p : 587;
                gonderenMail = emailSettings["Mail"]!.Trim();
                kullanici = emailSettings["Mail"]!.Trim();
                uygulamaSifresi = NormalizeSmtpPassword(emailSettings["Password"]!);
                gonderenAd = smtpSettings["FromName"] ?? "Kartist";
            }
            else
            {
                throw new InvalidOperationException("SMTP ayarları eksik. appsettings.json içinde EmailSettings veya Smtp alanlarını doldurun.");
            }

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(gonderenAd, gonderenMail));
            message.To.Add(MailboxAddress.Parse(toEmail));
            message.Subject = subject;
            message.Body = new TextPart("html") { Text = body };

            using var client = new SmtpClient();
            try
            {
                var socketOptions = port == 465 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls;
                await client.ConnectAsync(host, port, socketOptions);
                await client.AuthenticateAsync(kullanici, uygulamaSifresi);
                await client.SendAsync(message);
            }
            finally
            {
                await client.DisconnectAsync(true);
            }
        }

        private static bool IsConfigured(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;

            var normalized = value.Trim();
            return !normalized.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase)
                   && !normalized.Contains("your-email", StringComparison.OrdinalIgnoreCase)
                   && !normalized.Contains("example.com", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeSmtpPassword(string password)
        {
            var normalized = password.Trim();
            return normalized.Length == 19 && normalized.Count(char.IsWhiteSpace) == 3
                ? new string(normalized.Where(c => !char.IsWhiteSpace(c)).ToArray())
                : normalized;
        }
    }
}
