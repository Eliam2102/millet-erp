using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Vales;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.Calendario;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.P1;

public sealed class DocumentosValidosTests
{
    [Theory]
    [InlineData(false, null)] [InlineData(true, null)]
    [InlineData(false, "Cancelada")] [InlineData(true, "Cancelada")]
    [InlineData(false, "Borrador")] [InlineData(true, "Borrador")]
    [InlineData(false, "Cerrada")] [InlineData(true, "Cerrada")]
    [InlineData(false, "EnAutorizacionJefeCompras")] [InlineData(true, "EnAutorizacionJefeCompras")]
    [InlineData(false, "EnAutorizacionDireccion")] [InlineData(true, "EnAutorizacionDireccion")]
    [InlineData(false, "Rechazada")] [InlineData(true, "Rechazada")]
    public async Task Recepcion_rechaza_documento_sin_movimiento_ni_evento(bool packing, string? estado)
    {
        await using var f = new P1Fixture();
        var oc = await f.Oc.ObtenerAsync(f.DocumentoId, default);
        var port = new P1Fixture.OcPort(estado is null ? null : oc! with { Estado = estado });
        await Rechaza(f, () => Recibir(f, packing, port, [Linea(f)]), estado is null ? "RECEPCION_OC_NO_ENCONTRADA" : "RECEPCION_OC_NO_AUTORIZADA");
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Recepcion_exige_linea_y_articulo_y_suma_repetidas(bool packing)
    {
        await using var f = new P1Fixture();
        await Rechaza(f, () => Recibir(f, packing, f.Oc, [Linea(f) with { LineaOcId = null }]), "RECEPCION_LINEA_OC_REQUERIDA");
        await Rechaza(f, () => Recibir(f, packing, f.Oc, [Linea(f) with { ArticuloId = Guid.NewGuid() }]), "RECEPCION_LINEA_OC_ARTICULO_INCONGRUENTE");
        await Rechaza(f, () => Recibir(f, packing, f.Oc, [Linea(f) with { Cantidad = 6 }, Linea(f) with { Cantidad = 6 }]), "RECEPCION_EXCEDE_TOLERANCIA");
        var oc = (await f.Oc.ObtenerAsync(f.DocumentoId, default))!;
        await Rechaza(f, () => Recibir(f, packing, new P1Fixture.OcPort(oc with { Lineas = [oc.Lineas[0] with { PrecioUnitarioMxn = 0 }] }), [Linea(f)]), "RECEPCION_COSTO_OC_INVALIDO");
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Recepcion_valida_conserva_costo_y_linea(bool packing)
    {
        await using var f = new P1Fixture();
        await Recibir(f, packing, f.Oc, [Linea(f) with { Cantidad = 6 }, Linea(f) with { Cantidad = 5 }]);
        var m = await f.Db.Movimientos.Include(m => m.Lineas).SingleAsync();
        m.Lineas.Should().OnlyContain(l => l.LineaOcId == f.LineaId && l.CostoUnitarioMxn == 25);
        f.Events.Items.Should().ContainSingle();
    }
    [Theory]
    [InlineData(null)] [InlineData("Cancelada")] [InlineData("Borrador")]
    [InlineData("EnAutorizacion")] [InlineData("Rechazada")] [InlineData("Cerrada")]
    [InlineData("CerradaSinSurtir")] [InlineData("CerradaSurtidaParcial")] [InlineData("Eliminada")]
    public async Task Salida_rechaza_rq_invalida(string? estado)
    {
        await using var f = new P1Fixture();
        var rq = (await f.Rq.ObtenerAsync(f.DocumentoId, default))!;
        await Rechaza(f, () => Salir(f, new P1Fixture.RqPort(estado is null ? null : rq with { Estado = estado }), [Salida(f)]), estado is null ? "SALIDA_RQ_NO_ENCONTRADA" : "SALIDA_RQ_NO_AUTORIZADA");
    }
    [Fact]
    public async Task Salida_exige_linea_articulo_y_disponible_acumulado()
    {
        await using var f = new P1Fixture();
        await Rechaza(f, () => Salir(f, f.Rq, [Salida(f) with { LineaRqId = null }]), "SALIDA_LINEA_RQ_REQUERIDA");
        await Rechaza(f, () => Salir(f, f.Rq, [Salida(f) with { ArticuloId = Guid.NewGuid() }]), "SALIDA_LINEA_RQ_INVALIDA");
        await Rechaza(f, () => Salir(f, f.Rq, [Salida(f) with { Cantidad = 6 }, Salida(f) with { Cantidad = 5 }]), "SALIDA_EXCEDE_AUTORIZADO");
        await Salir(f, f.Rq, [Salida(f) with { Cantidad = 10 }]);
        (await f.Db.Movimientos.CountAsync()).Should().Be(1);
    }
    [Theory]
    [InlineData(null)] [InlineData("Cancelada")] [InlineData("EnSurtido")]
    public async Task Vale_rechaza_rq_inexistente_no_autorizada_o_insuficiente(string? estado)
    {
        await using var f = new P1Fixture();
        var vale = await f.MovimientoAsync(Domain.Movimientos.TipoMovimiento.SalidaPorVale, 11);
        var rq = (await f.Rq.ObtenerAsync(f.DocumentoId, default))!;
        var h = new RegularizarSalidaPorValeHandler(f.Db, new P1Fixture.RqPort(estado is null ? null : rq with { Estado = estado }));
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => h.Handle(new(vale.Id, f.DocumentoId), default));
        ex.Code.Should().Be("VALE_RQ_REGULARIZADORA_INVALIDA");
        vale.PendienteRegularizacion.Should().BeTrue();
    }
    [Fact]
    public async Task Vale_regulariza_con_rq_que_cubre_articulos_y_cantidades()
    {
        await using var f = new P1Fixture();
        var vale = await f.MovimientoAsync(Domain.Movimientos.TipoMovimiento.SalidaPorVale);
        await new RegularizarSalidaPorValeHandler(f.Db, f.Rq).Handle(new(vale.Id, f.DocumentoId), default);
        vale.PendienteRegularizacion.Should().BeFalse();
    }
    [Fact]
    public void Plazo_cruza_fin_de_semana_y_festivo_desde_fecha_del_vale()
    {
        var limite = CalendarioHabil.SumarHoras(new(2026, 11, 13), 48, CalendarioHabil.LeerFestivos(CalendarioHabil.FestivosIniciales), TimeZoneInfo.FindSystemTimeZoneById("America/Merida"));
        limite.Should().Be(new DateTimeOffset(2026, 11, 18, 6, 0, 0, TimeSpan.Zero));
        var cambio = CalendarioHabil.SumarHoras(new(2026, 12, 31), 48, CalendarioHabil.LeerFestivos(CalendarioHabil.FestivosIniciales), TimeZoneInfo.Utc);
        cambio.Should().Be(new DateTimeOffset(2027, 1, 5, 0, 0, 0, TimeSpan.Zero));
    }
    [Theory]
    [InlineData("{}")] [InlineData("[\"2026-02-30\"]")] [InlineData("null")]
    public void Festivos_invalidos_no_se_aceptan(string valor) => Assert.Throws<BusinessRuleException>(() => CalendarioHabil.LeerFestivos(valor));

    private static RegistrarRecepcionLineaInput Linea(P1Fixture f) => new(f.ArticuloId, f.LineaId, 1, null, null, f.BinId);
    private static RegistrarSalidaLineaInput Salida(P1Fixture f) => new(f.ArticuloId, f.LineaId, 1, null, null, null, null, f.BinId);
    private static async Task Recibir(P1Fixture f, bool packing, IComprasOcReadPort port, RegistrarRecepcionLineaInput[] lineas)
    {
        if (packing) await new RegistrarRecepcionConPackingListHandler(f.Db, port, new P1Fixture.Articulos(), f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true), new P7Support.Conversion()).Handle(new(f.DocumentoId, P1Fixture.Fecha, "packing.pdf", null, lineas), default);
        else await new RegistrarRecepcionConFacturaHandler(f.Db, port, new P1Fixture.Articulos(), f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true), new P7Support.Conversion()).Handle(new(f.DocumentoId, P1Fixture.Fecha, Guid.NewGuid(), null, null, lineas), default);
    }
    private static async Task Salir(P1Fixture f, IComprasRequisicionReadPort port, RegistrarSalidaLineaInput[] lineas) => await new RegistrarSalidaConRequisicionHandler(f.Db, port, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true), new P7Support.Conversion(), P7Support.Apartados(f.Db)).Handle(new(f.DocumentoId, P1Fixture.Fecha, null, null, lineas), default);
    private static async Task Rechaza(P1Fixture f, Func<Task> action, string codigo)
    {
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(action);
        ex.Code.Should().Be(codigo);
        f.Events.Items.Should().BeEmpty();
        (await f.Db.Movimientos.CountAsync()).Should().Be(0);
        f.Db.ChangeTracker.HasChanges().Should().BeFalse();
    }
}
