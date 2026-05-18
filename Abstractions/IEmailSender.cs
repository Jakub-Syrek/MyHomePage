namespace MyHomePage.Abstractions;

/// <summary>
/// Sends transactional emails (currently only the password-reset link).
/// Abstracted so the rest of the auth flow doesn't care whether the
/// delivery channel is SMTP, SendGrid, a Railway log line, or a test
/// double. The default implementation writes the link to the structured
/// log so the operator can grab it from Railway logs without any extra
/// dependency — sufficient for a single-admin deployment.
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends the supplied message to the given recipient.
    /// </summary>
    /// <param name="toEmail">Recipient address.</param>
    /// <param name="subject">Subject line.</param>
    /// <param name="bodyText">Plain-text body. Already includes any
    /// reset link — the implementation must not transform it.</param>
    Task SendAsync(string toEmail, string subject, string bodyText, CancellationToken cancellationToken = default);
}
