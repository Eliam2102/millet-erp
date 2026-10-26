using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Ports.Almacen;
using Millet.Compras.Infrastructure;
using Millet.Compras.IntegrationTests.Autorizacion;
using Millet.Compras.IntegrationTests.Fixtures;
using Millet.SharedKernel.Application;

namespace Millet.Compras.IntegrationTests.Reparto;

/// <summary>
/// Reparto de servicios directo a Compras y saldo de compra de RQ (minuta
/// Millet 12-oct, paso 3a). Verifican:
/// <list type="bullet">
///   <item>una línea de artículo Servicio, o una RQ de clasificación Servicio,
///     va completa a compra: no consulta stock ni aparta;</item>
///   <item>el listado de RQ disponibles excluye las RQ cubiertas 100% por
///     almacén y, para una RQ parcial, expone <c>faltantePorComprar</c> y las
///     líneas con saldo;</item>
///   <item>el detalle de RQ trae <c>cantComprometidaEnOc</c> y
///     <c>cantSaldoPorComprar</c> tras crear una OC parcial desde la RQ.</item>
/// </list>
/// </summary>
public class RepartoServicioSaldoRqTests : IClassFixture<StubsWebApplicationFactory>
{
    private const string SuperAdminOid = "dev-superadmin";
    private const int ClasificacionServicio = 0;
    // 86e3tfedd: la RQ de MP sólo acepta categorías de MP; el artículo de stock va en Orden de compra.
    private const int ClasificacionOrdenCompra = 1;

    // Artículo de inventario (stub: OnHand = 100) y artículo Servicio (ART-SVC-01) sembrados.
    private static readonly Guid ArticuloConStockId = Guid.Parse("00000005-0002-0000-0000-000000000001");
    private static readonly Guid ArticuloServicioId = Guid.Parse("00000005-0002-0000-0000-000000000003");
    // Proveedor activo sembrado (PROV-EST).
    private static readonly Guid ProveedorActivoId = Guid.Parse("00000005-0001-0000-0000-000000000001");

    private readonly StubsWebApplicationFactory _factory;

    public RepartoServicioSaldoRqTests(StubsWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Autorizar_LineaDeArticuloServicio_VaCompleta_ACompra_SinConsultarNiApartarStock()
    {
        var stock = new ContadorStock(disponible: 100m);
        await using var factory = WithStock(stock);
        var client = await AutorizacionTestSeeding.CreateSuperAdminClientAsync(factory);
        var rqId = await CrearTransmitirAsync(client, ArticuloServicioId, 10m, ClasificacionServicio);

        await AutorizarAsync(client, rqId);

        Assert.DoesNotContain(ArticuloServicioId, stock.Consultas);
        var linea = await LeerUnicaLineaAsync(rqId);
        Assert.Equal(0m, linea.CantidadDeAlmacen);
        Assert.Equal(10m, linea.CantidadDeCompra);
    }

    [Fact]
    public async Task Autorizar_RqDeClasificacionServicio_VaCompleta_ACompra_SinConsultarStock()
    {
        var stock = new ContadorStock(disponible: 100m);
        await using var factory = WithStock(stock);
        var client = await AutorizacionTestSeeding.CreateSuperAdminClientAsync(factory);
        // 86e3tfedd: ya no se captura un artículo de inventario en una RQ de Servicio;
        // se simula una RQ anterior a la regla cambiando la clasificación en BD.
        var rqId = await CrearTransmitirAsync(client, ArticuloConStockId, 10m, ClasificacionOrdenCompra);
        await ForzarClasificacionAsync(factory, rqId, ClasificacionServicio);

        await AutorizarAsync(client, rqId);

        Assert.DoesNotContain(ArticuloConStockId, stock.Consultas);
        var linea = await LeerUnicaLineaAsync(rqId);
        Assert.Equal(0m, linea.CantidadDeAlmacen);
        Assert.Equal(10m, linea.CantidadDeCompra);
    }

    [Fact]
    public async Task Disponibles_RqCubiertaTotalPorAlmacen_NoAparece()
    {
        var client = await AutorizacionTestSeeding.CreateSuperAdminClientAsync(_factory);
        // Stub: OnHand = 100 → 10 se cubren 100% por almacén, sin saldo de compra.
        var rqId = await CrearTransmitirAsync(client, ArticuloConStockId, 10m, ClasificacionOrdenCompra);
        await AutorizarAsync(client, rqId);

        var disponibles = await ListarDisponiblesAsync(client);

        Assert.DoesNotContain(disponibles.EnumerateArray(),
            d => d.GetProperty("id").GetGuid() == rqId);
    }

    [Fact]
    public async Task Disponibles_RqParcial_MuestraFaltanteYLineaConSaldo()
    {
        var client = await AutorizacionTestSeeding.CreateSuperAdminClientAsync(_factory);
        // Stub: OnHand = 100; 150 → 100 almacén + 50 saldo de compra.
        var rqId = await CrearTransmitirAsync(client, ArticuloConStockId, 150m, ClasificacionOrdenCompra);
        await AutorizarAsync(client, rqId);

        var disponibles = await ListarDisponiblesAsync(client);

        var rq = disponibles.EnumerateArray().Single(d => d.GetProperty("id").GetGuid() == rqId);
        Assert.Equal(50m, rq.GetProperty("faltantePorComprar").GetDecimal());
        var lineas = rq.GetProperty("lineasConSaldo").EnumerateArray().ToList();
        var linea = Assert.Single(lineas);
        Assert.Equal(ArticuloConStockId, linea.GetProperty("articuloId").GetGuid());
        Assert.Equal(50m, linea.GetProperty("cantidadSaldo").GetDecimal());
        Assert.Equal(100m, linea.GetProperty("cantidadDeAlmacen").GetDecimal());
    }

    [Fact]
    public async Task Detalle_TrasOcParcialDesdeRq_TraeComprometidaYSaldo()
    {
        var client = await AutorizacionTestSeeding.CreateSuperAdminClientAsync(_factory);
        var rqId = await CrearTransmitirAsync(client, ArticuloConStockId, 150m, ClasificacionOrdenCompra);
        await AutorizarAsync(client, rqId);

        // OC desde la RQ: hereda el saldo (50) y luego se reduce a 20 (parcial).
        var crear = await client.PostAsJsonAsync("/api/v1/compras/ordenes/desde-requisicion", new
        {
            RequisicionId = rqId,
            SucursalCodigo = TestComprasFixtures.SucursalCodigoMid,
            FolioAnio = TestComprasFixtures.FolioAnioDefault,
            ProveedorId = ProveedorActivoId,
            CondicionesPagoId = Guid.NewGuid(),
            UsoPrincipalId = Guid.NewGuid(), UsoCfdiClave = "G03", MetodoPago = "PPD", FormaPagoClave = "99",
            FechaDocumento = new DateOnly(2026, 10, 15),
        });
        crear.EnsureSuccessStatusCode();
        var ocId = (await AutorizacionTestSeeding.ReadJsonAsync(crear)).GetProperty("ordenCompraId").GetGuid();

        var oc = await AutorizacionTestSeeding.ReadJsonAsync(
            await client.GetAsync($"/api/v1/compras/ordenes/{ocId}"));
        var lineaOcId = oc.GetProperty("lineas")[0].GetProperty("id").GetGuid();
        var parcial = await client.PatchAsJsonAsync(
            $"/api/v1/compras/ordenes/{ocId}/lineas/{lineaOcId}", new { Cantidad = 20m });
        parcial.EnsureSuccessStatusCode();

        var detalle = await AutorizacionTestSeeding.ReadJsonAsync(
            await client.GetAsync($"/api/v1/compras/requisiciones/{rqId}"));
        var linea = detalle.GetProperty("lineas")[0];
        Assert.Equal(20m, linea.GetProperty("cantComprometidaEnOc").GetDecimal());
        Assert.Equal(30m, linea.GetProperty("cantSaldoPorComprar").GetDecimal());
    }

    // ── Helpers ──

    private WebApplicationFactory<Program> WithStock(IConsultarStockPort stock) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IConsultarStockPort>();
                services.AddSingleton(stock);
            });
        });

    private static async Task ForzarClasificacionAsync(WebApplicationFactory<Program> factory, Guid rqId, int clasificacion)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE compras.requisiciones SET clasificacion = {(short)clasificacion} WHERE id = {rqId}");
    }

    private static async Task AutorizarAsync(HttpClient client, Guid rqId)
    {
        var auth = await client.PostAsJsonAsync(
            $"/api/v1/compras/requisiciones/{rqId}/autorizaciones",
            new { Nivel = 1, Notas = (string?)null });
        auth.EnsureSuccessStatusCode();
    }

    private static async Task<JsonElement> ListarDisponiblesAsync(HttpClient client)
    {
        var response = await client.GetAsync(
            $"/api/v1/compras/ordenes/requisiciones-disponibles?sucursalId={TestComprasFixtures.SucursalMid}");
        response.EnsureSuccessStatusCode();
        return await AutorizacionTestSeeding.ReadJsonAsync(response);
    }

    private static async Task<Guid> CrearTransmitirAsync(
        HttpClient client, Guid articuloId, decimal cantidad, int clasificacion)
    {
        var crear = await client.PostAsJsonAsync("/api/v1/compras/requisiciones",
            TestComprasFixtures.BuildCrearRqValidBody(
                descripcion: "Test reparto servicio/saldo", clasificacion: clasificacion));
        crear.EnsureSuccessStatusCode();
        var rqId = (await AutorizacionTestSeeding.ReadJsonAsync(crear)).GetProperty("id").GetGuid();

        var linea = await client.PostAsJsonAsync($"/api/v1/compras/requisiciones/{rqId}/lineas", new
        {
            ArticuloId = articuloId,
            Cantidad = cantidad,
            UnidadMedida = "PZA",
            PrecioEstimadoMonto = 15m,
            PrecioEstimadoMoneda = "MXN",
            CuentaContableId = (Guid?)null,
            CentroCostoId = (Guid?)TestComprasFixtures.CentroCostoMaquinaSeed,
            Proyecto = (string?)null,
            FechaRequerida = (DateOnly?)null,
            Notas = (string?)null,
        });
        linea.EnsureSuccessStatusCode();

        var transmitir = await client.PostAsync($"/api/v1/compras/requisiciones/{rqId}/transmitir", content: null);
        transmitir.EnsureSuccessStatusCode();
        return rqId;
    }

    private async Task<(decimal CantidadDeAlmacen, decimal CantidadDeCompra)> LeerUnicaLineaAsync(Guid rqId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var linea = Assert.Single(await db.LineaRequisiciones.AsNoTracking()
            .Where(l => l.RequisicionId == rqId).ToListAsync());
        return (linea.CantidadDeAlmacen, linea.CantidadDeCompra);
    }

    /// <summary>Stock fijo que registra qué artículos se consultaron.</summary>
    private sealed class ContadorStock : IConsultarStockPort
    {
        private readonly decimal _disponible;

        public ContadorStock(decimal disponible) => _disponible = disponible;

        public List<Guid> Consultas { get; } = new();

        public Task<DisponibilidadStock> ConsultarPorSucursalAsync(
            Guid sucursalId, Guid articuloId, CancellationToken cancellationToken)
        {
            Consultas.Add(articuloId);
            return Task.FromResult(new DisponibilidadStock(OnHand: _disponible, Disponible: _disponible));
        }
    }
}
