using Millet.CuentasPorPagar.Infrastructure.Mailbox;

namespace Millet.CuentasPorPagar.UnitTests.Mailbox;

public sealed class MailboxOptionsTests
{
    [Fact]
    public void IsConfigured_false_si_falta_cualquier_credencial()
    {
        new MailboxOptions().IsConfigured.Should().BeFalse();
        new MailboxOptions { TenantId = "t" }.IsConfigured.Should().BeFalse();
        new MailboxOptions { TenantId = "t", ClientId = "c" }.IsConfigured.Should().BeFalse();
        new MailboxOptions { TenantId = "t", ClientId = "c", ClientSecret = "s" }.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_true_con_las_4_credenciales_pobladas()
    {
        var opts = new MailboxOptions
        {
            TenantId = "tenant-guid",
            ClientId = "client-guid",
            ClientSecret = "secret",
            MailboxUpn = "cfdi@millet.com.mx",
        };
        opts.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void Defaults_razonables()
    {
        var opts = new MailboxOptions();
        opts.InboxFolderName.Should().Be("Inbox");
        opts.ProcessedFolderName.Should().Be("Processed");
        opts.FailedFolderName.Should().Be("Failed");
        opts.MaxMensajesPorTick.Should().Be(25);
    }
}
