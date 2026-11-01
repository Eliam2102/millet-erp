using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Millet.Api.Auth.Provisioning;
using Millet.CuentasPorPagar.Domain.Ports.Portal;

namespace Millet.Api.UnitTests;

/// <summary>P8.3: el aviso de pago del portal sale por el mismo puerto de correo; en local cae en el SMTP de captura (Mailpit).</summary>
public sealed class PortalCorreoSandboxTests
{
    private sealed class Ambiente(string nombre) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nombre;
        public string ApplicationName { get; set; } = "Prueba";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IConfiguration Config(int puerto, string host = "127.0.0.1") => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["CuentasPorPagar:PortalProveedores:Proveedor"] = "SandboxLocal",
        ["CuentasPorPagar:PortalProveedores:Host"] = host,
        ["CuentasPorPagar:PortalProveedores:Puerto"] = puerto.ToString(),
    }).Build();

    [Fact]
    public async Task Aviso_de_pago_llega_al_SMTP_de_captura_con_folio_y_monto()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var puerto = ((IPEndPoint)listener.LocalEndpoint).Port;
        var servidor = Task.Run(async () =>
        {
            using var cliente = await listener.AcceptTcpClientAsync();
            using var red = cliente.GetStream();
            using var lector = new StreamReader(red, Encoding.ASCII);
            using var escritor = new StreamWriter(red, Encoding.ASCII) { AutoFlush = true, NewLine = "\r\n" };
            var datos = new StringBuilder();
            await escritor.WriteLineAsync("220 prueba");
            var enDatos = false;
            while (await lector.ReadLineAsync() is { } linea)
            {
                if (enDatos)
                {
                    if (linea == ".") { enDatos = false; await escritor.WriteLineAsync("250 ok"); }
                    else datos.AppendLine(linea);
                }
                else if (linea.StartsWith("DATA", StringComparison.OrdinalIgnoreCase)) { enDatos = true; await escritor.WriteLineAsync("354 sigue"); }
                else if (linea.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase)) { await escritor.WriteLineAsync("221 adios"); break; }
                else await escritor.WriteLineAsync("250 ok");
            }
            return datos.ToString();
        });
        var servicios = new ServiceCollection().AddGraphPortalProveedorCorreo(Config(puerto), new Ambiente(Environments.Development)).BuildServiceProvider();

        await servicios.GetRequiredService<IPortalCorreoPort>()
            .EnviarAvisoPagoAsync("ventas@prov.mx", new AvisoPagoCorreo("A-10", 1234.5m, "MXN", new DateOnly(2026, 11, 2)), CancellationToken.None);

        var correo = await servidor.WaitAsync(TimeSpan.FromSeconds(10));
        listener.Stop();
        Assert.Contains("ventas@prov.mx", correo);
        Assert.Contains("A-10", correo);
        Assert.Contains("1,234.50", correo);
    }

    [Theory]
    [InlineData("Production", "127.0.0.1")]
    [InlineData("Development", "smtp.externo.test")]
    public void Sandbox_solo_en_Development_y_loopback(string ambiente, string host)
    {
        var registrar = () => new ServiceCollection().AddGraphPortalProveedorCorreo(Config(1025, host), new Ambiente(ambiente));

        Assert.Throws<InvalidOperationException>(registrar);
    }
}
