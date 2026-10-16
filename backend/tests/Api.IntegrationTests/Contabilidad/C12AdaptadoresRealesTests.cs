using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Almacen;
using Millet.Api.IntegrationTests.DatosMaestros.Adjuntos;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compartido.Infrastructure.PublicAdapters;
using Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Infrastructure.Adapters;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using static Millet.Api.IntegrationTests.Contabilidad.ContabTestKit;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>
/// C1.2: adapters reales de periodo y tipo de cambio sobre PostgreSQL. Usa una moneda FIX propia
/// y un ejercicio FIX propio (2096); no toca USD ni el calendario de 2026.
/// </summary>
public sealed class C12AdaptadoresRealesTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Dia = new(2096, 10, 15);

    [Fact]
    public void El_DI_registra_los_adapters_reales_y_no_los_NoOp()
    {
        using var scope = factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        Assert.IsType<TipoCambioReadAdapter>(sp.GetRequiredService<Millet.Almacen.Domain.Ports.ITipoCambioReadPort>());
        Assert.IsType<TipoCambioReadPortAdapter>(sp.GetRequiredService<Millet.CuentasPorPagar.Domain.Ports.Administracion.ITipoCambioReadPort>());
        Assert.IsType<Millet.Facturacion.Infrastructure.Adapters.PeriodoContableAdapter>(
            sp.GetRequiredService<Millet.Facturacion.Domain.Ports.IPeriodoContablePort>());
        Assert.IsType<Millet.Tesoreria.Infrastructure.Adapters.PeriodoContableAdapter>(
            sp.GetRequiredService<Millet.Tesoreria.Domain.Ports.IPeriodoContablePort>());
        Assert.IsType<Millet.Almacen.Infrastructure.PublicAdapters.PeriodoContableReadAdapter>(
            sp.GetRequiredService<Millet.Almacen.Domain.Ports.IPeriodoContableReadPort>());
    }

    [Fact]
    public async Task C12a_Facturacion_y_Tesoreria_no_admiten_septiembre_cerrado()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AlmacenDbContext>();
        await using var calendario = new PeriodoContableFixture(db.Database.GetConnectionString()!, EmpresaBootstrapId, 2096);
        await calendario.SembrarAsync(mesAbierto: 10, cerrarAnteriores: true);

        var facturacion = new Millet.Facturacion.Infrastructure.Adapters.PeriodoContableAdapter(calendario.Consulta);
        var tesoreria = new Millet.Tesoreria.Infrastructure.Adapters.PeriodoContableAdapter(calendario.Consulta);

        Assert.False(await facturacion.EstaAbiertoAsync(2096, 9, default));
        Assert.True(await facturacion.EstaAbiertoAsync(2096, 10, default));
        Assert.False(await facturacion.EstaAbiertoAsync(2096, 11, default));
        Assert.False(await tesoreria.EstaAbiertoAsync(2096, 9, default));
        Assert.True(await tesoreria.EstaAbiertoAsync(2096, 10, default));
    }

    [Fact]
    public async Task Los_adapters_de_TC_leen_el_catalogo_y_sin_TC_devuelven_null()
    {
        var (monedaId, codigo) = await SembrarMonedaAsync(18.25m);
        try
        {
            using var scope = factory.Services.CreateScope();
            var almacen = scope.ServiceProvider.GetRequiredService<Millet.Almacen.Domain.Ports.ITipoCambioReadPort>();
            var cxp = scope.ServiceProvider.GetRequiredService<Millet.CuentasPorPagar.Domain.Ports.Administracion.ITipoCambioReadPort>();

            Assert.Equal(18.25m, await almacen.ObtenerTipoCambioMxnAsync(codigo, Dia, default));
            Assert.Equal(1m, await almacen.ObtenerTipoCambioMxnAsync("MXN", Dia, default));
            Assert.Null(await almacen.ObtenerTipoCambioMxnAsync(codigo, Dia.AddDays(1), default));
            Assert.Equal(18.25m, await cxp.ObtenerAsync(codigo, "MXN", Dia, default));
            Assert.Null(await cxp.ObtenerAsync(codigo, "MXN", Dia.AddDays(-1), default));
        }
        finally { await BorrarMonedaAsync(monedaId); }
    }

    [Fact]
    public async Task C12b_c_CxP_captura_en_moneda_extranjera_usa_el_TC_del_catalogo_y_sin_TC_responde_422()
    {
        var (monedaId, codigo) = await SembrarMonedaAsync(18.25m);
        var oc = new OcMoneda(codigo);
        var ambiente = new AdjuntosProveedorAmbiente(factory, services =>
        {
            services.AddSingleton<IComprasOcReadPort>(oc);
            // Mide el TC, no el candado de periodo: el periodo se da por abierto.
            services.AddScoped<Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort, PeriodoAbierto>();
        });
        var proveedorId = await ambiente.SeedProveedorAsync();
        oc.ProveedorId = proveedorId;
        try
        {
            var admin = await ambiente.SuperAdminAsync();
            CapturarFacturaConOcCommand Comando(DateOnly fecha, decimal? tipoCambio)
            {
                var ocId = Guid.NewGuid();
                var documento = new DateTimeOffset(fecha.ToDateTime(new TimeOnly(12, 0)), TimeSpan.FromHours(-6));
                return new(ocId, proveedorId, oc.SucursalId, null, null, "FIX-C12", null, documento, documento,
                    fecha.AddDays(30), codigo, tipoCambio, 100, 0, 0, 0, 100,
                    [new(oc.ArticuloId, null, "Material FIX C1.2", 1, "H87", "Pieza", 100, 100, null, ocId, null)]);
            }

            // C1.2-b: sin TC capturado ni XML toma el del catálogo para la fecha del documento.
            var conCatalogo = await admin.PostAsJsonAsync("/api/v1/cuentas-por-pagar/facturas", Comando(Dia, null));
            Assert.Equal(HttpStatusCode.Created, conCatalogo.StatusCode);
            Assert.Equal(18.25m, await TipoCambioGuardadoAsync(ambiente, (await Json(conCatalogo)).GetProperty("id").GetGuid()));

            // El TC capturado de la factura manda sobre el catálogo.
            var capturado = await admin.PostAsJsonAsync("/api/v1/cuentas-por-pagar/facturas", Comando(Dia, 18.40m));
            Assert.Equal(HttpStatusCode.Created, capturado.StatusCode);
            Assert.Equal(18.40m, await TipoCambioGuardadoAsync(ambiente, (await Json(capturado)).GetProperty("id").GetGuid()));

            // C1.2-c: sin TC para la fecha no se inventa uno.
            var sinTc = await admin.PostAsJsonAsync("/api/v1/cuentas-por-pagar/facturas", Comando(Dia.AddDays(1), null));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, sinTc.StatusCode);
            var problema = await Json(sinTc);
            Assert.Equal("TIPO_CAMBIO_NO_REGISTRADO", problema.GetProperty("code").GetString());
            Assert.Contains($"{codigo} registrado para 2096-10-16", problema.GetProperty("detail").GetString());
        }
        finally
        {
            using var scope = ambiente.Factory.Services.CreateScope();
            using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
            await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().FacturasProveedor
                .Where(f => f.ProveedorId == proveedorId).ExecuteDeleteAsync();
            await scope.ServiceProvider.GetRequiredService<CompartidoDbContext>().Proveedores
                .Where(p => p.Id == proveedorId).ExecuteDeleteAsync();
            await BorrarMonedaAsync(monedaId);
        }
    }

    private static async Task<decimal?> TipoCambioGuardadoAsync(AdjuntosProveedorAmbiente ambiente, Guid facturaId)
    {
        using var scope = ambiente.Factory.Services.CreateScope();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        return await scope.ServiceProvider.GetRequiredService<CuentasPorPagarDbContext>().FacturasProveedor
            .AsNoTracking().Where(f => f.Id == facturaId).Select(f => f.TipoCambio).SingleAsync();
    }

    /// <summary>Moneda FIX con código libre y su TC solo para <see cref="Dia"/>.</summary>
    private async Task<(Guid Id, string Codigo)> SembrarMonedaAsync(decimal valorEnMxn)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        var existentes = (await db.Monedas.IgnoreQueryFilters().AsNoTracking().Select(m => m.Codigo).ToListAsync()).ToHashSet();
        var codigo = Enumerable.Range(0, 26 * 26).Select(i => $"Q{(char)('A' + i / 26)}{(char)('A' + i % 26)}")
            .First(c => !existentes.Contains(c));
        var moneda = new Moneda(Guid.CreateVersion7(), codigo, $"FIX C1.2 {codigo}");
        db.Monedas.Add(moneda);
        db.TiposCambio.Add(new TipoCambio(Guid.CreateVersion7(), moneda.Id, Dia, valorEnMxn));
        await db.SaveChangesAsync();
        return (moneda.Id, codigo);
    }

    private async Task BorrarMonedaAsync(Guid monedaId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        await db.TiposCambio.Where(t => t.MonedaId == monedaId).ExecuteDeleteAsync();
        await db.Monedas.IgnoreQueryFilters().Where(m => m.Id == monedaId).ExecuteDeleteAsync();
    }

    private sealed class OcMoneda(string moneda) : IComprasOcReadPort
    {
        public Guid SucursalId { get; } = Guid.NewGuid();
        public Guid ArticuloId { get; } = Guid.NewGuid();
        public Guid ProveedorId { get; set; }
        public Task<OrdenCompraDto?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<OrdenCompraDto?>(
            new(id, "OC-FIX-C12", ProveedorId, AdjuntosProveedorAmbiente.EmpresaInicialId, SucursalId, 100, "Autorizada",
                [new(id, ArticuloId, 1, 100, 0, 1)], moneda));
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<IReadOnlyList<OrdenCompraDto>> ListarAutorizadasPorProveedorAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<OrdenCompraDto>>([]);
    }

    private sealed class PeriodoAbierto : Millet.CuentasPorPagar.Domain.Ports.Contabilidad.IPeriodoContablePort
    {
        public Task<bool> AdmiteMovimientosAsync(DateOnly fecha, CancellationToken ct) => Task.FromResult(true);
    }
}
