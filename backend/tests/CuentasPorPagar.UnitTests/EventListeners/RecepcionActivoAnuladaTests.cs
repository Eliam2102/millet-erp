using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Application.FacturaProveedor.Queries;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.CuentasPorPagar.Infrastructure.Almacen;
using Millet.CuentasPorPagar.Tests.P3;

namespace Millet.CuentasPorPagar.UnitTests.EventListeners;

public sealed class RecepcionActivoAnuladaTests
{
    private static RecepcionActivoAnuladaCommand Anular(P3FacturaConOcTests.Ambiente a, OcRecepcionRegistradaCommand rec, Guid? eventId = null) =>
        new(eventId ?? Guid.NewGuid(), new(a.Current!.Value, a.Ahora, Guid.NewGuid(), rec.Payload.RecepcionId,
            rec.Payload.OrdenCompraId, a.LineaId, 5, "Error de captura"));

    private static RecepcionActivoAnuladaHandler Handler(P3FacturaConOcTests.Ambiente a) =>
        new(a.Db, a, NullLogger<RecepcionActivoAnuladaHandler>.Instance);

    [Fact]
    public async Task Consumidor_es_idempotente_y_marca_la_linea_una_sola_vez()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();
        var rec = a.Recepcion(5);
        await a.Recepciones.Handle(rec, default);
        var cmd = Anular(a, rec);

        await Handler(a).Handle(cmd, default);
        await Handler(a).Handle(cmd, default); // re-entrega del mismo evento
        await Handler(a).Handle(Anular(a, rec), default); // otro evento, misma línea

        (await a.Db.RecepcionesOcLocal.SingleAsync()).LineasOcAnuladas.Should().Equal(a.LineaId);
        (await a.Db.EventosProcesados.CountAsync(e => e.EventoTipo == RecepcionActivoAnuladaHandler.EventType)).Should().Be(2);
    }

    [Fact]
    public async Task Recepcion_no_proyectada_lanza_para_reintento_sin_marcar_el_evento()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();
        var act = () => Handler(a).Handle(Anular(a, a.Recepcion(5)), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await a.Db.EventosProcesados.CountAsync(e => e.EventoTipo == RecepcionActivoAnuladaHandler.EventType)).Should().Be(0);
    }

    [Fact]
    public async Task Recepcion_anulada_no_se_ofrece_en_pendientes_ni_en_el_puerto_de_lectura()
    {
        await using var a = new P3FacturaConOcTests.Ambiente();
        var anulada = a.Recepcion(5);
        var vigente = a.Recepcion(3);
        await a.Recepciones.Handle(anulada, default);
        await a.Recepciones.Handle(vigente, default);
        await Handler(a).Handle(Anular(a, anulada), default);
        var oc = await ((IComprasOcReadPort)a).ObtenerAsync(anulada.Payload.OrdenCompraId, default);

        var pendientes = await new PendientesFacturarProveedorHandler(a.Db, a, a).Handle(new(null, oc!.ProveedorId), default);
        pendientes.Ocs.Single().Recepciones.Select(r => r.RecepcionId).Should().Equal(vigente.Payload.RecepcionId);

        var lectura = await new AlmacenRecepcionReadPortAdapter(a.Db)
            .ObtenerRecepcionDeOcAsync(anulada.Payload.OrdenCompraId, default);
        lectura!.RecepcionId.Should().Be(vigente.Payload.RecepcionId);
    }

    [Fact]
    public async Task Cantidad_anulada_no_cuenta_como_recibido_local_en_la_elegibilidad()
    {
        await using var a = new P3FacturaConOcTests.Ambiente(recibido: 0);
        var respuesta = await a.Handler.Handle(a.Command(5, 20), default);
        var factura = await a.Db.FacturasProveedor.SingleAsync(f => f.Id == respuesta.Id);
        var rec = a.Recepcion(5);
        await a.Recepciones.Handle(rec, default);
        factura.Autorizar(null, a.Ahora);
        await a.Db.SaveChangesAsync();
        await Handler(a).Handle(Anular(a, rec), default);

        var e = await a.Elegibilidad.CalcularAsync(factura, default, considerarRecepcionesLocales: true);

        e.ElegiblePendiente.Should().Be(0);
    }
}
