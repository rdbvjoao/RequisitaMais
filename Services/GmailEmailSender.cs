using System.Net;
using System.Net.Mail;
using System.Text;

namespace RequisitaPlus.Web.Services;

public sealed class GmailEmailSender(IConfiguration configuration) : IEmailSender
{
    public const string SenderAddress = "requisitaplus.sistema@gmail.com";

    public async Task SendAsync(string recipient, string subject, string body,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        if (recipient.Contains('\r') || recipient.Contains('\n') ||
            subject.Contains('\r') || subject.Contains('\n'))
            throw new ArgumentException("Destinatário e assunto não podem conter quebras de linha.");

        // MailAddress aceita somente um endereço; não há CC, BCC ou anexos.
        using var message = new MailMessage
        {
            From = new MailAddress(SenderAddress, "Requisita+"),
            Subject = subject,
            Body = body,
            IsBodyHtml = false,
            SubjectEncoding = Encoding.UTF8,
            BodyEncoding = Encoding.UTF8
        };
        message.To.Add(new MailAddress(recipient));

        var password = configuration["Email:Smtp:Password"];
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Configure Email:Smtp:Password no provedor seguro de configuração.");

        using var client = new SmtpClient("smtp.gmail.com", 587)
        {
            // STARTTLS obrigatório, com validação padrão do certificado pelo .NET.
            EnableSsl = true,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(SenderAddress, password),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            // Uma tentativa, sem repetição automática.
            await client.SendMailAsync(message, timeout.Token);
        }
        catch (SmtpException)
        {
            // Não propagar respostas do servidor ou exceções internas para logs/telas.
            throw new InvalidOperationException("Não foi possível enviar o e-mail pelo Gmail. Verifique a configuração e a conexão SMTP.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("O envio de e-mail excedeu o limite de 30 segundos.");
        }
    }
}
