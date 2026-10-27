using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.CuentasPorCobrar.Domain.Ports.Correo;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.Facturacion.Domain.Repp;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.CuentasPorCobrar;

/// <summary>
/// F1-CXC-09 · C7.8: el estado de cuenta no se emite ni se envía mientras
/// el cliente tenga un pago confirmado sin REP (CA7.14). PostgreSQL
/// desechable de TestAssemblyInit; el correo se reemplaza por un falso.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class EstadoCuentaEnvioTests : IAsyncLifetime
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private const string Base = "/api/v1/cuentas-por-cobrar/cartera/estado-cuenta";
    private static readonly byte[] Pdf = "%PDF-1.4 estado de cuenta de prueba"u8.ToArray();
    private readonly WebApplicationFactory<Program> _factory;
    private readonly CorreoFalso _correo = new();

    public EstadoCuentaEnvioTests(WebApplicationFactory<Program> factory) => _factory = factory.WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEstadoCuentaCorreoPort>();
            services.AddSingleton<IEstadoCuentaCorreoPort>(_correo);
        }));

    public Task InitializeAsync() => Task.CompletedTask;
    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Pago_sin_REP_bloquea_emision_y_envio_hasta_resolverse()
    {
        using var client = await Login();
        var cliente = Guid.NewGuid();
        var pendienteId = await SembrarPagoSinRep(cliente);

        var reporte = await client.GetAsync($"{Base}/{cliente}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reporte.StatusCode);
        Assert.Contains("ESTADO_CUENTA_REPP_PENDIENTE", await reporte.Content.ReadAsStringAsync());

        var pendientes = await client.GetFromJsonAsync<JsonElement>($"{Base}/{cliente}/pendientes");
        Assert.False(pendientes.GetProperty("emitible").GetBoolean());
        var lista = pendientes.GetProperty("pendientes");
        Assert.Equal(1, lista.GetArrayLength());
        Assert.Equal(pendienteId, lista[0].GetProperty("id").GetGuid());
        Assert.Equal(250m, lista[0].GetProperty("monto").GetDecimal());
        Assert.Equal("SinRep", lista[0].GetProperty("motivo").GetString());

        var bloqueado = await Enviar(client, cliente);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, bloqueado.StatusCode);
        Assert.Contains("ESTADO_CUENTA_REPP_PENDIENTE", await bloqueado.Content.ReadAsStringAsync());
        Assert.Equal(0, await ContarEnvios(cliente));
        Assert.DoesNotContain(_correo.Destinatarios, d => d.Cliente == cliente.ToString());

        await Descartar(pendienteId);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{Base}/{cliente}")).StatusCode);
        var enviado = await Enviar(client, cliente);
        Assert.Equal(HttpStatusCode.Created, enviado.StatusCode);
        var cuerpo = await enviado.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Enviado", cuerpo.GetProperty("estado").GetString());
        Assert.Equal(1, await ContarEnvios(cliente));
        Assert.Contains(_correo.Destinatarios, d => d.Correo == "cobranza@cliente.test");

        var historial = await client.GetFromJsonAsync<JsonElement>($"{Base}/{cliente}/envios");
        Assert.Equal(1, historial.GetArrayLength());
        Assert.Equal("cobranza@cliente.test", historial[0].GetProperty("destinatario").GetString());
    }

    [Fact]
    public async Task Envio_exige_idempotency_key_y_pdf_valido()
    {
        using var client = await Login();
        var cliente = Guid.NewGuid();

        var sinLlave = await Enviar(client, cliente, conLlave: false);
        Assert.Equal(HttpStatusCode.BadRequest, sinLlave.StatusCode);

        var noPdf = await Enviar(client, cliente, contenido: "hola"u8.ToArray(), tipo: "text/plain");
        Assert.Equal(HttpStatusCode.BadRequest, noPdf.StatusCode);
        Assert.Equal(0, await ContarEnvios(cliente));
    }

    [Fact]
    public async Task Envio_sin_permiso_retorna_403()
    {
        using var client = await Login("test-no-perms");
        var r = await Enviar(client, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    private async Task<Guid> SembrarPagoSinRep(Guid cliente)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var pendiente = new ReppPendiente(Empresa, cliente, Guid.NewGuid(), Guid.NewGuid(), null, 250m, "MXN",
            new DateOnly(2026, 10, 15), "C78 ficticio", []);
        db.ReppPendientes.Add(pendiente);
        await db.SaveChangesAsync();
        return pendiente.Id;
    }

    private async Task Descartar(Guid pendienteId)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var pendiente = await db.ReppPendientes.SingleAsync(p => p.Id == pendienteId);
        pendiente.Descartar("Pago duplicado en prueba C7.8");
        await db.SaveChangesAsync();
    }

    private async Task<int> ContarEnvios(Guid cliente)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<CuentasPorCobrarDbContext>();
        return await db.EstadoCuentaEnvios.CountAsync(e => e.ClienteId == cliente);
    }

    private static async Task<HttpResponseMessage> Enviar(HttpClient client, Guid cliente, bool conLlave = true,
        byte[]? contenido = null, string tipo = "application/pdf")
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent("cobranza@cliente.test"), "destinatario" },
            { new StringContent("Adjunto su estado de cuenta."), "mensaje" },
        };
        var archivo = new ByteArrayContent(contenido ?? Pdf);
        archivo.Headers.ContentType = new MediaTypeHeaderValue(tipo);
        form.Add(archivo, "pdf", "estado-cuenta.pdf");
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Base}/{cliente}/envios") { Content = form };
        if (conLlave) request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return await client.SendAsync(request);
    }

    private async Task<HttpClient> Login(string oid = "dev-superadmin")
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login",
            new { EntraOid = oid, Email = $"{oid}@dev.local", Nombre = "Prueba C7.8", EmpresaId = (Guid?)null });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed class CorreoFalso : IEstadoCuentaCorreoPort
    {
        public ConcurrentBag<(string Cliente, string Correo)> Destinatarios { get; } = [];

        public Task<bool> EnviarAsync(CorreoEstadoCuenta correo, CancellationToken cancellationToken)
        {
            Destinatarios.Add((correo.RazonSocialCliente, correo.Destinatario));
            return Task.FromResult(true);
        }
    }
}
