using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Administracion.Domain;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Domain.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Integraciones.Fiscal.Domain.Ports;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Facturacion;

/// <summary>
/// FAC-08 (86e3qdddf): una factura por cada salida parcial sin cambiar el pedido original.
/// HTTP + handlers + PostgreSQL desechable con la reserva de folio real; PAC, repositorio
/// CFDI, período, emisor y maestro de clientes son dobles. Datos ficticios.
/// </summary>
[Collection(ApiCompartida.Nombre)]
public sealed class SalidasParcialesFacturaTests : IClassFixture<SalidasParcialesFacturaTests.Servidor>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const string Pedidos = "/api/v1/facturacion/pedidos-facturables";
    private const string Facturas = "/api/v1/facturacion/facturas";
    private const string Series = "/api/v1/admin/series";
    private static readonly Guid Empresa = Guid.Parse("00000003-0000-0000-0000-000000000001");

    private readonly WebApplicationFactory<Program> _factory;

    public SalidasParcialesFacturaTests(Servidor fixture) => _factory = fixture.Factory;

    public sealed class Servidor : IDisposable
    {
        private readonly WebApplicationFactory<Program> _raiz = new();
        public WebApplicationFactory<Program> Factory { get; }
        public Servidor() => Factory = _raiz.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICfdiTimbradoPort>(); services.AddSingleton<ICfdiTimbradoPort, Pac>();
            services.RemoveAll<ICfdiRepositorioPort>(); services.AddSingleton<ICfdiRepositorioPort, Repositorio>();
            services.RemoveAll<IPeriodoContablePort>(); services.AddSingleton<IPeriodoContablePort, Periodo>();
            services.RemoveAll<IEmpresaFiscalReadPort>(); services.AddSingleton<IEmpresaFiscalReadPort, Emisor>();
            services.RemoveAll<IClientesReadPort>(); services.AddSingleton<IClientesReadPort, Clientes>();
        }));
        public void Dispose() => _raiz.Dispose();
    }

    [Fact]
    public async Task Salida_de_60_y_de_40_dan_dos_facturas_con_folios_distintos_y_el_pedido_sigue_en_100()
    {
        var client = await SuperAdminAsync();
        var sucursal = Guid.NewGuid();
        await CrearSerieAsync(client, sucursal);
        var pedidoId = await CrearPedidoAwAsync(sucursal);

        var s1 = await RegistrarSalidaAsync(client, pedidoId, "SAL-60", 60m, HttpStatusCode.Created);
        var f1 = await client.PostAsJsonAsync(Facturas, EmitirBody(sucursal, pedidoId, s1, 60m));
        Assert.True(f1.StatusCode == HttpStatusCode.Created, await f1.Content.ReadAsStringAsync());
        var s2 = await RegistrarSalidaAsync(client, pedidoId, "SAL-40", 40m, HttpStatusCode.Created);
        var f2 = await client.PostAsJsonAsync(Facturas, EmitirBody(sucursal, pedidoId, s2, 40m));
        Assert.True(f2.StatusCode == HttpStatusCode.Created, await f2.Content.ReadAsStringAsync());

        var folio1 = (await f1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("folio").GetString();
        var folio2 = (await f2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("folio").GetString();
        Assert.NotEqual(folio1, folio2);

        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var pedido = await db.PedidosFacturables.AsNoTracking().Include(p => p.Lineas).SingleAsync(p => p.Id == pedidoId);
            Assert.Equal(EstadoPedidoFacturable.Importado, pedido.Estado);
            Assert.Equal(100m, pedido.Lineas.Single().Cantidad);
            Assert.Equal(1000m, pedido.Total);
            var facturas = await db.FacturasVenta.AsNoTracking().Where(f => f.PedidoFacturableId == pedidoId)
                .OrderBy(f => f.FolioNumero).ToListAsync();
            Assert.Equal([s1, s2], facturas.Select(f => f.SalidaFacturableId!.Value));
            Assert.Equal([600m, 400m], facturas.Select(f => f.Subtotal));
        }

        var avance = await client.GetFromJsonAsync<JsonElement>($"{Pedidos}/{pedidoId}/salidas");
        var posicion = avance.GetProperty("avance")[0];
        Assert.Equal(100m, posicion.GetProperty("pedida").GetDecimal());
        Assert.Equal(100m, posicion.GetProperty("facturada").GetDecimal());
        Assert.Equal(1000m, avance.GetProperty("totalFacturado").GetDecimal());
    }

    [Fact]
    public async Task Reenviar_la_salida_no_crea_otra_ni_una_tercera_factura()
    {
        var client = await SuperAdminAsync();
        var sucursal = Guid.NewGuid();
        await CrearSerieAsync(client, sucursal);
        var pedidoId = await CrearPedidoAwAsync(sucursal);
        var salida = await RegistrarSalidaAsync(client, pedidoId, "SAL-1", 60m, HttpStatusCode.Created);
        var emitida = await client.PostAsJsonAsync(Facturas, EmitirBody(sucursal, pedidoId, salida, 60m));
        Assert.Equal(HttpStatusCode.Created, emitida.StatusCode);

        var reenviada = await RegistrarSalidaAsync(client, pedidoId, "SAL-1", 60m, HttpStatusCode.OK);
        Assert.Equal(salida, reenviada);
        var otraVez = await client.PostAsJsonAsync(Facturas, EmitirBody(sucursal, pedidoId, salida, 60m));
        await AssertProblemAsync(otraVez, HttpStatusCode.UnprocessableEntity, "SALIDA_YA_FACTURADA");
        var cambiada = await client.PostAsJsonAsync($"{Pedidos}/{pedidoId}/salidas", SalidaBody(pedidoId, "SAL-1", 70m));
        await AssertProblemAsync(cambiada, HttpStatusCode.UnprocessableEntity, "SALIDA_REFERENCIA_DISTINTA");

        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        Assert.Equal(1, await db.SalidasFacturables.CountAsync(s => s.PedidoFacturableId == pedidoId));
        Assert.Equal(1, await db.FacturasVenta.CountAsync(f => f.PedidoFacturableId == pedidoId));
        Assert.Equal(600m, (await db.SalidasFacturables.AsNoTracking().SingleAsync(s => s.Id == salida)).Total);
    }

    [Fact]
    public async Task Un_pedido_con_salidas_no_se_factura_completo_ni_la_salida_con_otras_cantidades()
    {
        var client = await SuperAdminAsync();
        var sucursal = Guid.NewGuid();
        await CrearSerieAsync(client, sucursal);
        var pedidoId = await CrearPedidoAwAsync(sucursal);
        var salida = await RegistrarSalidaAsync(client, pedidoId, "SAL-1", 60m, HttpStatusCode.Created);

        var completo = await client.PostAsJsonAsync(Facturas, EmitirBody(sucursal, pedidoId, null, 100m));
        await AssertProblemAsync(completo, HttpStatusCode.UnprocessableEntity, "PEDIDO_CON_SALIDAS");
        var distinta = await client.PostAsJsonAsync(Facturas, EmitirBody(sucursal, pedidoId, salida, 100m));
        await AssertProblemAsync(distinta, HttpStatusCode.UnprocessableEntity, "FAC_SALIDA_LINEAS_DISTINTAS");
        var excede = await client.PostAsJsonAsync($"{Pedidos}/{pedidoId}/salidas", SalidaBody(pedidoId, "SAL-2", 41m));
        await AssertProblemAsync(excede, HttpStatusCode.UnprocessableEntity, "SALIDA_EXCEDE_PEDIDO");
    }

    [Fact]
    public async Task Contenedores_de_CE_dan_una_salida_por_pedido_y_al_cancelar_se_descarta_o_va_a_revision()
    {
        var client = await SuperAdminAsync();
        var sucursal = Guid.NewGuid();
        await CrearSerieAsync(client, sucursal);
        var a = await CrearPedidoAwAsync(sucursal);
        var b = await CrearPedidoAwAsync(sucursal);
        var (c1, c2) = (Guid.NewGuid(), Guid.NewGuid());
        var (n1, n2) = ($"ITCE{Guid.NewGuid():N}"[..14], $"ITCE{Guid.NewGuid():N}"[..14]);

        // Contenedor 1 lleva 60 de A y 100 de B (misma referencia, dos pedidos); contenedor 2 lleva 40 de A.
        await EntregarCeAsync(Embarcado(c1, n1, (a, 60m), (b, 100m)));
        await EntregarCeAsync(Embarcado(c1, n1, (a, 60m), (b, 100m)));  // reentrega
        await EntregarCeAsync(Embarcado(c2, n2, (a, 40m)));

        Guid sa1, sa2;
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            var salidas = await db.SalidasFacturables.AsNoTracking().Where(s => s.PedidoFacturableId == a || s.PedidoFacturableId == b)
                .OrderBy(s => s.PedidoFacturableId == a ? 0 : 1).ThenBy(s => s.NumeroParcialidad).ToListAsync();
            Assert.Equal(3, salidas.Count);
            Assert.All(salidas, s => Assert.Equal(OrigenSalidaFacturable.ContenedorExportacion, s.Origen));
            Assert.Equal([(n1, 600m), (n2, 400m), (n1, 1000m)], salidas.Select(s => (s.Referencia, s.Total)));
            (sa1, sa2) = (salidas[0].Id, salidas[1].Id);
        }

        // Cada contenedor de A se factura por separado.
        var f1 = await client.PostAsJsonAsync(Facturas, EmitirBody(sucursal, a, sa1, 60m));
        Assert.True(f1.StatusCode == HttpStatusCode.Created, await f1.Content.ReadAsStringAsync());

        // Cancelar el contenedor 1 (salida de A facturada) → revisión; cancelar el 2 (sin factura) → descarte.
        await EntregarCeAsync(Cancelado(c1, n1, a, b));
        await EntregarCeAsync(Cancelado(c2, n2, a));
        await EntregarCeAsync(Cancelado(c2, n2, a));  // reentrega

        var descartada = await client.PostAsJsonAsync(Facturas, EmitirBody(sucursal, a, sa2, 40m));
        await AssertProblemAsync(descartada, HttpStatusCode.UnprocessableEntity, "SALIDA_DESCARTADA");

        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
            Assert.False((await db.SalidasFacturables.AsNoTracking().SingleAsync(s => s.Id == sa1)).Descartada);
            Assert.True((await db.SalidasFacturables.AsNoTracking().SingleAsync(s => s.Id == sa2)).Descartada);
            Assert.True((await db.SalidasFacturables.AsNoTracking().SingleAsync(s => s.PedidoFacturableId == b)).Descartada);
            var numeroA = await db.PedidosFacturables.Where(p => p.Id == a).Select(p => p.NumeroPedido).SingleAsync();
            var revision = await db.ExcepcionesImportacion.AsNoTracking().Where(x => x.PedidoRef == numeroA && !x.Resuelto).ToListAsync();
            Assert.Single(revision);
            Assert.Equal(Millet.Facturacion.Domain.Ingesta.MotivoExcepcion.CancelacionSobreFacturado, revision[0].Motivo);
            Assert.Contains(n1, revision[0].Detalle);
        }

        var avance = await client.GetFromJsonAsync<JsonElement>($"{Pedidos}/{a}/salidas");
        Assert.Equal(60m, avance.GetProperty("avance")[0].GetProperty("enSalidas").GetDecimal());
    }

    private static Millet.ComercioExterior.Application.ContenedorEmbarcadoEvent Embarcado(Guid contenedor, string numero, params (Guid Pedido, decimal Cantidad)[] pedidos) =>
        new(Empresa, DateTimeOffset.UtcNow, Guid.NewGuid(), "CE-EXP-FIX", contenedor, numero, "FIX-BL", new DateOnly(2026, 11, 4),
            pedidos.Select(p => new Millet.ComercioExterior.Application.PedidoContenedorEvento(p.Pedido, "",
                [new Millet.ComercioExterior.Application.LineaContenedorEvento(1, p.Cantidad, "H87")])).ToArray());

    private static Millet.ComercioExterior.Application.ContenedorCanceladoEvent Cancelado(Guid contenedor, string numero, params Guid[] pedidos) =>
        new(Empresa, DateTimeOffset.UtcNow, Guid.NewGuid(), "CE-EXP-FIX", contenedor, numero, "FIX naviera canceló",
            pedidos.Select(p => new Millet.ComercioExterior.Application.PedidoContenedorEvento(p, "", [])).ToArray());

    private Task EntregarCeAsync(Millet.SharedKernel.Application.Integration.IntegrationEvent ev)
    {
        Millet.SharedKernel.Application.Integration.ILocalIntegrationEventConsumer consumidor =
            new Millet.Facturacion.Infrastructure.Workers.ComercioExteriorEventListenerWorker(null!,
                _factory.Services.GetRequiredService<IServiceScopeFactory>(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Millet.Facturacion.Infrastructure.Workers.ComercioExteriorEventListenerWorker>.Instance);
        return consumidor.ConsumirLocalAsync(ev.EventType, Guid.NewGuid(), JsonSerializer.Serialize(ev, ev.GetType()), CancellationToken.None);
    }

    // ----- Helpers -----

    private async Task<Guid> CrearPedidoAwAsync(Guid sucursal)
    {
        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<FacturacionDbContext>();
        var p = PedidoFacturable.ImportarDesdeAw(Empresa, $"FIX-F08-{Guid.NewGuid():N}"[..30], sucursal, Guid.NewGuid(),
            "Cliente ficticio FAC-08", 1, ComportamientoFiscal.MostradorInmediato, "MXN", null, null, null, 1, "15");
        p.AgregarLinea(null, "FIX vidrio ficticio", "01010101", "H87", 100, 10, 0, false, tasaIva: .16m);
        p.RecalcularTotal();
        db.PedidosFacturables.Add(p);
        await db.SaveChangesAsync();
        return p.Id;
    }

    // La referencia es única por empresa y origen: se le agrega el pedido para que las pruebas
    // que comparten la base no choquen entre sí.
    private static object SalidaBody(Guid pedidoId, string referencia, decimal cantidad) => new
    {
        referencia = $"{referencia}-{pedidoId:N}",
        lineas = new[] { new { posicion = 1, cantidad } },
        origen = (int)OrigenSalidaFacturable.ProductoTerminado,
    };

    private static async Task<Guid> RegistrarSalidaAsync(HttpClient client, Guid pedidoId, string referencia, decimal cantidad,
        HttpStatusCode esperado)
    {
        var r = await client.PostAsJsonAsync($"{Pedidos}/{pedidoId}/salidas", SalidaBody(pedidoId, referencia, cantidad));
        Assert.True(r.StatusCode == esperado, $"{(int)r.StatusCode}: {await r.Content.ReadAsStringAsync()}");
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private static string Prefijo() => "T" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant();

    private static async Task CrearSerieAsync(HttpClient client, Guid sucursal)
    {
        var r = await client.PostAsJsonAsync(Series, new
        {
            Id = Guid.Empty, EmpresaId = Empresa, SucursalId = (Guid?)sucursal, TipoDocumento = (int)TipoDocumentoSerie.Cfdi,
            Prefijo = Prefijo(), Sufijo = (string?)null, ReinicioPeriodo = 0,
        });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
    }

    private static object EmitirBody(Guid sucursal, Guid pedidoId, Guid? salidaId, decimal cantidad) => new
    {
        sucursalId = sucursal,
        receptorRfc = "AAA010101AAA",
        receptorNombre = "Cliente ficticio FAC-08",
        receptorRegimenFiscal = "601",
        receptorCodigoPostal = "97000",
        receptorUsoCfdi = "G03",
        receptorPais = "MEX",
        rfcEmisor = "BBB010101BBB",
        regimenFiscalEmisor = "601",
        metodoPago = "PUE",
        formaPago = "01",
        moneda = "MXN",
        tipoCambio = (decimal?)null,
        canalVenta = 1,
        comportamientoFiscal = (int)ComportamientoFiscal.MostradorInmediato,
        obraId = (long?)null,
        obraNombre = (string?)null,
        facturaAgrupada = false,
        lineas = new[]
        {
            new
            {
                productoId = (Guid?)null, claveProdServSat = "01010101", descripcion = "FIX vidrio ficticio",
                claveUnidadSat = "H87", cantidad, valorUnitario = 10m, descuento = 0m, objetoImp = "02",
                tasaIvaTraslado = 0.16m, tasaRetencionIva = (decimal?)null, tasaRetencionIsr = (decimal?)null,
            },
        },
        pedidoFacturableId = pedidoId,
        salidaFacturableId = salidaId,
    };

    private static async Task AssertProblemAsync(HttpResponseMessage r, HttpStatusCode status, string code)
    {
        var contenido = await r.Content.ReadAsStringAsync();
        Assert.True(r.StatusCode == status, $"{(int)r.StatusCode}: {contenido}");
        using var doc = JsonDocument.Parse(contenido);
        Assert.Equal(code, doc.RootElement.GetProperty("code").GetString());
    }

    private async Task<HttpClient> SuperAdminAsync()
    {
        var client = _factory.CreateClientWithIdempotency();
        var login = await client.PostAsJsonAsync("/api/dev/fake-login",
            new { EntraOid = SuperAdminOid, Email = "superadmin@dev.local", Nombre = "Super Admin Dev", EmpresaId = (Guid?)null });
        login.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            doc.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private sealed class Periodo : IPeriodoContablePort
    {
        public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class Emisor : IEmpresaFiscalReadPort
    {
        public Task<EmpresaFiscalLectura?> ObtenerAsync(Guid empresaId, CancellationToken ct) =>
            Task.FromResult<EmpresaFiscalLectura?>(new(empresaId, "BBB010101BBB", "FIX emisor FAC-08", "601", 0.16m, "97000"));
        public Task<Guid?> ObtenerSucursalUnicaActivaAsync(CancellationToken ct) => Task.FromResult<Guid?>(null);
    }

    private sealed class Clientes : IClientesReadPort
    {
        public Task<ClienteFiscalLectura?> ObtenerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ClienteFiscalLectura?>(new(id, "AAA010101AAA", "Cliente ficticio FAC-08", "601", "97000", "G03", "01", "PUE", "MXN", false));
        public Task<ClienteFiscalLectura?> ResolverPorReferenciaAsync(string referencia, CancellationToken ct) => Task.FromResult<ClienteFiscalLectura?>(null);
        public Task<IReadOnlyList<ClienteBusquedaItem>> BuscarAsync(string? rfc, string? nombre, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ClienteBusquedaItem>>([]);
    }

    private sealed class Pac : ICfdiTimbradoPort
    {
        public Task<TimbradoResultado> TimbrarAsync(CfdiEmision emision, CancellationToken ct) => Task.FromResult(new TimbradoResultado(
            TimbradoEstado.Timbrado, Guid.NewGuid().ToString(), null, null, null, DateTimeOffset.UtcNow, null, "<prueba-ficticia/>", null, null));
        public Task<CancelacionResultado> CancelarAsync(CancelacionSolicitud s, CancellationToken ct) => throw new NotSupportedException();
        public Task<EstatusCfdiResultado> ConsultarEstatusAsync(EstatusCfdiSolicitud s, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Repositorio : ICfdiRepositorioPort
    {
        public Task<Guid> GuardarAsync(CfdiArchivoNuevo a, CancellationToken ct) => Task.FromResult(Guid.NewGuid());
        public Task<CfdiArchivoLeido?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<CfdiArchivoLeido?>(null);
        public Task<CfdiArchivoLeido?> ObtenerPorUuidAsync(string uuid, CancellationToken ct) => Task.FromResult<CfdiArchivoLeido?>(null);
    }
}
