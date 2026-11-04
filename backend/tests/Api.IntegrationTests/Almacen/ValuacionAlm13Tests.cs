using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Millet.Almacen.Application.Cierre;
using Millet.Almacen.Application.EventListeners;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Reportes;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Api.IntegrationTests.Contabilidad;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.UnidadesMedida;
using static Millet.Api.IntegrationTests.Almacen.CuarentenaRecepcionTests;

namespace Millet.Api.IntegrationTests.Almacen;

/// <summary>
/// F1-ALM-13 (86e3jvuez) con Postgres real y los triggers del ledger: el inventario se valúa a costo promedio ponderado
/// (CA4.11), la diferencia de precio de factura no revalúa el inventario (ALM-13-b, ALM-13-c) y el ALFAK reporta el valor
/// que era cierto al corte (<c>Hasta</c>), no el costo de hoy aplicado a la existencia pasada.
/// </summary>
[Collection(PeriodoContableSinParalelo.Nombre)]
public class ValuacionAlm13Tests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateOnly Fecha = new(2027, 4, 12);
    private static readonly Guid OtroUsuario = Guid.NewGuid();

    private static async Task<Guid> RecibirAsync(AlmacenDbContext db, Ctx ctx, decimal cantidad, decimal precio) =>
        (await new RegistrarRecepcionConFacturaHandler(db, new OcPrecio(ctx, precio), new CapturaEventos(), new UserFake(ctx.UserId),
            new EmpresaFake(ctx.EmpresaId), new DecimalesNulo(), ctx.Periodos, new ConversionIdentidad(), new CxpVacia(), new ProveedorRfc())
            .Handle(new RegistrarRecepcionConFacturaCommand(ctx.OcId, Fecha, null, "AD662D33-6934-459C-A128-BDF0393E0062", null,
                [new RegistrarRecepcionLineaInput(ctx.ArticuloId, ctx.LineaOcId, cantidad, null, null, ctx.RackId)]), default)).RecepcionId;

    private static async Task<decimal> CostoPromedioAsync(AlmacenDbContext db, Ctx ctx)
    {
        db.ChangeTracker.Clear();
        return await db.SaldosInventario.AsNoTracking().Where(s => s.UbicacionId == ctx.RackId && s.ArticuloId == ctx.ArticuloId)
            .Select(s => s.CostoPromedioMxn).SingleAsync();
    }

    private static Task<int> MovimientosAsync(AlmacenDbContext db, Ctx ctx)
    {
        db.ChangeTracker.Clear();
        return db.LineasMovimiento.CountAsync(l => l.ArticuloId == ctx.ArticuloId);
    }

    private static async Task SalidaAsync(AlmacenDbContext db, Ctx ctx, decimal cantidad, decimal costo)
    {
        var salida = new MovimientoInventario(Guid.CreateVersion7(), TipoMovimiento.SalidaConsumo, ctx.EmpresaId, Fecha);
        salida.AgregarLinea(new LineaMovimiento(Guid.CreateVersion7(), salida.Id, 1, ctx.ArticuloId, cantidad, "PZA", costo, ubicacionId: ctx.RackId));
        salida.Registrar(FolioMovimiento.Construir(TipoMovimiento.SalidaConsumo, 2027, Random.Shared.Next(900000, 999999)), ctx.UserId);
        db.Movimientos.Add(salida);
        await db.SaveChangesAsync();
    }

    private static DiferenciaPrecioFacturaDetectadaCommand Diferencia(Ctx ctx, Guid eventoId, decimal facturado, decimal oc) =>
        new(eventoId, new(ctx.EmpresaId, DateTimeOffset.UtcNow, Guid.NewGuid(), ctx.OcId, ctx.ArticuloId, 10m, facturado, oc, facturado - oc, (facturado - oc) * 10m));

    private static async Task FecharAsync(AlmacenDbContext db, Guid movimientoId, DateOnly fecha) =>
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE almacen.movimientos_inventario SET fecha_movimiento = {fecha} WHERE id = {movimientoId}");

    private static async Task<AlfakHistorialFila> AlfakAsync(AlmacenDbContext db, Ctx ctx, DateOnly hasta)
    {
        db.ChangeTracker.Clear();
        return (await new AlfakHistorialAlmacenHandler(db, new ArticulosUnidad()).Handle(
            new AlfakHistorialAlmacenQuery(new DateOnly(2027, 1, 1), hasta, ctx.SubDisponibleId, ctx.ArticuloId), default)).Filas.Single();
    }

    [Fact]
    public async Task CA4_11_dos_entradas_a_900_y_1000_dejan_costo_promedio_950()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            await RecibirAsync(db, ctx, 10m, 900m);
            await RecibirAsync(db, ctx, 10m, 1000m);
            (await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId)).Should().Be(20m);
            (await CostoPromedioAsync(db, ctx)).Should().BeApproximately(950m, 0.0001m);
        });
    }

    [Fact]
    public async Task ALM_13_b_la_diferencia_de_precio_no_revalua_lo_que_queda()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            await RecibirAsync(db, ctx, 10m, 100m);
            await SalidaAsync(db, ctx, 4m, 100m);
            var antes = await MovimientosAsync(db, ctx);

            await new DiferenciaPrecioFacturaDetectadaHandler(db, NullLogger<DiferenciaPrecioFacturaDetectadaHandler>.Instance)
                .Handle(Diferencia(ctx, Guid.NewGuid(), 110m, 100m), default);

            (await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId)).Should().Be(6m);
            (await CostoPromedioAsync(db, ctx)).Should().Be(100m);
            (await MovimientosAsync(db, ctx)).Should().Be(antes);
        });
    }

    [Fact]
    public async Task ALM_13_c_el_mismo_evento_dos_veces_no_crea_movimientos_ni_cambia_el_costo()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            await RecibirAsync(db, ctx, 10m, 100m);
            var antes = await MovimientosAsync(db, ctx);
            var handler = new DiferenciaPrecioFacturaDetectadaHandler(db, NullLogger<DiferenciaPrecioFacturaDetectadaHandler>.Instance);
            var comando = Diferencia(ctx, Guid.NewGuid(), 110m, 100m);

            await handler.Handle(comando, default);
            await handler.Handle(comando, default);

            (await MovimientosAsync(db, ctx)).Should().Be(antes);
            (await CostoPromedioAsync(db, ctx)).Should().Be(100m);
            (await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId)).Should().Be(10m);
        });
    }

    [Fact]
    public async Task Corte_pasado_valua_con_el_costo_de_entonces_y_no_con_el_de_hoy()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            var marzo = await RecibirAsync(db, ctx, 10m, 900m);
            await RecibirAsync(db, ctx, 10m, 1000m);   // queda fechada en abril (mes M+1)
            await FecharAsync(db, marzo, new DateOnly(2027, 3, 10));

            var alCierreMarzo = await AlfakAsync(db, ctx, new DateOnly(2027, 3, 31));
            (alCierreMarzo.SaldoFinal, alCierreMarzo.ValorInventarioFinal).Should().Be((10m, 9000m));
            alCierreMarzo.CostoPromedioFinal.Should().BeApproximately(900m, 0.0001m);

            var alCierreAbril = await AlfakAsync(db, ctx, new DateOnly(2027, 4, 30));
            (alCierreAbril.SaldoFinal, alCierreAbril.ValorInventarioFinal).Should().Be((20m, 19000m));
            alCierreAbril.CostoPromedioFinal.Should().BeApproximately(950m, 0.0001m);
        });
    }

    [Fact]
    public async Task Corte_pasado_no_cambia_si_la_recepcion_se_cancela_despues_del_corte()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            var recepcion = await RecibirAsync(db, ctx, 10m, 900m);
            var corte = new DateOnly(2027, 3, 31);
            await FecharAsync(db, recepcion, new DateOnly(2027, 3, 10));
            var antes = await AlfakAsync(db, ctx, corte);
            (antes.SaldoFinal, antes.ValorInventarioFinal).Should().Be((10m, 9000m));

            // Cancelación fechada en abril (reloj fijo): después del corte. Hoy el saldo es 0, el corte de marzo sigue en 9,000.
            // El periodo de la recepción debe estar abierto al cancelar: se re-fecha después y se vuelve a medir.
            await FecharAsync(db, recepcion, Fecha);
            await new CancelarRecepcionHandler(db, new OcPrecio(ctx, 900m), new CapturaEventos(), new UserFake(OtroUsuario),
                new EmpresaFake(ctx.EmpresaId), ctx.Periodos, new ConversionIdentidad(), new CxpVacia(), new RelojFijo())
                .Handle(new CancelarRecepcionCommand(recepcion, "ARTICULO", "Era otro artículo"), default);
            (await LeerSaldoAsync(db, ctx.RackId, ctx.ArticuloId)).Should().Be(0m);
            await FecharAsync(db, recepcion, new DateOnly(2027, 3, 10));

            var despues = await AlfakAsync(db, ctx, corte);
            (despues.SaldoFinal, despues.ValorInventarioFinal).Should().Be((antes.SaldoFinal, antes.ValorInventarioFinal));
            despues.CostoPromedioFinal.Should().Be(antes.CostoPromedioFinal);
        });
    }

    /// <summary>
    /// ALM-13-a con Postgres real: compras a $900 (marzo, mes cerrado con foto) y a $1,000 (abril). El cierre de abril vale 19,000 y
    /// cuadra contra la cuenta de inventario (doble del puerto de Contabilidad con 19,000) y contra el kardex (9,000 + 10 × 1,000).
    /// Un ajuste de valor sin movimiento después del cierre de marzo no cambia la cifra de marzo (foto) y el kardex de abril lo señala.
    /// </summary>
    [Fact]
    public async Task ALM_13_a_el_cierre_cuadra_con_la_cuenta_y_con_el_kardex_y_la_foto_fija_el_mes_cerrado()
    {
        await new CuarentenaRecepcionTests(factory).EjecutarConFixtureAsync(async (db, ctx) =>
        {
            var marzo = await RecibirAsync(db, ctx, 10m, 900m);
            await FecharAsync(db, marzo, new DateOnly(2027, 3, 10));
            var periodo = new PeriodoCerrado(Guid.NewGuid(), ctx.EmpresaId, 2027, 3, ctx.UserId);
            db.PeriodosCerrados.Add(periodo);
            await EjecutarCierreMensualHandler.TomarFotoValuacionAsync(db, periodo, default);
            await db.SaveChangesAsync();
            try
            {
                await RecibirAsync(db, ctx, 10m, 1000m);   // abril

                var cuadre = await CuadreAsync(db, ctx, 19_000m);
                var suc = cuadre.Filas.Single(f => f.Nivel == "Sucursal");
                (suc.Valuacion, suc.SaldoContable, suc.Diferencia, suc.Observacion).Should().Be((19_000m, 19_000m, 0m, "Cuadra"));
                cuadre.Filas.Should().NotContain(f => f.Nivel == "Kardex");

                // Ajuste de valor sin movimiento (p. ej. incrementable de CE) después del cierre de marzo: +500.
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE almacen.saldos_inventario SET costo_promedio_mxn = 975 WHERE ubicacion_id = {ctx.RackId} AND articulo_id = {ctx.ArticuloId}");
                (await AlfakAsync(db, ctx, new DateOnly(2027, 3, 31))).ValorInventarioFinal.Should().Be(9_000m);
                var kardex = (await CuadreAsync(db, ctx, 19_000m)).Filas.Single(f => f.Nivel == "Kardex");
                (kardex.Valuacion, kardex.Diferencia).Should().Be((19_500m, 500m));
            }
            finally
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM almacen.periodos_cerrados WHERE empresa_id = {ctx.EmpresaId}");
            }
        });
    }

    private async Task<ReporteResponse<CuadreInventarioFila>> CuadreAsync(AlmacenDbContext db, Ctx ctx, decimal saldoContable)
    {
        db.ChangeTracker.Clear();
        using var scope = factory.Services.CreateScope();
        return await new CuadreInventarioContableHandler(db, new InventarioDoble(saldoContable),
                scope.ServiceProvider.GetRequiredService<ISucursalReadPort>(), new EmpresaFake(ctx.EmpresaId), new ArticulosUnidad())
            .Handle(new CuadreInventarioContableQuery(2027, 4), default);
    }

    // ─────────────────────────── Dobles ───────────────────────────

    private sealed class InventarioDoble(decimal saldo) : IInventarioContableReadPort
    {
        public Task<IReadOnlyDictionary<Guid, CuentaInventarioSaldo>> ObtenerAsync(DateOnly corte, IReadOnlyCollection<Guid> sucursales, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CuentaInventarioSaldo>>(
                sucursales.ToDictionary(s => s, _ => new CuentaInventarioSaldo(Guid.NewGuid(), "1150", "Inventario", saldo)));
    }

    private sealed class RelojFijo : IClock
    {
        public DateTimeOffset UtcNow => new(Fecha.Year, Fecha.Month, Fecha.Day, 18, 0, 0, TimeSpan.Zero);
    }

    private sealed class OcPrecio(Ctx ctx, decimal precio) : IComprasOcReadPort
    {
        public Task<OcLectura?> ObtenerAsync(Guid ocId, CancellationToken ct) => Task.FromResult<OcLectura?>(
            new(ocId, "OC-ALM13", Guid.NewGuid(), ctx.EmpresaId, "Autorizada",
                [new OcLineaLectura(ctx.LineaOcId, ctx.ArticuloId, "PZA", 100m, 0m, precio)]));
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> ocIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class CxpVacia : ICxpDocumentosReadPort
    {
        public Task<CfdiRecibidoLectura?> ObtenerCfdiAsync(Guid id, CancellationToken ct) => Task.FromResult<CfdiRecibidoLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosFacturaAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Vacio();
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerUuidsFiscalesCfdiAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Vacio();
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosNotaCreditoAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) => Vacio();
        public Task<IReadOnlyList<FacturaLigadaRecepcionLectura>> ObtenerFacturasLigadasRecepcionAsync(
            IReadOnlyCollection<Guid> recepcionIds, IReadOnlyCollection<Guid> facturaIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<FacturaLigadaRecepcionLectura>>([]);
        private static Task<IReadOnlyDictionary<Guid, string>> Vacio() => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

    private sealed class ProveedorRfc : IProveedorReadPort
    {
        public Task<ProveedorLectura?> ObtenerAsync(Guid id, CancellationToken ct) =>
            Task.FromResult<ProveedorLectura?>(new ProveedorLectura(id, "P", "Proveedor", "AAA010101AAA", true));
        public Task<IReadOnlyDictionary<Guid, ProveedorLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ProveedorLectura>>(new Dictionary<Guid, ProveedorLectura>());
    }

    private sealed class ConversionIdentidad : IConversionUnidadPort
    {
        public Task<ConversionUnidad> ConvertirAsync(Guid articuloId, decimal cantidad, string? capturada, string documento, CancellationToken ct)
        {
            var unidad = string.IsNullOrWhiteSpace(documento) ? "PZA" : documento;
            return Task.FromResult(new ConversionUnidad(cantidad, unidad, cantidad, 1, unidad, cantidad));
        }
    }
}
