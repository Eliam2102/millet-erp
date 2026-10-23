using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Ambiente = Millet.CuentasPorPagar.Tests.P3.P3FacturaConOcTests.Ambiente;

namespace Millet.CuentasPorPagar.UnitTests.EventListeners;

/// <summary>86e3tabdp: CxP consume la cancelación de una recepción (sin nota de cargo).</summary>
public sealed class OcRecepcionCanceladaHandlerTests
{
    private static OcRecepcionCanceladaHandler Handler(Ambiente a) =>
        new(a.Db, a, new(a.Eventos, a.Db, a.Elegibilidad), NullLogger<OcRecepcionCanceladaHandler>.Instance);

    private static OcRecepcionCanceladaCommand Cancelar(Ambiente a, OcRecepcionRegistradaCommand rec, decimal cantidad, Guid? eventoId = null) =>
        new(eventoId ?? Guid.NewGuid(), new(a.Current!.Value, a.Ahora, rec.Payload.RecepcionId, rec.Payload.FolioRecepcion,
            rec.Payload.OrdenCompraId, [new(rec.Payload.Lineas[0].LineaOcId, cantidad)]));

    [Fact]
    public async Task Borra_la_proyeccion_desliga_la_factura_y_no_genera_nota_de_cargo_ni_se_repite()
    {
        await using var a = new Ambiente(recibido: 5);
        var rec = a.Recepcion(5);
        await a.Recepciones.Handle(rec, default);
        var captura = await a.Handler.Handle(a.Command(4, 20) with { RecepcionId = rec.Payload.RecepcionId }, default);
        var cmd = Cancelar(a, rec, 5);

        await Handler(a).Handle(cmd, default);

        (await a.Db.RecepcionesOcLocal.CountAsync(r => r.RecepcionId == rec.Payload.RecepcionId)).Should().Be(0);
        (await a.Db.FacturasProveedor.SingleAsync(f => f.Id == captura.Id)).RecepcionId.Should().BeNull();
        (await a.Db.NotasCargo.CountAsync()).Should().Be(0);
        (await a.Db.EventosProcesados.CountAsync(e => e.EventoId == cmd.EventId && e.EventoTipo == OcRecepcionCanceladaHandler.EventType)).Should().Be(1);

        await Handler(a).Handle(cmd, default); // re-entrega: dedupe, sin errores ni efectos
        (await a.Db.EventosProcesados.CountAsync(e => e.EventoId == cmd.EventId)).Should().Be(1);
    }

    [Fact]
    public async Task Factura_autorizada_republica_el_pasivo_sin_lo_recibido_de_la_recepcion_cancelada()
    {
        await using var a = new Ambiente(recibido: 8); // OC 10 x 20; Compras ve 8 recibidos
        var captura = await a.Handler.Handle(a.Command(10, 20), default);
        var factura = await a.Db.FacturasProveedor.SingleAsync(f => f.Id == captura.Id);
        factura.Autorizar(null, a.Ahora);
        await a.Db.SaveChangesAsync();
        var rec = a.Recepcion(3);
        await a.Recepciones.Handle(rec, default);
        a.Eventos.Integraciones.Clear();

        await Handler(a).Handle(Cancelar(a, rec, 3), default);

        // Elegible antes: 8 x 20 = 160. Quitando las 3 de la recepción cancelada: 5 x 20 = 100.
        a.Eventos.Integraciones.OfType<PasivoAutorizadoParaPagoIntegrationEvent>().Single().SaldoPendiente.Should().Be(100);
    }

    [Fact]
    public async Task Desligar_recepcion_es_idempotente_y_no_toca_otra_recepcion()
    {
        await using var a = new Ambiente();
        var rec = a.Recepcion(5);
        await a.Recepciones.Handle(rec, default);
        var captura = await a.Handler.Handle(a.Command(4, 20) with { RecepcionId = rec.Payload.RecepcionId }, default);
        var factura = await a.Db.FacturasProveedor.SingleAsync(f => f.Id == captura.Id);
        var original = factura.RecepcionId;

        factura.DesligarRecepcion(Guid.NewGuid());
        factura.RecepcionId.Should().Be(original);

        factura.DesligarRecepcion(original!.Value);
        factura.DesligarRecepcion(original.Value);
        factura.RecepcionId.Should().BeNull();
    }
}
