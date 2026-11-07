using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Catalogo;
using Millet.Almacen.Application.DevolucionesInternas;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Vales;
using Millet.Almacen.Domain.Catalogo;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.Incidencias;

namespace Millet.Almacen.UnitTests.P1;

/// <summary>
/// U-4 (86e3yzemw): los pasos de Almacén de la oleada 1 abren su incidencia al rechazarse y la resuelven al retomarse.
/// El puerto se sustituye por un doble que registra lo que se abre y lo que se resuelve.
/// </summary>
public sealed class IncidenciasAlmacenU4Tests
{
    internal sealed class IncidenciasFake : IIncidenciasPort
    {
        public List<AbrirIncidenciaSolicitud> Abiertas { get; } = [];
        public List<(string Paso, Guid DocumentoId, string? DetalleClave, string Resolucion)> Resueltas { get; } = [];

        public Task<IncidenciaAbierta> AbrirAsync(AbrirIncidenciaSolicitud solicitud, CancellationToken cancellationToken)
        {
            Abiertas.Add(solicitud);
            return Task.FromResult(new IncidenciaAbierta(Guid.NewGuid(), Abiertas.Count, false));
        }

        public Task<int> ResolverPorDocumentoAsync(string paso, Guid documentoId, string? detalleClave, string resolucion, CancellationToken cancellationToken)
        {
            Resueltas.Add((paso, documentoId, detalleClave, resolucion));
            return Task.FromResult(1);
        }

        public Task<bool> HayPendientesAsync(Guid documentoId, string? paso, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private static RegistrarRecepcionLineaInput Linea(P1Fixture f, decimal cantidad, decimal rechazada = 0m) =>
        new(f.ArticuloId, f.LineaId, cantidad, null, null, f.BinId,
            CantidadRechazada: rechazada, MotivoRechazo: rechazada > 0 ? "No es el material pedido" : null,
            EvidenciaBlobRef: rechazada > 0 ? "blob://evidencia/foto.jpg" : null);

    private static Task<RegistrarRecepcionResponse> ConFactura(P1Fixture f, IIncidenciasPort inc, params RegistrarRecepcionLineaInput[] lineas) =>
        new RegistrarRecepcionConFacturaHandler(f.Db, f.Oc, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true),
            new P7Support.Conversion(), CfdiFake.Valido(), new ProveedorRfcFake(), incidencias: inc)
            .Handle(new(f.DocumentoId, P1Fixture.Fecha, Guid.NewGuid(), null, null, lineas), default);

    [Fact]
    public async Task Recepcion_con_partida_rechazada_abre_incidencia_contra_la_orden_a_cargo_de_quien_recibe()
    {
        await using var f = new P1Fixture();
        var inc = new IncidenciasFake();

        var r = await ConFactura(f, inc, Linea(f, cantidad: 2, rechazada: 3));

        var abierta = inc.Abiertas.Should().ContainSingle().Subject;
        abierta.Paso.Should().Be(PasosIncidencia.AlmRecepcionDiferenciaOc);
        (abierta.Modulo, abierta.TipoDocumento, abierta.DocumentoId).Should().Be(("almacen", "orden_compra", f.DocumentoId));
        abierta.DetalleClave.Should().Be($"rechazo:{f.LineaId}");
        abierta.ResponsableUsuarioId.Should().Be(f.Context.UserId!.Value);
        abierta.Motivo.Should().Contain("Se rechazaron 3").And.Contain("No es el material pedido");
        abierta.Detalle.Should().Contain(r.Folio).And.Contain("evidencia");
        abierta.PasoReanudacion.Should().Be(PasosIncidencia.AlmRecepcionRegistrar);
        abierta.RutaReanudacion.Should().Be($"/almacen/recepciones/{r.RecepcionId}");
        // Lo aceptado en esa misma recepción resuelve antes lo pendiente de recepciones anteriores de la partida.
        inc.Resueltas.Select(x => x.DetalleClave).Should().BeEquivalentTo([$"rechazo:{f.LineaId}", $"excede:{f.LineaId}"]);
    }

    [Fact]
    public async Task Recepcion_sin_rechazo_solo_resuelve_las_incidencias_de_la_partida_recibida()
    {
        await using var f = new P1Fixture();
        var inc = new IncidenciasFake();

        var r = await ConFactura(f, inc, Linea(f, cantidad: 4));

        inc.Abiertas.Should().BeEmpty();
        inc.Resueltas.Should().HaveCount(2).And.OnlyContain(x =>
            x.Paso == PasosIncidencia.AlmRecepcionDiferenciaOc && x.DocumentoId == f.DocumentoId && x.Resolucion.Contains(r.Folio));
    }

    [Fact]
    public async Task Recepcion_que_excede_lo_pendiente_rechaza_con_incidencia_y_no_guarda()
    {
        await using var f = new P1Fixture();
        var inc = new IncidenciasFake();

        var ex = await Assert.ThrowsAsync<IncidenciaRechazoException>(() => ConFactura(f, inc, Linea(f, cantidad: 50)));

        ex.Code.Should().Be("RECEPCION_EXCEDE_PENDIENTE");
        ex.Solicitud.Paso.Should().Be(PasosIncidencia.AlmRecepcionDiferenciaOc);
        ex.Solicitud.CodigoMotivo.Should().Be("RECEPCION_EXCEDE_PENDIENTE");
        (ex.Solicitud.DocumentoId, ex.Solicitud.DetalleClave).Should().Be((f.DocumentoId, $"excede:{f.LineaId}"));
        ex.Solicitud.ResponsableUsuarioId.Should().Be(Guid.Empty, "el pipeline asigna a quien recibió el rechazo");
        inc.Abiertas.Should().BeEmpty("la abre el pipeline, fuera de la transacción revertida");
        (await f.Db.Movimientos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Entrada_a_rack_sin_asignacion_rechaza_con_incidencia_del_articulo_y_la_ubicacion()
    {
        await using var f = new P1Fixture();
        var rack = new Ubicacion(Guid.NewGuid(), f.SubId, "R9", "Rack sin asignar");
        f.Db.Ubicaciones.Add(rack);
        await f.Db.SaveChangesAsync();
        var sucursal = (await f.Db.Almacenes.SingleAsync()).SucursalId;

        var ex = await Assert.ThrowsAsync<IncidenciaRechazoException>(() =>
            UbicacionEntradaGuard.ValidarEntradaYDerivarSubAsync(f.Db, rack.Id, f.ArticuloId, default));

        ex.Code.Should().Be("ENTRADA_SIN_ASIGNACION");
        var s = ex.Solicitud;
        (s.Paso, s.TipoDocumento, s.DocumentoId, s.DetalleClave).Should()
            .Be((PasosIncidencia.AlmUbicacionesAsignacion, "articulo", f.ArticuloId, rack.Id.ToString()));
        s.SucursalId.Should().Be(sucursal);
        s.DocumentoEtiqueta.Should().Contain("R9");
        s.PasoReanudacion.Should().Be(PasosIncidencia.AlmUbicacionesAsignar);
        s.RutaReanudacion.Should().Be($"/almacen/asignaciones?articuloId={f.ArticuloId}");
    }

    [Fact]
    public async Task Vale_con_requisicion_invalida_rechaza_con_incidencia_del_vale()
    {
        await using var f = new P1Fixture();
        var vale = await f.MovimientoAsync(TipoMovimiento.SalidaPorVale);
        var rq = (await f.Rq.ObtenerAsync(f.DocumentoId, default))!;
        var inc = new IncidenciasFake();
        var h = new RegularizarSalidaPorValeHandler(f.Db, new P1Fixture.RqPort(rq with { Estado = "Borrador" }), f.Events, inc);

        var ex = await Assert.ThrowsAsync<IncidenciaRechazoException>(() => h.Handle(new(vale.Id, f.DocumentoId), default));

        ex.Code.Should().Be("VALE_RQ_REGULARIZADORA_INVALIDA");
        (ex.Solicitud.Paso, ex.Solicitud.TipoDocumento, ex.Solicitud.DocumentoId).Should()
            .Be((PasosIncidencia.AlmValeRectificar, "vale", vale.Id));
        ex.Solicitud.SucursalId.Should().Be((await f.Db.Almacenes.SingleAsync()).SucursalId);
        ex.Solicitud.RutaReanudacion.Should().Be($"/almacen/salidas/{vale.Id}");
        inc.Resueltas.Should().BeEmpty();
    }

    [Fact]
    public async Task Vale_regularizado_resuelve_su_incidencia()
    {
        await using var f = new P1Fixture();
        var vale = await f.MovimientoAsync(TipoMovimiento.SalidaPorVale);
        var sucursal = (await f.Db.Almacenes.SingleAsync()).SucursalId;
        var rq = (await f.Rq.ObtenerAsync(f.DocumentoId, default))!;
        var inc = new IncidenciasFake();

        await new RegularizarSalidaPorValeHandler(f.Db, new P1Fixture.RqPort(rq with { SucursalId = sucursal }), f.Events, inc)
            .Handle(new(vale.Id, f.DocumentoId), default);

        var resuelta = inc.Resueltas.Should().ContainSingle().Subject;
        (resuelta.Paso, resuelta.DocumentoId, resuelta.DetalleClave).Should().Be((PasosIncidencia.AlmValeRectificar, vale.Id, (string?)null));
        resuelta.Resolucion.Should().Contain(rq.Folio);
    }

    [Fact]
    public async Task Devolucion_interna_resuelve_la_diferencia_de_acuse_de_la_salida()
    {
        await using var f = new P1Fixture();
        var salida = await f.MovimientoAsync(TipoMovimiento.SalidaConsumo);
        var inc = new IncidenciasFake();
        var handler = new AplicarDevolucionInternaHandler(f.Db, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true), inc);

        var r = await handler.Handle(new AplicarDevolucionInternaCommand(salida.Id, f.SubId, P1Fixture.Fecha, "Integro", "Conteo distinto", null,
            [new(salida.Lineas.Single().Id, 2, f.BinId)]), default);

        var resuelta = inc.Resueltas.Should().ContainSingle().Subject;
        (resuelta.Paso, resuelta.DocumentoId).Should().Be((PasosIncidencia.AlmSalidaAcuse, salida.Id));
        resuelta.Resolucion.Should().Contain(r.Folio);
    }

    [Fact]
    public async Task Si_el_puerto_falla_la_recepcion_queda_registrada()
    {
        await using var f = new P1Fixture();

        var r = await ConFactura(f, new IncidenciasQueFallan(), Linea(f, cantidad: 2, rechazada: 1));

        (await f.Db.Movimientos.AnyAsync(m => m.Id == r.RecepcionId)).Should().BeTrue();
    }

    private sealed class IncidenciasQueFallan : IIncidenciasPort
    {
        public Task<IncidenciaAbierta> AbrirAsync(AbrirIncidenciaSolicitud solicitud, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Compartido no disponible");
        public Task<int> ResolverPorDocumentoAsync(string paso, Guid documentoId, string? detalleClave, string resolucion, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Compartido no disponible");
        public Task<bool> HayPendientesAsync(Guid documentoId, string? paso, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}
