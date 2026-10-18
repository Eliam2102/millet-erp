using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.Application.Vales;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.Domain.Ports;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.P1;

/// <summary>
/// F5 (86e3pc50v): la RQ y el almacén de la salida con RQ o de la regularización de vale deben ser de la misma sucursal.
/// </summary>
public sealed class SucursalRqF5Tests
{
    [Fact]
    public async Task Salida_con_rq_de_otra_sucursal_lanza_SALIDA_SUCURSAL_DISTINTA()
    {
        await using var f = new P1Fixture();
        var rq = (await f.Rq.ObtenerAsync(f.DocumentoId, default))!;
        var port = new P1Fixture.RqPort(rq with { SucursalId = Guid.NewGuid() });

        await Error(() => Salir(f, port), "SALIDA_SUCURSAL_DISTINTA");
        f.Events.Items.Should().BeEmpty();
        (await f.Db.Movimientos.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Salida_con_rq_de_la_misma_sucursal_no_lanza()
    {
        await using var f = new P1Fixture();
        var sucursal = (await f.Db.Almacenes.SingleAsync()).SucursalId;
        var rq = (await f.Rq.ObtenerAsync(f.DocumentoId, default))!;

        await Salir(f, new P1Fixture.RqPort(rq with { SucursalId = sucursal }));
        (await f.Db.Movimientos.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Vale_regularizado_con_rq_de_otra_sucursal_lanza_VALE_SUCURSAL_DISTINTA()
    {
        await using var f = new P1Fixture();
        var vale = await f.MovimientoAsync(TipoMovimiento.SalidaPorVale);
        var rq = (await f.Rq.ObtenerAsync(f.DocumentoId, default))!;
        var h = new RegularizarSalidaPorValeHandler(f.Db, new P1Fixture.RqPort(rq with { SucursalId = Guid.NewGuid() }));

        await Error(() => h.Handle(new(vale.Id, f.DocumentoId), default), "VALE_SUCURSAL_DISTINTA");
        vale.PendienteRegularizacion.Should().BeTrue();
        f.Events.Items.Should().BeEmpty();
        (await f.Db.Movimientos.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Vale_regularizado_con_rq_de_la_misma_sucursal_no_lanza()
    {
        await using var f = new P1Fixture();
        var vale = await f.MovimientoAsync(TipoMovimiento.SalidaPorVale);
        var sucursal = (await f.Db.Almacenes.SingleAsync()).SucursalId;
        var rq = (await f.Rq.ObtenerAsync(f.DocumentoId, default))!;

        await new RegularizarSalidaPorValeHandler(f.Db, new P1Fixture.RqPort(rq with { SucursalId = sucursal }))
            .Handle(new(vale.Id, f.DocumentoId), default);
        vale.PendienteRegularizacion.Should().BeFalse();
    }

    private static Task<RegistrarSalidaResponse> Salir(P1Fixture f, IComprasRequisicionReadPort port) =>
        new RegistrarSalidaConRequisicionHandler(f.Db, port, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true), new P7Support.Conversion(), P7Support.Apartados(f.Db))
            .Handle(new(f.DocumentoId, P1Fixture.Fecha, null, null, [new(f.ArticuloId, f.LineaId, 1, null, null, null, null, f.BinId)]), default);

    private static async Task Error(Func<Task> action, string code) => (await Assert.ThrowsAsync<BusinessRuleException>(action)).Code.Should().Be(code);
}
