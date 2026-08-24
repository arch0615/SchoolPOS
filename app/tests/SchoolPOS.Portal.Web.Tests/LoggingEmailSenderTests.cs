using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolPOS.Domain.Abstractions;
using SchoolPOS.Portal.Web.Infrastructure.Email;

namespace SchoolPOS.Portal.Web.Tests;

public sealed class LoggingEmailSenderTests
{
    [Fact]
    public async Task SendAsync_with_attachments_does_not_throw_and_completes()
    {
        var sender = new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance);
        var attachments = new List<EmailAttachment>
        {
            new("CFDI-abc.xml", new byte[] { 1, 2, 3 }, "application/xml"),
            new("CFDI-abc.pdf", new byte[] { 4, 5, 6 }, "application/pdf"),
        };

        var act = async () => await sender.SendAsync("destino@escuela.test", "Asunto", "<p>Cuerpo</p>", attachments);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SendAsync_without_attachments_still_works()
    {
        var sender = new LoggingEmailSender(NullLogger<LoggingEmailSender>.Instance);

        var act = async () => await sender.SendAsync("destino@escuela.test", "Asunto", "<p>Cuerpo</p>");

        await act.Should().NotThrowAsync();
    }
}
