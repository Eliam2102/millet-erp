using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Vales;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Tambores;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Tambores;

/// <summary>
/// M-172 (AC-030): el silicón y el Ködispace marcados «Se consume por tambor a línea» salen del inventario por tambor a
/// una línea de producción; el tambor anterior del mismo artículo en la misma línea queda gastado.
/// </summary>
public sealed class TamboresEnLineaTests
{
    private static readonly Guid Insulado1 = Guid.NewGuid();
    private static readonly Guid Insulado2 = Guid.NewGuid();

    private static async Task<P1Fixture> FixtureAsync()
    {
        var f = new P1Fixture();
        var sucursal = (await f.Db.Almacenes.SingleAsync()).SucursalId;
        f.Db.LineasProduccion.Add(new LineaProduccion(Insulado1, sucursal, "INS1", "Insulado 1", "INSULADO"));
        f.Db.LineasProduccion.Add(new LineaProduccion(Insulado2, sucursal, "INS2", "Insulado 2", "INSULADO"));
        await f.Db.SaveChangesAsync();
        return f;
    }

    /// <summary>Silicón A marcado; 200 kg por tambor (dato provisional).</summary>
    private sealed class Silicon(bool marcado = true) : IArticuloReadPort
    {
        private ArticuloLectura L(Guid id) => new(id, "SIL-A", "Silicón componente A", "PZA", null, true,
            ConsumoPorTamborALinea: marcado, KgPorPresentacion: 200m);
        public Task<ArticuloLectura?> ObtenerAsync(Guid id, CancellationToken ct) => Task.FromResult<ArticuloLectura?>(L(id));
        public Task<IReadOnlyDictionary<Guid, ArticuloLectura>> ObtenerPorIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, ArticuloLectura>>(ids.ToDictionary(i => i, L));
    }

    private static Task<RegistrarSalidaResponse> Vale(P1Fixture f, decimal cantidad, Guid? linea, IArticuloReadPort? articulos = null) =>
        new RegistrarSalidaPorValeHandler(f.Db, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true),
                new P1Fixture.Calendario(), new P1Fixture.Centros(), new P7Support.Conversion(), P7Support.Apartados(f.Db),
                articulos ?? new Silicon())
            .Handle(new(P1Fixture.Fecha, "vale.pdf", Guid.NewGuid(), null,
                [new(f.ArticuloId, null, cantidad, null, null, null, null, f.BinId, LineaProduccionId: linea)],
                "Cambio de tambor", "Operador de insulado"), default);

    private static async Task<RegistrarSalidaResponse> ConRq(P1Fixture f, decimal cantidad, Guid? linea)
    {
        var sucursal = (await f.Db.Almacenes.SingleAsync()).SucursalId;
        var rq = (await f.Rq.ObtenerAsync(f.DocumentoId, default))! with { SucursalId = sucursal };
        return await new RegistrarSalidaConRequisicionHandler(f.Db, new P1Fixture.RqPort(rq), f.Events, f.Context, f.Context, f.Guard,
                new PeriodoContableStub(true), new P7Support.Conversion(), P7Support.Apartados(f.Db), new Silicon())
            .Handle(new(f.DocumentoId, P1Fixture.Fecha, Guid.NewGuid(), null,
                [new(f.ArticuloId, f.LineaId, cantidad, null, null, null, null, f.BinId, LineaProduccionId: linea)], "Operador de insulado"), default);
    }

    private static Task<List<TamborEnLinea>> Tambores(P1Fixture f, Guid linea) =>
        f.Db.TamboresLinea.AsNoTracking().Where(t => t.LineaProduccionId == linea).OrderBy(t => t.Id).ToListAsync();

    [Fact]
    public async Task Criterio1_salida_de_un_tambor_descuenta_una_pieza_al_promedio_y_queda_activo()
    {
        await using var f = await FixtureAsync();
        var r = await Vale(f, 1, Insulado1);

        var linea = (await f.Db.Movimientos.Include(m => m.Lineas).SingleAsync()).Lineas.Single();
        linea.Cantidad.Should().Be(1m);
        linea.CostoUnitarioMxn.Should().Be(25m); // costo promedio del saldo
        var tambor = (await Tambores(f, Insulado1)).Single();
        tambor.Estado.Should().Be(TamborEnLinea.Activo);
        tambor.Folio.Should().Be(r.Folio);
        tambor.KgPresentacion.Should().Be(200m);
        tambor.FechaFin.Should().BeNull();
    }

    [Fact]
    public async Task Criterio1_salida_con_requisicion_tambien_registra_el_tambor()
    {
        await using var f = await FixtureAsync();
        var r = await ConRq(f, 1, Insulado1);
        (await Tambores(f, Insulado1)).Single().Folio.Should().Be(r.Folio);
    }

    [Fact]
    public async Task Criterio2_el_segundo_tambor_gasta_el_anterior_con_fecha_y_folio_del_que_lo_reemplazo()
    {
        await using var f = await FixtureAsync();
        var primero = await Vale(f, 1, Insulado1);
        var segundo = await Vale(f, 1, Insulado1);

        var tambores = await Tambores(f, Insulado1);
        tambores.Should().HaveCount(2);
        tambores[0].Folio.Should().Be(primero.Folio);
        tambores[0].Estado.Should().Be(TamborEnLinea.Gastado);
        tambores[0].FechaFin.Should().NotBeNull();
        tambores[0].FolioReemplazo.Should().Be(segundo.Folio);
        tambores[1].Estado.Should().Be(TamborEnLinea.Activo);
    }

    [Fact]
    public async Task Criterio3_surtir_a_otra_linea_no_toca_el_tambor_activo_de_la_primera()
    {
        await using var f = await FixtureAsync();
        await Vale(f, 1, Insulado1);
        await Vale(f, 1, Insulado2);

        (await Tambores(f, Insulado1)).Single().Estado.Should().Be(TamborEnLinea.Activo);
        (await Tambores(f, Insulado2)).Single().Estado.Should().Be(TamborEnLinea.Activo);
    }

    [Fact]
    public async Task Dos_tambores_juntos_a_la_misma_linea_el_primero_queda_gastado_en_el_mismo_instante()
    {
        await using var f = await FixtureAsync();
        var r = await Vale(f, 2, Insulado1);

        var tambores = await Tambores(f, Insulado1);
        tambores.Should().HaveCount(2);
        tambores.Count(t => t.Estado == TamborEnLinea.Activo).Should().Be(1);
        var gastado = tambores.Single(t => t.Estado == TamborEnLinea.Gastado);
        gastado.FechaFin.Should().Be(gastado.FechaInicio);
        gastado.FolioReemplazo.Should().Be(r.Folio);
    }

    [Theory]
    [InlineData(1.0, false, "SALIDA_TAMBOR_SIN_LINEA")]
    [InlineData(1.5, true, "SALIDA_TAMBOR_PIEZAS_ENTERAS")]
    public async Task Criterio5_sin_linea_o_con_piezas_fraccionadas_se_rechaza_sin_mover_saldo(double cantidad, bool conLinea, string codigo)
    {
        await using var f = await FixtureAsync();
        var vale = await Assert.ThrowsAsync<BusinessRuleException>(() => Vale(f, (decimal)cantidad, conLinea ? Insulado1 : null));
        var rq = await Assert.ThrowsAsync<BusinessRuleException>(() => ConRq(f, (decimal)cantidad, conLinea ? Insulado1 : null));

        vale.Code.Should().Be(codigo);
        rq.Code.Should().Be(codigo);
        (await f.Db.Movimientos.CountAsync()).Should().Be(0);
        (await f.Db.TamboresLinea.CountAsync()).Should().Be(0);
        f.Events.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Linea_de_otra_sucursal_se_rechaza()
    {
        await using var f = await FixtureAsync();
        var ajena = Guid.NewGuid();
        f.Db.LineasProduccion.Add(new LineaProduccion(ajena, Guid.NewGuid(), "INS1", "Insulado 1", "INSULADO"));
        await f.Db.SaveChangesAsync();

        (await Assert.ThrowsAsync<BusinessRuleException>(() => Vale(f, 1, ajena))).Code.Should().Be("SALIDA_TAMBOR_LINEA_OTRA_SUCURSAL");
    }

    [Fact]
    public async Task Articulo_sin_marca_sale_como_siempre_sin_linea_ni_tambor()
    {
        await using var f = await FixtureAsync();
        await Vale(f, 1.5m, null, new Silicon(marcado: false));
        (await f.Db.Movimientos.CountAsync()).Should().Be(1);
        (await f.Db.TamboresLinea.CountAsync()).Should().Be(0);
    }
}
