using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Administracion.Application.Series;
using Millet.Facturacion.Application.EventListeners;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests;

// PostgreSQL desechable configurado por TestAssemblyInit; PAC y reserva simulados.
// No crea ni modifica catálogos compartidos.
public sealed class ReppPendientesTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");
    private static readonly Guid Cliente = Guid.Parse("40000000-0000-0000-0000-000000000011");
    private const string Base = "/api/v1/facturacion/repp/pendientes";
    private readonly WebApplicationFactory<Program> _factory;
    public ReppPendientesTests(WebApplicationFactory<Program> factory) => _factory = factory.WithWebHostBuilder(builder =>
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ICfdiTimbradoPort>(); services.AddSingleton<ICfdiTimbradoPort, Pac>();
            services.RemoveAll<ICfdiRepositorioPort>(); services.AddSingleton<ICfdiRepositorioPort, Repositorio>();
            services.RemoveAll<IPeriodoContablePort>(); services.AddSingleton<IPeriodoContablePort, Periodo>();
            services.RemoveAll<IReppBancarioReadPort>(); services.AddSingleton<IReppBancarioReadPort, Banco>();
            services.RemoveAll<IClientesReadPort>(); services.AddSingleton<IClientesReadPort, Clientes>();
            services.RemoveAll<IRequestHandler<ReservarFolioCommand, ReservarFolioResponse>>();
            services.AddSingleton<IRequestHandler<ReservarFolioCommand, ReservarFolioResponse>, Folio>();
        }));

    [Fact]
    public async Task Evento_crea_pendiente_sin_recibo_y_emision_es_idempotente_con_outbox()
    {
        var client = await Login();
        var (id, facturaId) = await CrearPendiente();
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            Assert.False(await db.Set<Millet.Facturacion.Domain.Repp.ReciboPagoFactura>().AnyAsync(f => f.FacturaVentaId == facturaId));
            Assert.Equal(1, await db.ReppPendientes.CountAsync(p => p.Id == id));
        }
        var response = await Post(client, $"{Base}/{id}/emitir", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("emitido").GetBoolean());
        var reciboId = body.GetProperty("reciboPagoId").GetGuid();
        var otra = await Post(client, $"{Base}/{id}/emitir", new { });
        Assert.Equal(reciboId, (await otra.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reciboPagoId").GetGuid());
        var detalle = await client.GetFromJsonAsync<JsonElement>($"{Base}/{id}");
        Assert.Equal("Emitido", detalle.GetProperty("estado").GetString());
        using var verificar = _factory.Services.CreateScope();
        using var empresa = verificar.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var dbFinal = verificar.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        // Payload es jsonb: PostgreSQL no admite LIKE sobre jsonb, se compara en memoria.
        var payloadsTimbrado = await dbFinal.OutboxEntries
            .Where(e => e.EventType == "facturacion.recibo-pago.timbrado.v1")
            .Select(e => e.Payload)
            .ToListAsync();
        Assert.Contains(payloadsTimbrado, p => p.Contains(reciboId.ToString()));
        Assert.Equal(1, await dbFinal.Set<Millet.Facturacion.Domain.Repp.ReciboPagoFactura>().CountAsync(f => f.FacturaVentaId == facturaId));
    }

    [Fact]
    public async Task Dos_emisiones_concurrentes_del_mismo_pago_generan_un_solo_recibo()
    {
        var client = await Login(); var (id, facturaId) = await CrearPendiente();
        var respuestas = await Task.WhenAll(Post(client, $"{Base}/{id}/emitir", new { }), Post(client, $"{Base}/{id}/emitir", new { }));
        Assert.All(respuestas, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var primero = await respuestas[0].Content.ReadFromJsonAsync<JsonElement>();
        var segundo = await respuestas[1].Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(primero.GetProperty("reciboPagoId").GetGuid(), segundo.GetProperty("reciboPagoId").GetGuid());
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        Assert.Equal(1, await db.Set<Millet.Facturacion.Domain.Repp.ReciboPagoFactura>().CountAsync(f => f.FacturaVentaId == facturaId));
    }

    [Fact]
    public async Task PUT_con_suma_distinta_retorna_422_y_conserva_monto()
    {
        var client = await Login(); var (id, factura) = await CrearPendiente();
        var r = await client.PutAsJsonAsync($"{Base}/{id}", new { formaPago = "03", facturas = new[] { new { facturaVentaId = factura, importe = 99m } } });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var detalle = await client.GetFromJsonAsync<JsonElement>($"{Base}/{id}");
        Assert.Equal(100m, detalle.GetProperty("monto").GetDecimal());
    }

    [Fact]
    public async Task Lote_aisla_exito_error_y_limita_a_50()
    {
        var client = await Login(); var (correcto, _) = await CrearPendiente(); var (vacio, _) = await CrearPendiente(false);
        var r = await Post(client, $"{Base}/emitir-lote", new { ids = new[] { vacio, correcto } });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var resultados = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(resultados[0].GetProperty("emitido").GetBoolean());
        Assert.True(resultados[1].GetProperty("emitido").GetBoolean());
        var excesivo = await Post(client, $"{Base}/emitir-lote", new { ids = Enumerable.Range(0, 51).Select(_ => Guid.NewGuid()).ToArray() });
        Assert.Equal(HttpStatusCode.BadRequest, excesivo.StatusCode);
    }

    [Theory]
    [InlineData("emitir")]
    [InlineData("descartar")]
    public async Task Escritura_sin_permiso_retorna_403(string accion)
    {
        var client = await Login("test-no-perms");
        var r = await Post(client, $"{Base}/{Guid.NewGuid()}/{accion}", new { motivo = "Prueba" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Emision_exige_idempotency_key()
    {
        var client = await Login(); var (id, _) = await CrearPendiente();
        var r = await client.PostAsJsonAsync($"{Base}/{id}/emitir", new { });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    private async Task<(Guid, Guid)> CrearPendiente(bool relacionar = true)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var f = FacturaVenta.CrearBorrador(Empresa, $"U11-{Guid.NewGuid():N}", 1, Guid.NewGuid(), null, null,
            new DatosFiscalesReceptor("AAA010101AAA", "Cliente ficticio U1.1", "601", "97000", "G03", "MEX", false),
            new DatosFiscalesEmisor("BBB010101BBB", "Emisor ficticio U1.1", "601", "97000"), "PPD", "99", "MXN", null,
            2026, 10, 7, ComportamientoFiscal.MostradorInmediato, null, null, null, false);
        f.AgregarLinea(null, "01010101", "Prueba U1.1", "E48", 1m, 100m, 0m, "01", null, null, null);
        f.RecalcularTotales(); f.MarcarTimbradoEnProceso();
        f.MarcarTimbrado(Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, null);
        db.FacturasVenta.Add(f); await db.SaveChangesAsync();
        var payload = new PagoClienteConfirmadoPayload(Empresa, DateTimeOffset.UtcNow, null, Cliente, Guid.NewGuid(), Guid.NewGuid(),
            100m, "MXN", new(2026, 10, 28), "U11 ficticio", relacionar ? [new(f.Id, 100m)] : []);
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var command = new EmitirReppDesdePagoConfirmadoCommand(Guid.NewGuid(), payload);
        await sender.Send(command); await sender.Send(command);
        return ((await db.ReppPendientes.SingleAsync(p => p.MovimientoBancarioId == payload.MovimientoBancarioId)).Id, f.Id);
    }
    private async Task<HttpClient> Login(string oid = "dev-superadmin")
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/dev/fake-login", new { EntraOid = oid, Email = $"{oid}@dev.local", Nombre = "Prueba U1.1", EmpresaId = (Guid?)null });
        response.EnsureSuccessStatusCode();
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
    private static async Task<HttpResponseMessage> Post(HttpClient client, string path, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString()); return await client.SendAsync(request);
    }
    private sealed class Folio : IRequestHandler<ReservarFolioCommand, ReservarFolioResponse>
    {
        public Task<ReservarFolioResponse> Handle(ReservarFolioCommand request, CancellationToken cancellationToken) => Task.FromResult(new ReservarFolioResponse($"U11-{Guid.NewGuid():N}", 1, ""));
    }
    private sealed class Banco : IReppBancarioReadPort
    {
        public Task<decimal?> TipoCambioAsync(string moneda, DateOnly fecha, CancellationToken cancellationToken) => Task.FromResult<decimal?>(20m);
        public Task<Guid> SucursalEmisoraAsync(CancellationToken cancellationToken) => Task.FromResult(Guid.Parse("40000000-0000-0000-0000-000000000012"));
    }
    private sealed class Periodo : IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken cancellationToken) => Task.FromResult(true);
    }
    private sealed class Clientes : IClientesReadPort
    {
        public Task<ClienteFiscalLectura?> ObtenerAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<ClienteFiscalLectura?>(new(id, "AAA010101AAA", "Cliente ficticio U1.1", "601", "97000", "G03", "03", "PPD", "MXN", false));
        public Task<ClienteFiscalLectura?> ResolverPorReferenciaAsync(string referencia, CancellationToken cancellationToken) => ObtenerAsync(Cliente, cancellationToken);
        public Task<IReadOnlyList<ClienteBusquedaItem>> BuscarAsync(string? rfc, string? nombre, int limit, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ClienteBusquedaItem>>([]);
    }
    private sealed class Pac : ICfdiTimbradoPort
    {
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken cancellationToken) => Task.FromResult(new TimbradoResultado(
            TimbradoEstado.Timbrado, Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, "<prueba-ficticia/>", null, null));
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
    private sealed class Repositorio : ICfdiRepositorioPort
    {
        public Task<Guid> GuardarAsync(CfdiArchivoNuevo a, CancellationToken cancellationToken) => Task.FromResult(Guid.NewGuid());
        public Task<CfdiArchivoLeido?> ObtenerAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<CfdiArchivoLeido?>(null);
        public Task<CfdiArchivoLeido?> ObtenerPorUuidAsync(string uuid, CancellationToken cancellationToken) => Task.FromResult<CfdiArchivoLeido?>(null);
    }
}
