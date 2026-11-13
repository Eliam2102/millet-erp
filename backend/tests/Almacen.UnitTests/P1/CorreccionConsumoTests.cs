using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Correcciones;
using Millet.Almacen.Application.DevolucionesInternas;
using Millet.Almacen.Application.Reportes;
using Millet.Almacen.Domain.Cierre;
using Millet.Almacen.Domain.Correcciones;
using Millet.Almacen.Domain.Movimientos;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.P1;

/// <summary>M-334 y M-331: corrección de consumo que autoriza Costos y se aplica en el mes corriente ligada a la salida.</summary>
public sealed class CorreccionConsumoTests
{
    private static readonly Guid Almacenista = Guid.NewGuid();

    private sealed class Sender(AplicarDevolucionInternaHandler h) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken ct = default) =>
            (TResponse)(object)await h.Handle((AplicarDevolucionInternaCommand)(object)request, ct);
        public Task Send<TRequest>(TRequest request, CancellationToken ct = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static Sender Mediador(P1Fixture f) =>
        new(new AplicarDevolucionInternaHandler(f.Db, f.Events, f.Context, f.Context, f.Guard, new PeriodoContableStub(true)));

    private static SolicitarCorreccionConsumo Solicitud(P1Fixture f, MovimientoInventario salida, decimal cantidad,
        string tipo = CorreccionConsumo.ErrorCaptura) =>
        new(salida.Id, f.SubId, tipo, "Se capturaron 2 de más", null, [new(salida.Lineas.Single().Id, cantidad, f.BinId)]);

    [Fact]
    public async Task Costos_autoriza_y_la_vista_muestra_10_consumidas_2_corregidas_y_8_netas()
    {
        await using var f = new P1Fixture();
        var salida = await f.MovimientoAsync(TipoMovimiento.SalidaConsumo);
        var c = await CorreccionConsumoApp.SolicitarAsync(f.Db, P1Fixture.EmpresaId, Almacenista, Solicitud(f, salida, 2), default);
        await f.Db.SaveChangesAsync();

        // Pendiente: no mueve saldo y la vista lo muestra como por autorizar.
        (await f.Db.Movimientos.CountAsync()).Should().Be(1);
        (await CorreccionConsumoApp.SaldosAsync(f.Db, salida, default)).Single().Pendiente.Should().Be(2);

        await CorreccionConsumoApp.AutorizarAsync(f.Db, Mediador(f), c, P1Fixture.EmpresaId, new DateOnly(2026, 10, 20), default);

        c.Estado.Should().Be(CorreccionConsumo.Autorizada);
        var dev = await f.Db.Movimientos.Include(m => m.Lineas).SingleAsync(m => m.Tipo == TipoMovimiento.DevolucionSalida);
        dev.SalidaOrigenId.Should().Be(salida.Id);
        dev.Lineas.Single().CostoUnitarioMxn.Should().Be(25); // al costo con que salió
        c.FolioDevolucion.Should().Be(dev.Folio);
        var s = (await CorreccionConsumoApp.SaldosAsync(f.Db, salida, default)).Single();
        (s.Consumido, s.Corregido, s.Neto, s.Pendiente).Should().Be((10m, 2m, 8m, 0m));
    }

    [Fact]
    public async Task No_excede_lo_consumido_no_se_crea_dos_veces_y_no_la_autoriza_quien_la_pidio()
    {
        await using var f = new P1Fixture();
        var salida = await f.MovimientoAsync(TipoMovimiento.SalidaConsumo);
        await Error(() => CorreccionConsumoApp.SolicitarAsync(f.Db, P1Fixture.EmpresaId, Almacenista, Solicitud(f, salida, 11), default),
            "CORRECCION_EXCEDE_CONSUMO");
        var c = await CorreccionConsumoApp.SolicitarAsync(f.Db, P1Fixture.EmpresaId, Almacenista, Solicitud(f, salida, 2), default);
        await f.Db.SaveChangesAsync();
        await Error(() => CorreccionConsumoApp.SolicitarAsync(f.Db, P1Fixture.EmpresaId, Almacenista, Solicitud(f, salida, 1), default),
            "CORRECCION_DUPLICADA");
        await Error(() => CorreccionConsumoApp.AutorizarAsync(f.Db, Mediador(f), c, Almacenista, P1Fixture.Fecha, default),
            "CORRECCION_MISMO_USUARIO");
        (await f.Db.Movimientos.CountAsync()).Should().Be(1); // sin autorización no se aplica

        c.Rechazar(P1Fixture.EmpresaId, "No procede");
        await f.Db.SaveChangesAsync();
        // Rechazada, ya se puede pedir otra; lo ya corregido sí cuenta contra lo consumido.
        await CorreccionConsumoApp.SolicitarAsync(f.Db, P1Fixture.EmpresaId, Almacenista, Solicitud(f, salida, 10), default);
    }

    [Fact]
    public async Task M331_una_salida_de_un_mes_cerrado_se_corrige_en_el_mes_corriente_y_el_kardex_liga_los_dos_folios()
    {
        await using var f = new P1Fixture();
        var salida = await f.MovimientoAsync(TipoMovimiento.SalidaConsumo); // 9-oct
        f.Db.Set<PeriodoCerrado>().Add(new PeriodoCerrado(Guid.NewGuid(), P1Fixture.EmpresaId, 2026, 10, P1Fixture.EmpresaId));
        await f.Db.SaveChangesAsync();
        var c = await CorreccionConsumoApp.SolicitarAsync(f.Db, P1Fixture.EmpresaId, Almacenista, Solicitud(f, salida, 3), default);
        await f.Db.SaveChangesAsync();

        await CorreccionConsumoApp.AutorizarAsync(f.Db, Mediador(f), c, P1Fixture.EmpresaId, new DateOnly(2026, 11, 11), default);

        var dev = await f.Db.Movimientos.SingleAsync(m => m.Tipo == TipoMovimiento.DevolucionSalida);
        dev.FechaMovimiento.Should().Be(new DateOnly(2026, 11, 11));
        var lineas = await AlfakHistorialAlmacenHandler.LineasKardex(f.Db, null, f.ArticuloId, null).ToListAsync();
        var (corrige, corregidaPor) = await KardexHandler.CorreccionesAsync(f.Db, lineas, default);
        KardexHandler.ConCorreccion(null, dev.Folio, corrige, corregidaPor).Should().Be($"Corrige {salida.Folio}");
        KardexHandler.ConCorreccion("Consumo", salida.Folio, corrige, corregidaPor).Should().Be($"Corregida por {dev.Folio} · Consumo");
    }

    private static async Task Error(Func<Task> act, string code) =>
        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(code);
}
