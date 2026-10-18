using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.DevolucionesInternas;
using Millet.Almacen.Application.Revision;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.Domain.Revision;
using Millet.Almacen.Domain.Saldos;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.Revision;

public class DictaminarMaterialEnRevisionTests
{
    private static readonly Guid EmpresaId = P1Fixture.EmpresaId;
    private static readonly Guid SucursalId = Guid.NewGuid();
    private static readonly Guid ArticuloId = Guid.NewGuid();
    private static readonly Guid OcId = Guid.NewGuid();
    private static readonly Guid ProveedorId = Guid.NewGuid();
    private static readonly DateOnly Fecha = new(2026, 10, 9);

    private sealed record Ctx(
        InMemoryAlmacenDbContext Db, Guid AlmacenId, Guid RevSubId, Guid RevBinId, Guid RackId, Guid LineaRevisionId);

    // Recepción 10: 8 en R-1 y 2 en REV-01 (movimiento hermano). Los saldos los pone el trigger en Postgres; aquí se siembran.
    private static async Task<Ctx> SembrarAsync()
    {
        var f = new P1Fixture.Contexto();
        var db = new InMemoryAlmacenDbContext(
            new DbContextOptionsBuilder<InMemoryAlmacenDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, f);
        var almacen = new Domain.Catalogo.Almacen(Guid.NewGuid(), "ALM", "Almacén", SucursalId);
        var sub = new SubAlmacen(Guid.NewGuid(), almacen.Id, "INS", "Insumos", TipoSubAlmacen.Insumos);
        var rev = new SubAlmacen(Guid.NewGuid(), almacen.Id, "MAT-REV", "Revisión", TipoSubAlmacen.MaterialEnRevision);
        var rack = new Ubicacion(Guid.NewGuid(), sub.Id, "R-1", "Rack 1");
        var revBin = new Ubicacion(Guid.NewGuid(), rev.Id, "REV-01", "Revisión 1");
        db.Almacenes.Add(almacen);
        db.SubAlmacenes.AddRange(sub, rev);
        db.Ubicaciones.AddRange(rack, revBin);
        db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), rack.Id, ArticuloId));
        db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), revBin.Id, ArticuloId));
        db.SaldosInventario.Add(new SaldoInventario(revBin.Id, rev.Id, ArticuloId, 2, 10));

        var principal = Entrada(1, null, rack.Id, 8m, out _);
        var revision = Entrada(2, principal.Id, revBin.Id, 2m, out var lineaRev);
        db.Movimientos.AddRange(principal, revision);
        await db.SaveChangesAsync();
        return new Ctx(db, almacen.Id, rev.Id, revBin.Id, rack.Id, lineaRev);
    }

    private static MovimientoInventario Entrada(int n, Guid? principalId, Guid binId, decimal cantidad, out Guid lineaId)
    {
        var mov = new MovimientoInventario(Guid.NewGuid(), TipoMovimiento.EntradaCompra, EmpresaId, Fecha);
        lineaId = Guid.NewGuid();
        mov.AgregarLinea(new LineaMovimiento(lineaId, mov.Id, 1, ArticuloId, cantidad, "PZA", 10m, comentarioLinea: principalId is null ? null : "Golpeado", ubicacionId: binId));
        mov.VincularRecepcionVarianteB(OcId, null, "DEMO.pdf");
        mov.Registrar(FolioMovimiento.Construir(TipoMovimiento.EntradaCompra, 2026, n), Guid.NewGuid());
        if (principalId is { } p) mov.VincularRecepcionPrincipal(p);
        return mov;
    }

    private sealed class FakeAdjuntos(params AdjuntoEvidenciaLectura[] items) : IAdjuntosEntidadReadPort
    {
        public Task<IReadOnlyList<AdjuntoEvidenciaLectura>> ListarVigentesAsync(string tipoEntidad, Guid entidadId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AdjuntoEvidenciaLectura>>(tipoEntidad == "linea_revision" ? items : []);
    }

    private static DictaminarMaterialEnRevisionHandler Handler(
        InMemoryAlmacenDbContext db, params AdjuntoEvidenciaLectura[] evidencias)
    {
        var ctx = new P1Fixture.Contexto();
        var oc = new OcLectura(OcId, "OC-1", ProveedorId, EmpresaId, "Autorizada", []);
        return new(db, ctx, ctx, new P1Fixture.Decimales(), new PeriodoContableStub(true),
            new FakeAdjuntos(evidencias), new P1Fixture.OcPort(oc));
    }

    private static async Task Error(Func<Task> act, string code) =>
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(code);

    [Fact]
    public async Task Excede_pendiente()
    {
        var s = await SembrarAsync();
        await Error(() => Handler(s.Db).Handle(
            new(s.LineaRevisionId, ResultadoDictamen.Liberar, 3m, "Ok", Fecha, s.RackId), default), "DICTAMEN_EXCEDE_PENDIENTE");
    }

    [Fact]
    public async Task Destino_en_material_en_revision()
    {
        var s = await SembrarAsync();
        await Error(() => Handler(s.Db).Handle(
            new(s.LineaRevisionId, ResultadoDictamen.Liberar, 1m, "Ok", Fecha, s.RevBinId), default), "DICTAMEN_DESTINO_EN_REVISION");
    }

    [Fact]
    public async Task Destino_en_otro_almacen()
    {
        var s = await SembrarAsync();
        var otro = new Domain.Catalogo.Almacen(Guid.NewGuid(), "ALM2", "Otro", SucursalId);
        var sub = new SubAlmacen(Guid.NewGuid(), otro.Id, "INS2", "Insumos 2", TipoSubAlmacen.Insumos);
        var bin = new Ubicacion(Guid.NewGuid(), sub.Id, "R-9", "Rack 9");
        s.Db.Almacenes.Add(otro);
        s.Db.SubAlmacenes.Add(sub);
        s.Db.Ubicaciones.Add(bin);
        s.Db.AsignacionesArticuloUbicacion.Add(new AsignacionArticuloUbicacion(Guid.NewGuid(), bin.Id, ArticuloId));
        await s.Db.SaveChangesAsync();
        await Error(() => Handler(s.Db).Handle(
            new(s.LineaRevisionId, ResultadoDictamen.Liberar, 1m, "Ok", Fecha, bin.Id), default), "DICTAMEN_DESTINO_OTRO_ALMACEN");
    }

    [Fact]
    public async Task Liberar_crea_salida_tipo_10_entrada_tipo_9_y_dictamen_con_ambos_ids()
    {
        var s = await SembrarAsync();
        var r = await Handler(s.Db).Handle(
            new(s.LineaRevisionId, ResultadoDictamen.Liberar, 1m, "Utilizable", Fecha, s.RackId), default);

        var salida = await s.Db.Movimientos.Include(m => m.Lineas).SingleAsync(m => m.Tipo == TipoMovimiento.SalidaPorLiberacionRevision);
        var entrada = await s.Db.Movimientos.Include(m => m.Lineas).SingleAsync(m => m.Tipo == TipoMovimiento.ReincorporacionTrasRevision);
        salida.Lineas.Single().UbicacionId.Should().Be(s.RevBinId);
        entrada.Lineas.Single().UbicacionId.Should().Be(s.RackId);
        var d = await s.Db.DictamenesRevision.SingleAsync();
        d.MovimientoSalidaRevisionId.Should().Be(salida.Id);
        d.MovimientoEntradaId.Should().Be(entrada.Id);
        r.Folio.Should().Be(entrada.Folio);
    }

    [Fact]
    public async Task Baja_crea_baja_por_dano_en_el_bin_de_revision()
    {
        var s = await SembrarAsync();
        await Handler(s.Db).Handle(new(s.LineaRevisionId, ResultadoDictamen.Baja, 1m, "Rota", Fecha, null), default);

        var baja = await s.Db.Movimientos.Include(m => m.Lineas).SingleAsync(m => m.Tipo == TipoMovimiento.BajaPorDano);
        baja.Lineas.Single().UbicacionId.Should().Be(s.RevBinId);
        (await s.Db.DictamenesRevision.SingleAsync()).MovimientoEntradaId.Should().BeNull();
    }

    // Saldo 8.A ajeno a recepciones: 3 unidades en un segundo bin de MAT-REV, sin línea de recepción en revisión.
    private static async Task<Guid> AgregarSaldo8AAsync(Ctx s, decimal cantidad)
    {
        var bin = new Ubicacion(Guid.NewGuid(), s.RevSubId, "REV-02", "Revisión 2");
        s.Db.Ubicaciones.Add(bin);
        s.Db.SaldosInventario.Add(new SaldoInventario(bin.Id, s.RevSubId, ArticuloId, cantidad, 10));
        await s.Db.SaveChangesAsync();
        return bin.Id;
    }

    private static ReincorporacionTrasRevisionHandler Reincorporador(Ctx s, string unidad = "PZA")
    {
        var ctx = new P1Fixture.Contexto();
        return new(s.Db, ctx, ctx, new P1Fixture.Decimales(), new PeriodoContableStub(true), new P1Fixture.ArticulosConUnidad(unidad));
    }

    private static BajaPorDanoHandler Bajador(Ctx s)
    {
        var ctx = new P1Fixture.Contexto();
        return new(s.Db, ctx, ctx, new P1Fixture.Decimales(), new PeriodoContableStub(true), new P1Fixture.ArticulosConUnidad());
    }

    [Fact]
    public async Task Reincorporar_8A_descuenta_mat_rev_y_suma_al_destino_con_el_mismo_par()
    {
        var s = await SembrarAsync();
        var bin8A = await AgregarSaldo8AAsync(s, 3m);
        var insumosSub = await s.Db.Ubicaciones.Where(u => u.Id == s.RackId).Select(u => u.SubAlmacenId).SingleAsync();

        await Reincorporador(s).Handle(new(insumosSub, Fecha, "Revisión", [new(ArticuloId, 1m, s.RackId)]), default);

        var salida = await s.Db.Movimientos.Include(m => m.Lineas).SingleAsync(m => m.Tipo == TipoMovimiento.SalidaPorLiberacionRevision);
        var entrada = await s.Db.Movimientos.Include(m => m.Lineas).SingleAsync(m => m.Tipo == TipoMovimiento.ReincorporacionTrasRevision);
        salida.Lineas.Single().UbicacionId.Should().Be(bin8A); // toma el saldo libre, no el reservado de la recepción
        salida.Lineas.Single().Cantidad.Should().Be(1m);
        entrada.Lineas.Single().UbicacionId.Should().Be(s.RackId);
        entrada.Lineas.Single().Cantidad.Should().Be(1m);
    }

    [Fact]
    public async Task Reincorporar_8A_usa_la_unidad_base_del_articulo_no_PZA()
    {
        var s = await SembrarAsync();
        await AgregarSaldo8AAsync(s, 3m);
        var insumosSub = await s.Db.Ubicaciones.Where(u => u.Id == s.RackId).Select(u => u.SubAlmacenId).SingleAsync();

        await Reincorporador(s, "M2").Handle(new(insumosSub, Fecha, "Revisión", [new(ArticuloId, 1.5m, s.RackId)]), default);

        var lineas = await s.Db.LineasMovimiento
            .Where(l => l.Cantidad == 1.5m).ToListAsync();
        lineas.Should().HaveCount(2).And.OnlyContain(l => l.UnidadMedida == "M2");
    }

    [Fact]
    public async Task Reincorporar_8A_no_consume_lo_reservado_para_dictamen()
    {
        var s = await SembrarAsync(); // MAT-REV solo tiene los 2 de la recepción, sin dictaminar
        var insumosSub = await s.Db.Ubicaciones.Where(u => u.Id == s.RackId).Select(u => u.SubAlmacenId).SingleAsync();

        await Error(() => Reincorporador(s).Handle(
            new(insumosSub, Fecha, "Revisión", [new(ArticuloId, 1m, s.RackId)]), default), "MATREV_RESERVADO_PARA_DICTAMEN");
        (await s.Db.Movimientos.CountAsync(m => m.Tipo == TipoMovimiento.SalidaPorLiberacionRevision)).Should().Be(0);
    }

    [Fact]
    public async Task Baja_8A_no_consume_lo_reservado_para_dictamen_pero_si_el_saldo_ajeno()
    {
        var s = await SembrarAsync();
        var bin8A = await AgregarSaldo8AAsync(s, 3m);

        await Error(() => Bajador(s).Handle(
            new(s.RevSubId, Fecha, "Daño", [new(ArticuloId, 1m, s.RevBinId)]), default), "MATREV_RESERVADO_PARA_DICTAMEN");

        await Bajador(s).Handle(new(s.RevSubId, Fecha, "Daño", [new(ArticuloId, 3m, bin8A)]), default);
        (await s.Db.Movimientos.CountAsync(m => m.Tipo == TipoMovimiento.BajaPorDano)).Should().Be(1);
    }

    [Fact]
    public async Task Baja_8A_libera_la_reserva_conforme_se_dictamina()
    {
        var s = await SembrarAsync();
        s.Db.DictamenesRevision.Add(new DictamenRevision(Guid.NewGuid(), EmpresaId, s.LineaRevisionId, Guid.NewGuid(),
            ResultadoDictamen.Baja, 2m, "Destruido", Guid.NewGuid(), movimientoSalidaRevisionId: Guid.NewGuid()));
        await s.Db.SaveChangesAsync();

        // Todo dictaminado: ya no hay reserva y el saldo (2 sembrado) queda para el consumo normal.
        await Bajador(s).Handle(new(s.RevSubId, Fecha, "Daño", [new(ArticuloId, 1m, s.RevBinId)]), default);
        (await s.Db.Movimientos.CountAsync(m => m.Tipo == TipoMovimiento.BajaPorDano)).Should().Be(1);
    }

    [Fact]
    public async Task Rechazar_sin_evidencia_se_rechaza()
    {
        var s = await SembrarAsync();
        await Error(() => Handler(s.Db).Handle(
            new(s.LineaRevisionId, ResultadoDictamen.Rechazar, 1m, "No sirve", Fecha, null), default), "DICTAMEN_RECHAZO_SIN_EVIDENCIA");
        (await s.Db.DevolucionesProveedor.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Rechazar_con_evidencia_abre_devolucion_en_autorizacion_con_referencia_adjunto()
    {
        var s = await SembrarAsync();
        var adjuntoId = Guid.NewGuid();
        var r = await Handler(s.Db, new AdjuntoEvidenciaLectura(adjuntoId, "foto.png")).Handle(
            new(s.LineaRevisionId, ResultadoDictamen.Rechazar, 1m, "No sirve", Fecha, null), default);

        var dev = await s.Db.DevolucionesProveedor.Include(d => d.Lineas).Include(d => d.Evidencias).SingleAsync();
        r.DevolucionAProveedorId.Should().Be(dev.Id);
        dev.Estado.Should().Be(EstadoDevolucionProveedor.EnAutorizacion);
        dev.ProveedorId.Should().Be(ProveedorId);
        dev.Lineas.Should().ContainSingle().Which.LineaRecepcionOrigenId.Should().Be(s.LineaRevisionId);
        var ev = dev.Evidencias.Should().ContainSingle().Subject;
        ev.BlobRef.Should().Be($"adjunto:{adjuntoId}");
        ev.Comentario.Should().Be("Golpeado");
        (await s.Db.DictamenesRevision.SingleAsync()).DevolucionAProveedorId.Should().Be(dev.Id);
    }
}
