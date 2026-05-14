namespace Kartist.Services
{
    public interface IMailService
    {
        Task GonderAsync(string toEmail, string subject, string body);
    }
}
