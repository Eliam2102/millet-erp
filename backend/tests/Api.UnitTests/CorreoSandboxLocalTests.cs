using System.Net;
using System.Net.Mail;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Api.Auth.Provisioning;
using Millet.Identidad.Application.DirectorioEntra;
using Millet.Identidad.Application.Ports;

namespace Millet.Api.UnitTests;

public sealed class CorreoSandboxLocalTests
{
    [Fact]
    public async Task Buzon_No_Disponible_No_Reporta_Envio_Aceptado()
    {
        // Reserva un puerto sin escuchar para provocar rechazo sin depender de Mailpit.
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        var options = new EntraDirectorioOptions();
        options.Simulacion.CorreoSandbox.Host = "127.0.0.1";
        options.Simulacion.CorreoSandbox.Puerto = ((IPEndPoint)socket.LocalEndPoint!).Port;
        var correo = new CorreoSandboxLocal(Options.Create(options),
            new ConfigurationBuilder().Build(), NullLogger<CorreoSandboxLocal>.Instance);

        await Assert.ThrowsAsync<SmtpException>(() => correo.EnviarAccesoColaboradorAsync(
            new CorreoAccesoColaborador("demo@example.test", "Demo", "demo@example.test",
                "clave-simulada", "http://localhost:5173"), CancellationToken.None));
    }
}
