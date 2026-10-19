using Millet.Almacen.Application.Cierre;
using Millet.Almacen.Application.Conteos;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.UnitTests.TestSupport;

namespace Millet.Almacen.UnitTests.Periodos;

public sealed class RevisionPreviaCierreTests
{
    [Fact]
    public async Task Mes_limpio_y_concluido_puede_cerrarse()
    {
        await using var f = new P1Fixture();
        var mes = FechaNegocio.Hoy(DateTimeOffset.UtcNow).AddMonths(-3);

        var r = await new ObtenerRevisionPreviaCierreHandler(f.Db, f.Context)
            .Handle(new(mes.Year, mes.Month), default);

        r.PuedeCerrar.Should().BeTrue();
        r.Checks.Should().HaveCount(3).And.OnlyContain(c => c.Ok);
    }

    [Fact]
    public async Task Vale_pendiente_y_conteo_sin_aplicar_bloquean_con_los_codigos_del_cierre()
    {
        await using var f = new P1Fixture();
        await f.MovimientoAsync(TipoMovimiento.SalidaPorVale);
        f.Db.Set<ConteoInventario>().Add(new ConteoInventario(
            Guid.NewGuid(), P1Fixture.EmpresaId, TipoConteo.Rotativo, P1Fixture.Fecha, Guid.NewGuid()));
        await f.Db.SaveChangesAsync();

        var r = await new ObtenerRevisionPreviaCierreHandler(f.Db, f.Context)
            .Handle(new(P1Fixture.Fecha.Year, P1Fixture.Fecha.Month), default);

        r.PuedeCerrar.Should().BeFalse();
        r.Checks.Where(c => !c.Ok).Select(c => c.Codigo).Should().BeEquivalentTo(
            "CIERRE_CONTEOS_PENDIENTES", "CIERRE_VALES_PENDIENTES");
    }

    [Fact]
    public async Task Periodo_cerrado_aparece_en_la_lista_y_no_puede_cerrarse_de_nuevo()
    {
        await using var f = new P1Fixture();
        var mes = FechaNegocio.Hoy(DateTimeOffset.UtcNow).AddMonths(-1);
        await new EjecutarCierreMensualHandler(f.Db, f.Context, f.Context).Handle(new(mes.Year, mes.Month), default);

        var periodos = await new ListarPeriodosCierreHandler(f.Db).Handle(new(), default);
        var revision = await new ObtenerRevisionPreviaCierreHandler(f.Db, f.Context)
            .Handle(new(mes.Year, mes.Month), default);

        periodos.Should().HaveCount(12);
        periodos[0].Should().Match<PeriodoCierreItem>(p => p.Anio == mes.Year && p.Mes == mes.Month && p.Cerrado);
        revision.YaCerrado.Should().BeTrue();
        revision.PuedeCerrar.Should().BeFalse();
    }

    [Fact]
    public async Task Busqueda_por_folio_de_salida_y_por_familia_de_conteo()
    {
        await using var f = new P1Fixture();
        var salida = await f.MovimientoAsync(TipoMovimiento.SalidaPorVale);
        var folio = salida.Folio!;
        var salidas = new ListarSalidasHandler(f.Db, new RqVacio());

        (await salidas.Handle(new(null, null, null, null, null, null, null, null, 0, 50, Q: folio[^4..].ToLowerInvariant()), default))
            .Items.Should().ContainSingle();
        (await salidas.Handle(new(null, null, null, null, null, null, null, null, 0, 50, Q: "NOEXISTE"), default))
            .Items.Should().BeEmpty();

        f.Db.Set<ConteoInventario>().Add(new ConteoInventario(
            Guid.NewGuid(), P1Fixture.EmpresaId, TipoConteo.Rotativo, P1Fixture.Fecha, Guid.NewGuid(), filtroFamilia: "Sellantes"));
        await f.Db.SaveChangesAsync();
        var conteos = new ListarConteosHandler(f.Db);
        (await conteos.Handle(new(null, null, null, 0, 50, "sellan"), default)).Items.Should().ContainSingle();
        (await conteos.Handle(new(null, null, null, 0, 50, "pintura"), default)).Items.Should().BeEmpty();
    }

    private sealed class RqVacio : Millet.Almacen.Domain.Ports.IComprasRequisicionReadPort
    {
        public Task<Millet.Almacen.Domain.Ports.RequisicionLectura?> ObtenerAsync(Guid rqId, CancellationToken ct) =>
            Task.FromResult<Millet.Almacen.Domain.Ports.RequisicionLectura?>(null);
        public Task<IReadOnlyDictionary<Guid, string>> ObtenerFoliosAsync(IReadOnlyCollection<Guid> rqIds, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }
}
