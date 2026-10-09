using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Cierre;
using Millet.Almacen.Application.DevolucionesInternas;
using Millet.Almacen.Application.DevolucionesProveedor;
using Millet.Almacen.Application.Integration;
using Millet.Almacen.Application.Vales;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Domain.Conteos;
using Millet.Almacen.Domain.DevolucionesProveedor;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.P1;

public sealed class DevolucionesYCierreTests
{
    [Fact]
    public async Task Segunda_devolucion_interna_no_puede_exceder_salida()
    {
        await using var f = new P1Fixture();
        var salida = await f.MovimientoAsync(TipoMovimiento.SalidaConsumo);
        var handler = new AplicarDevolucionInternaHandler(f.Db, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true));
        var cmd = new AplicarDevolucionInternaCommand(salida.Id, f.SubId, P1Fixture.Fecha, "Integro", "Retorno", null,
            [new(salida.Lineas.Single().Id, 6, f.BinId)]);
        await handler.Handle(cmd, default);
        var devolucion = await f.Db.Movimientos.Include(m => m.Lineas).SingleAsync(m => m.Tipo == TipoMovimiento.DevolucionSalida);
        devolucion.Lineas.Single().LineaSalidaOrigenId.Should().Be(salida.Lineas.Single().Id);
        await Error(() => handler.Handle(cmd, default), "DEVOLUCION_EXCEDE_SALIDA");
        (await f.Db.Movimientos.CountAsync()).Should().Be(2);
        f.Events.Items.Should().ContainSingle();
    }
    [Fact]
    public async Task Devolucion_proveedor_toma_costo_origen_y_rechaza_exceso_y_proveedor_ajeno()
    {
        await using var f = new P1Fixture();
        var recepcion = await f.MovimientoAsync(TipoMovimiento.EntradaCompra);
        var oc = (await f.Oc.ObtenerAsync(f.DocumentoId, default))!;
        var h = new IniciarDevolucionAProveedorHandler(f.Db, f.Context, f.Context, f.Guard, f.Oc);
        var cmd = new IniciarDevolucionAProveedorCommand(oc.ProveedorId, "Defecto", recepcion.Id, null, null, f.SubId,
            [new(f.ArticuloId, 6, "PZA", 999, recepcion.Lineas.Single().Id)]);
        await Error(() => h.Handle(cmd with { ProveedorId = Guid.NewGuid() }, default), "DEVOLUCION_PROVEEDOR_INVALIDO");
        await Error(() => h.Handle(cmd with { RecepcionOrigenId = null }, default), "DEVOLUCION_RECEPCION_INVALIDA");
        await Error(() => h.Handle(cmd with { Lineas = [cmd.Lineas[0] with { Cantidad = 11 }] }, default), "DEVOLUCION_EXCEDE_RECEPCION");
        var result = await h.Handle(cmd, default);
        var dev = await f.Db.Set<DevolucionAProveedor>().Include(d => d.Lineas).SingleAsync();
        dev.Lineas.Single().CostoUnitarioMxn.Should().Be(25);
        await Error(() => h.Handle(cmd, default), "DEVOLUCION_EXCEDE_RECEPCION");
        dev.AgregarEvidencia(new(Guid.NewGuid(), dev.Id, "Foto", "foto.png", "foto.png"));
        dev.SolicitarAutorizacion(); dev.Autorizar(P1Fixture.EmpresaId);
        await f.Db.SaveChangesAsync();
        await new RegistrarSalidaDevolucionAProveedorHandler(f.Db, f.Events, f.Context, f.Context, f.Oc, new PeriodoContableStub(true))
            .Handle(new(result.DevolucionId, f.SubId, P1Fixture.Fecha, [new(dev.Lineas.Single().Id, f.BinId)]), default);
        var evento = f.Events.Items.OfType<OcDevolucionRegistradaIntegrationEvent>().Single();
        evento.MontoTotalMxn.Should().Be(150);
        evento.Lineas.Single().CostoUnitarioMxn.Should().Be(25);
        evento.Lineas.Single().LineaOcId.Should().Be(f.LineaId);
    }
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task MatRev_respeta_periodo_y_permite_camino_valido(bool cerrado, bool reincorporar)
    {
        await using var f = new P1Fixture();
        if (cerrado) await Cerrar(f);
        Task Ejecutar() => reincorporar
            ? new ReincorporacionTrasRevisionHandler(f.Db, f.Context, f.Context, f.Guard, new PeriodoContableStub(true)).Handle(new(f.SubId, P1Fixture.Fecha, "Revisión", [new(f.ArticuloId, 1, f.BinId)]), default)
            : new BajaPorDanoHandler(f.Db, f.Context, f.Context, f.Guard, new PeriodoContableStub(true)).Handle(new(f.SubId, P1Fixture.Fecha, "Daño", [new(f.ArticuloId, 1, f.BinId)]), default);
        if (cerrado) await Error(Ejecutar, "PERIODO_CERRADO"); else await Ejecutar();
        (await f.Db.Movimientos.CountAsync()).Should().Be(cerrado ? 0 : 1);
    }
    [Fact]
    public async Task Salida_proveedor_en_periodo_cerrado_no_genera_movimiento_ni_evento()
    {
        await using var f = new P1Fixture();
        var dev = new DevolucionAProveedor(Guid.NewGuid(), P1Fixture.EmpresaId, Guid.NewGuid(), "Defecto", P1Fixture.EmpresaId);
        f.Db.Set<DevolucionAProveedor>().Add(dev);
        await Cerrar(f);
        await Error(() => new RegistrarSalidaDevolucionAProveedorHandler(f.Db, f.Events, f.Context, f.Context, f.Oc, new PeriodoContableStub(true))
            .Handle(new(dev.Id, f.SubId, P1Fixture.Fecha), default), "PERIODO_CERRADO");
        (await f.Db.Movimientos.CountAsync()).Should().Be(0);
        f.Events.Items.Should().BeEmpty();
    }
    [Theory]
    [InlineData(EstadoConteo.Planificado)] [InlineData(EstadoConteo.EnCurso)]
    [InlineData(EstadoConteo.EnConciliacion)] [InlineData(EstadoConteo.Aprobado)]
    public async Task Cierre_bloquea_todo_conteo_no_terminal(EstadoConteo estado)
    {
        await using var f = new P1Fixture();
        var conteo = new ConteoInventario(Guid.NewGuid(), P1Fixture.EmpresaId, TipoConteo.Rotativo, P1Fixture.Fecha, Guid.NewGuid(), f.SubId);
        typeof(ConteoInventario).GetProperty(nameof(ConteoInventario.Estado))!.SetValue(conteo, estado);
        f.Db.Set<ConteoInventario>().Add(conteo); await f.Db.SaveChangesAsync();
        await Error(() => new EjecutarCierreMensualHandler(f.Db, f.Context, f.Context).Handle(new(2026, 10), default), "CIERRE_CONTEOS_PENDIENTES");
        (await f.Db.Set<PeriodoCerrado>().CountAsync()).Should().Be(0);
    }
    [Fact]
    public async Task Cierre_bloquea_conteo_iniciado_antes_de_su_fecha_planificada()
    {
        await using var f = new P1Fixture();
        var conteo = new ConteoInventario(Guid.NewGuid(), P1Fixture.EmpresaId, TipoConteo.Rotativo, new(2026, 11, 2), Guid.NewGuid(), f.SubId);
        typeof(ConteoInventario).GetProperty(nameof(ConteoInventario.Estado))!.SetValue(conteo, EstadoConteo.EnCurso);
        typeof(ConteoInventario).GetProperty(nameof(ConteoInventario.FechaInicio))!.SetValue(conteo, new DateTimeOffset(2026, 10, 30, 12, 0, 0, TimeSpan.Zero));
        f.Db.Set<ConteoInventario>().Add(conteo);
        await f.Db.SaveChangesAsync();

        await Error(() => new EjecutarCierreMensualHandler(f.Db, f.Context, f.Context).Handle(new(2026, 10), default), "CIERRE_CONTEOS_PENDIENTES");
        (await f.Db.Set<PeriodoCerrado>().CountAsync()).Should().Be(0);
    }
    [Theory]
    [InlineData(EstadoConteo.Aplicado)] [InlineData(EstadoConteo.Rechazado)] [InlineData(EstadoConteo.Planificado)]
    public async Task Cierre_permite_conteos_terminales_y_planificados_del_mes_siguiente(EstadoConteo estado)
    {
        await using var f = new P1Fixture();
        var fecha = estado == EstadoConteo.Planificado ? new DateOnly(2026, 11, 2) : P1Fixture.Fecha;
        var conteo = new ConteoInventario(Guid.NewGuid(), P1Fixture.EmpresaId, TipoConteo.Rotativo, fecha, Guid.NewGuid(), f.SubId);
        typeof(ConteoInventario).GetProperty(nameof(ConteoInventario.Estado))!.SetValue(conteo, estado);
        f.Db.Set<ConteoInventario>().Add(conteo);
        await f.Db.SaveChangesAsync();

        await new EjecutarCierreMensualHandler(f.Db, f.Context, f.Context).Handle(new(2026, 10), default);
        (await f.Db.Set<PeriodoCerrado>().CountAsync()).Should().Be(1);
    }
    [Fact]
    public async Task Cierre_bloquea_vale_pendiente_y_permite_tras_regularizacion()
    {
        await using var f = new P1Fixture();
        var vale = await f.MovimientoAsync(TipoMovimiento.SalidaPorVale);
        var h = new EjecutarCierreMensualHandler(f.Db, f.Context, f.Context);
        await Error(() => h.Handle(new(2026, 10), default), "CIERRE_VALES_PENDIENTES");
        await new RegularizarSalidaPorValeHandler(f.Db, f.Rq).Handle(new(vale.Id, f.DocumentoId), default);
        await h.Handle(new(2026, 10), default);
        (await f.Db.Set<PeriodoCerrado>().CountAsync()).Should().Be(1);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Vale_valida_ceco_y_guarda_plazo(bool invalido)
    {
        await using var f = new P1Fixture();
        var h = new RegistrarSalidaPorValeHandler(f.Db, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true), new P1Fixture.Calendario(), invalido ? new CentroInactivo() : new P1Fixture.Centros());
        Task Ejecutar() => h.Handle(new(P1Fixture.Fecha, "vale.pdf", null, null, [new(f.ArticuloId, null, 1, Guid.NewGuid(), null, null, null, f.BinId)]), default);
        if (invalido)
        {
            await Error(Ejecutar, "CECO_INVALIDO");
            f.Events.Items.Should().BeEmpty();
            (await f.Db.Movimientos.CountAsync()).Should().Be(0);
        }
        else
        {
            await Ejecutar();
            var m = await f.Db.Movimientos.SingleAsync();
            m.FechaLimiteRegularizacion.Should().Be(new DateTimeOffset(2026, 10, 13, 6, 0, 0, TimeSpan.Zero));
        }
    }
    [Fact]
    public async Task Aplicar_conteo_no_genera_ajustes_en_periodo_cerrado()
    {
        await using var f = new P1Fixture();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        var conteo = new ConteoInventario(Guid.NewGuid(), P1Fixture.EmpresaId, TipoConteo.Rotativo, hoy, Guid.NewGuid(), f.SubId);
        conteo.AgregarLinea(new LineaConteo(Guid.NewGuid(), conteo.Id, f.ArticuloId, f.SubId, f.BinId, 10, 25));
        conteo.Iniciar(); conteo.Lineas.Single().Capturar(9, Guid.NewGuid());
        conteo.EnviarAConciliacion(); conteo.Aprobar(Guid.NewGuid());
        f.Db.Set<ConteoInventario>().Add(conteo);
        f.Db.Set<PeriodoCerrado>().Add(new(Guid.NewGuid(), P1Fixture.EmpresaId, hoy.Year, hoy.Month, P1Fixture.EmpresaId));
        await f.Db.SaveChangesAsync();
        await Error(() => new Millet.Almacen.Application.Conteos.AplicarConteoHandler(f.Db, f.Events, f.Context, f.Context, new PeriodoContableStub(true))
            .Handle(new(conteo.Id), default), "PERIODO_CERRADO");
        (await f.Db.Movimientos.CountAsync()).Should().Be(0);
        f.Events.Items.Should().BeEmpty();
    }

    private sealed class CentroInactivo : ICentroCostoElegibilidadPort
    {
        public Task ValidarAsync(Guid id, CancellationToken ct) => throw new BusinessRuleException("CECO_INVALIDO", "El centro de costo está inactivo.");
    }
    private static async Task Cerrar(P1Fixture f)
    {
        f.Db.Set<PeriodoCerrado>().Add(new(Guid.NewGuid(), P1Fixture.EmpresaId, 2026, 10, P1Fixture.EmpresaId));
        await f.Db.SaveChangesAsync();
    }
    private static async Task Error(Func<Task> action, string code) => (await Assert.ThrowsAsync<BusinessRuleException>(action)).Code.Should().Be(code);
}
