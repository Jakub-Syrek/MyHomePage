using MyHomePage.Abstractions;

namespace MyHomePage.Services;

/// <summary>
/// Default <see cref="IEmailSender"/> implementation that writes the
/// message to the structured log instead of contacting an SMTP server.
/// Suitable for a single-admin deployment where the operator can pull
/// the reset link out of Railway logs without provisioning an email
/// service. Swap for an SMTP/SendGrid implementation when multi-user
/// access is needed.
/// </summary>
public sealed class LogOnlyEmailSender : IEmailSender
{
    private readonly ILogger<LogOnlyEmailSender> _logger;

    public LogOnlyEmailSender(ILogger<LogOnlyEmailSender> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task SendAsync(string toEmail, string subject, string bodyText, CancellationToken cancellationToken = default)
    {
        // Logged at Information so it shows up in Railway's default
        // log view; the body is structured so an operator can pipe the
        // JSON log through `jq` if they want to lift just the link.
        _logger.LogInformation(
            "[EMAIL → {ToEmail}] {Subject}\n{Body}",
            toEmail, subject, bodyText);
        return Task.CompletedTask;
    }
}
