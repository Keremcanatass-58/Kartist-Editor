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

            bool emailSettingsHazir = !string.IsNullOrWhiteSpace(emailSettings["Mail"]) &&
                                      !string.IsNullOrWhiteSpace(emailSettings["Password"]);
            bool smtpSettingsHazir = !string.IsNullOrWhiteSpace(smtpSettings["User"]) &&
                                     !string.IsNullOrWhiteSpace(smtpSettings["Pass"]);

            string host, gonderenMail, kullanici, uygulamaSifresi, gonderenAd;
            int port;

            if (emailSettingsHazir)
            {
                host = emailSettings["Host"] ?? "smtp.gmail.com";
                port = int.TryParse(emailSettings["Port"], out var p) ? p : 587;
                gonderenMail = emailSettings["Mail"]!;
                kullanici = emailSettings["Mail"]!;
                uygulamaSifresi = emailSettings["Password"]!;
                gonderenAd = smtpSettings["FromName"] ?? "Kartist";
            }
            else if (smtpSettingsHazir)
            {
                host = smtpSettings["Host"] ?? "smtp.gmail.com";
                port = int.TryParse(smtpSettings["Port"], out var p) ? p : 587;
                gonderenMail = smtpSettings["From"] ?? smtpSettings["User"]!;
                kullanici = smtpSettings["User"]!;
                uygulamaSifresi = smtpSettings["Pass"]!;
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
                await client.ConnectAsync(host, port, SecureSocketOptions.StartTls);
                await client.AuthenticateAsync(kullanici, uygulamaSifresi);
                await client.SendAsync(message);
            }
            finally
            {
                await client.DisconnectAsync(true);
            }
        }
    }
}
