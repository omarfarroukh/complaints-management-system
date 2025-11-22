using CMS.Application.Interfaces;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace CMS.Infrastructure.Services;

public class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _config;

    public SmtpEmailService(IConfiguration config)
    {
        _config = config;
    }

    public async Task SendEmailAsync(string to, string subject, string body)
    {
        var email = new MimeMessage();
        
        // 1. Sender
        email.From.Add(new MailboxAddress(
            _config["SmtpSettings:SenderName"], 
            _config["SmtpSettings:SenderEmail"]
        ));
        
        // 2. Receiver
        email.To.Add(MailboxAddress.Parse(to));
        
        // 3. Content
        email.Subject = subject;
        var builder = new BodyBuilder { HtmlBody = body }; // Supports HTML
        email.Body = builder.ToMessageBody();

        // 4. Send via SMTP
        using var smtp = new SmtpClient();
        try
        {
            // Connect to Gmail
            await smtp.ConnectAsync(
                _config["SmtpSettings:Host"], 
                int.Parse(_config["SmtpSettings:Port"]!), 
                SecureSocketOptions.StartTls
            );

            // Authenticate
            await smtp.AuthenticateAsync(
                _config["SmtpSettings:SenderEmail"], 
                _config["SmtpSettings:Password"]
            );

            // Send
            await smtp.SendAsync(email);
        }
        finally
        {
            await smtp.DisconnectAsync(true);
        }
    }
}