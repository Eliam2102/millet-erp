using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Infrastructure;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Compras.Oc;

// F1-COM-13 (86e3jvu98): análisis de compras, saldos y cumplimiento de proveedores.
public partial class OrdenesCompraEndpointsTests
{
    private const string AnalisisBase = "/api/v1/compras/reportes/analisis-proveedores";

    /// <summary>
    /// Caso DEMO del plano 1.7.1: dos OC de 10 y 5 piezas a $100, recibidas 6 y 5,
    /// de proveedores distintos, y otra OC fuera del periodo. Cada corrida usa un
    /// periodo único en el pasado para no mezclarse con otras pruebas.
    /// </summary>
    private async Task<(DateOnly Dia, Guid ProveedorA, Guid ProveedorB, Guid Oc1, Guid Oc2, HttpClient Client)> SembrarDemoAnalisisAsync()
    {
        var client = await CreateSuperAdminClientAsync();
        var (oc1, _) = await SembrarP2(client, recepcion: false);
        var (oc2, _) = await SembrarP2(client, recepcion: false);
        var (oc3, _) = await SembrarP2(client, recepcion: false);
        var dia = DiaUnico();
        var proveedorB = Guid.NewGuid();

        using var scope = _factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
        await db.OrdenesCompra.Where(o => o.Id == oc1 || o.Id == oc2 || o.Id == oc3)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.FechaDocumento, dia).SetProperty(o => o.FechaEntregaEsperada, dia));
        await db.OrdenesCompra.Where(o => o.Id == oc2).ExecuteUpdateAsync(s => s.SetProperty(o => o.ProveedorId, proveedorB));
        await db.OrdenesCompra.Where(o => o.Id == oc3).ExecuteUpdateAsync(s => s.SetProperty(o => o.FechaDocumento, dia.AddMonths(6)));
        await db.LineasOrdenCompra.Where(l => l.OrdenCompraId == oc1).ExecuteUpdateAsync(s => s.SetProperty(l => l.CantidadRecibida, 6m));
        await db.LineasOrdenCompra.Where(l => l.OrdenCompraId == oc2)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.Cantidad, 5m).SetProperty(l => l.CantidadRecibida, 5m));
        var proveedorA = await db.OrdenesCompra.Where(o => o.Id == oc1).Select(o => o.ProveedorId).SingleAsync();
        return (dia, proveedorA, proveedorB, oc1, oc2, client);
    }

    /// <summary>
    /// Día al azar entre 1800 y 1999 (~73 mil opciones): con --shared-db la BD no se
    /// limpia entre corridas y un rango corto repetía fechas y mezclaba datos.
    /// </summary>
    private static DateOnly DiaUnico() =>
        new DateOnly(1800, 1, 1).AddDays(System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 73_000));

    private static string Periodo(DateOnly dia) => $"desde={dia:yyyy-MM-dd}&hasta={dia:yyyy-MM-dd}";

    private static JsonElement.ArrayEnumerator Filas(JsonElement r) => r.GetProperty("reporte").GetProperty("filas").EnumerateArray();

    [Fact]
    public async Task Com13_Compras_del_periodo_reproduce_el_caso_del_plano()
    {
        var (dia, _, _, _, _, client) = await SembrarDemoAnalisisAsync();

        var r = await ReadJsonAsync(await client.GetAsync($"{AnalisisBase}/compras?{Periodo(dia)}"));

        var filas = Filas(r).ToList();
        Assert.Equal(2, filas.Count); // la tercera OC está fuera del periodo
        var total = r.GetProperty("totalesPorMoneda").EnumerateArray().Single();
        Assert.Equal("MXN", total.GetProperty("moneda").GetString());
        Assert.Equal(1500m, total.GetProperty("ordenado").GetDecimal());
        Assert.Equal(1100m, total.GetProperty("recibido").GetDecimal());
        Assert.Equal(400m, total.GetProperty("pendienteFisico").GetDecimal());
    }

    [Fact]
    public async Task Com13_Monedas_distintas_no_se_suman()
    {
        var (dia, _, _, _, oc2, client) = await SembrarDemoAnalisisAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            await scope.ServiceProvider.GetRequiredService<ComprasDbContext>().OrdenesCompra.Where(o => o.Id == oc2)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Moneda, "USD"));
        }

        var r = await ReadJsonAsync(await client.GetAsync($"{AnalisisBase}/compras?{Periodo(dia)}"));

        var totales = r.GetProperty("totalesPorMoneda").EnumerateArray()
            .ToDictionary(t => t.GetProperty("moneda").GetString()!, t => t.GetProperty("ordenado").GetDecimal());
        Assert.Equal(1000m, totales["MXN"]);
        Assert.Equal(500m, totales["USD"]);
    }

    [Fact]
    public async Task Com13_Saldos_solo_ordenes_abiertas_y_cambiar_proveedor_cambia_el_conjunto()
    {
        var (dia, proveedorA, proveedorB, oc1, oc2, client) = await SembrarDemoAnalisisAsync();

        var todas = await ReadJsonAsync(await client.GetAsync($"{AnalisisBase}/saldos?{Periodo(dia)}"));
        var soloB = await ReadJsonAsync(await client.GetAsync($"{AnalisisBase}/saldos?{Periodo(dia)}&proveedorId={proveedorB}"));

        // Ambas siguen abiertas: sin factura ni pago.
        Assert.Equal(new[] { oc1, oc2 }.OrderBy(x => x), Filas(todas).Select(f => f.GetProperty("ocId").GetGuid()).OrderBy(x => x));
        var fila1 = Filas(todas).Single(f => f.GetProperty("ocId").GetGuid() == oc1);
        Assert.Equal(400m, fila1.GetProperty("porRecibir").GetDecimal());
        Assert.Equal(1000m, fila1.GetProperty("porFacturar").GetDecimal());
        Assert.Equal(proveedorA, fila1.GetProperty("proveedorId").GetGuid());
        Assert.Equal(oc2, Filas(soloB).Single().GetProperty("ocId").GetGuid());
    }

    [Fact]
    public async Task Com13_Cumplimiento_por_proveedor_cuadra_con_el_detalle()
    {
        var (dia, proveedorA, proveedorB, oc1, _, client) = await SembrarDemoAnalisisAsync();

        var r = await ReadJsonAsync(await client.GetAsync($"{AnalisisBase}/cumplimiento?{Periodo(dia)}"));

        var filas = Filas(r).ToDictionary(f => f.GetProperty("proveedorId").GetGuid());
        Assert.Equal(2, filas.Count);
        // A: 6 de 10 con compromiso vencido → evaluable, tarde e incompleta.
        Assert.Equal(1, filas[proveedorA].GetProperty("lineasEvaluables").GetInt32());
        Assert.Equal(0m, filas[proveedorA].GetProperty("porcentajeATiempo").GetDecimal());
        Assert.Equal(0m, filas[proveedorA].GetProperty("porcentajeCompleto").GetDecimal());
        // B: completa sin fecha de Almacén → no se evalúa a tiempo (n/d), sí completa.
        Assert.Equal(JsonValueKind.Null, filas[proveedorB].GetProperty("porcentajeATiempo").ValueKind);
        Assert.Equal(100m, filas[proveedorB].GetProperty("porcentajeCompleto").GetDecimal());
        Assert.Equal("—", filas[proveedorB].GetProperty("diferenciaPrecio").GetString());
        Assert.NotEmpty(r.GetProperty("formulas").EnumerateArray());

        var detalle = await ReadJsonAsync(await client.GetAsync(
            $"{AnalisisBase}/ordenes?{Periodo(dia)}&proveedorId={proveedorA}&indicador=a-tiempo"));
        var orden = detalle.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(oc1, orden.GetProperty("ocId").GetGuid());
        var linea = orden.GetProperty("lineas").EnumerateArray().Single();
        Assert.True(linea.GetProperty("evaluable").GetBoolean());
        Assert.False(linea.GetProperty("aTiempo").GetBoolean());
        Assert.Equal("oc-esperada", linea.GetProperty("origenCompromiso").GetString());
        Assert.True(orden.GetProperty("pendientes").GetProperty("recepcion").GetBoolean());
    }

    [Fact]
    public async Task Com13_Saldos_coincide_con_partidas_abiertas()
    {
        var (dia, _, _, _, _, client) = await SembrarDemoAnalisisAsync();

        var saldos = await ReadJsonAsync(await client.GetAsync($"{AnalisisBase}/saldos?{Periodo(dia)}"));
        var partidas = await ReadJsonAsync(await client.GetAsync(
            $"{EndpointBase}/partidas-abiertas?fechaDesde={dia:yyyy-MM-dd}&fechaHasta={dia:yyyy-MM-dd}&pageSize=500"));

        Assert.Equal(
            partidas.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).OrderBy(x => x),
            Filas(saldos).Select(f => f.GetProperty("ocId").GetGuid()).OrderBy(x => x));
    }

    [Fact]
    public async Task Com13_Total_de_ordenes_por_articulo_no_cuenta_dos_veces_la_misma_oc()
    {
        var client = await CreateSuperAdminClientAsync();
        var (ocId, _) = await SembrarP2(client, recepcion: false, borrador: true);
        var dia = DiaUnico();
        using (var scope = _factory.Services.CreateScope())
        {
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            var db = scope.ServiceProvider.GetRequiredService<ComprasDbContext>();
            var oc = await db.OrdenesCompra.Include(o => o.Lineas).SingleAsync(o => o.Id == ocId);
            oc.AgregarLineaDesdeRequisicion(Guid.NewGuid(), Guid.NewGuid(), 2, "PZA", 50, oc.Lineas.Single().DepartamentoSolicitanteId,
                Guid.NewGuid(), Guid.NewGuid(), centroCostoId: CentroCostoSeedId);
            oc.EnviarAAutorizacion(DateTimeOffset.UtcNow);
            oc.Autorizar(Guid.NewGuid(), Millet.Compras.Domain.NivelAutorizacion.Nivel1, Guid.NewGuid(), DateTimeOffset.UtcNow);
            oc.Autorizar(Guid.NewGuid(), Millet.Compras.Domain.NivelAutorizacion.Nivel2, Guid.NewGuid(), DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
            await db.OrdenesCompra.Where(o => o.Id == ocId).ExecuteUpdateAsync(s => s.SetProperty(o => o.FechaDocumento, dia));
        }

        var r = await ReadJsonAsync(await client.GetAsync($"{AnalisisBase}/compras?{Periodo(dia)}&agruparPor=articulo"));

        Assert.Equal(2, Filas(r).Count());
        var total = r.GetProperty("totalesPorMoneda").EnumerateArray().Single();
        Assert.Equal(1, total.GetProperty("ordenes").GetInt32());
        Assert.Equal(1100m, total.GetProperty("ordenado").GetDecimal());
    }

    [Fact]
    public async Task Com13_Proveedor_sin_compras_en_el_periodo_no_aparece()
    {
        var (dia, _, _, _, _, client) = await SembrarDemoAnalisisAsync();
        var r = await ReadJsonAsync(await client.GetAsync(
            $"{AnalisisBase}/cumplimiento?{Periodo(dia)}&proveedorId={Guid.NewGuid()}"));
        Assert.Empty(Filas(r));
    }

    [Fact]
    public async Task Com13_Alcance_por_sucursal_y_permisos()
    {
        var (dia, _, _, _, _, _) = await SembrarDemoAnalisisAsync();
        var sinSucursal = await CreateClientConPermisosAsync(PermisosCanonicos.ComprasOrdenesReportesAnalisisProveedores);
        var corporativo = await CreateClientConPermisosAsync(PermisosCanonicos.ComprasOrdenesReportesAnalisisProveedores,
            PermisosCanonicos.ComprasOrdenesLeerTodasSucursales);
        var sinPermiso = await CreateClientConPermisosAsync(PermisosCanonicos.ComprasOrdenesLeer);
        try
        {
            // El helper asocia al usuario a la sucursal de las OC; sin esa asociación no ve nada.
            var conSucursal = await ReadJsonAsync(await sinSucursal.GetAsync($"{AnalisisBase}/compras?{Periodo(dia)}"));
            Assert.Equal(2, Filas(conSucursal).Count());
            var sinSucursalId = await UsuarioIdDeAsync(sinSucursal);
            using (var scope = _factory.Services.CreateScope())
            {
                using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
                await scope.ServiceProvider.GetRequiredService<Millet.Identidad.Infrastructure.IdentidadDbContext>().UsuarioSucursales
                    .Where(x => x.UsuarioId == sinSucursalId).ExecuteDeleteAsync();
            }
            var operativo = await ReadJsonAsync(await sinSucursal.GetAsync($"{AnalisisBase}/compras?{Periodo(dia)}"));
            Assert.Empty(Filas(operativo));
            var drill = await ReadJsonAsync(await sinSucursal.GetAsync($"{AnalisisBase}/ordenes?{Periodo(dia)}"));
            Assert.Empty(drill.GetProperty("items").EnumerateArray());

            var todas = await ReadJsonAsync(await corporativo.GetAsync($"{AnalisisBase}/compras?{Periodo(dia)}"));
            Assert.Equal(2, Filas(todas).Count());

            Assert.Equal(HttpStatusCode.Forbidden, (await sinPermiso.GetAsync($"{AnalisisBase}/compras?{Periodo(dia)}")).StatusCode);
        }
        finally
        {
            await LimpiarUsuarioAsync(await UsuarioIdDeAsync(sinSucursal));
            await LimpiarUsuarioAsync(await UsuarioIdDeAsync(corporativo));
            await LimpiarUsuarioAsync(await UsuarioIdDeAsync(sinPermiso));
        }
    }

    [Theory]
    [InlineData("desde=2026-10-01&hasta=2026-09-01")]
    [InlineData("desde=2024-01-01&hasta=2026-01-01")]
    [InlineData("hasta=2026-01-01")]
    public async Task Com13_Periodo_invalido_400(string query)
    {
        var client = await CreateSuperAdminClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"{AnalisisBase}/cumplimiento?{query}")).StatusCode);
    }
}
