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

    private static OcRecepcionCanceladaCommand Cancelar(Ambiente a, OcRecepcionRegistradaCommand rec, decimal cantidad,
        decimal? recibidoVigente = null, Guid? eventoId = null) =>
        new(eventoId ?? Guid.NewGuid(), new(a.Current!.Value, a.Ahora, rec.Payload.RecepcionId, rec.Payload.FolioRecepcion,
            rec.Payload.OrdenCompraId, [new(rec.Payload.Lineas[0].LineaOcId, cantidad, recibidoVigente)]));

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

    // OC 10 x 20. Almacén cancela una recepción de 3 y deja 5 recibidos vigentes (el pasivo elegible es 5 x 20 = 100).
    // Compras reporta 8 si aún no descontó la cancelación y 5 si ya la descontó: el resultado no debe cambiar.
    [Theory]
    [InlineData(8)] // Compras todavía no procesó su evento
    [InlineData(5)] // Compras ya descontó la cancelación (antes se restaba otra vez y salía 40)
    public async Task Factura_autorizada_republica_el_pasivo_igual_haya_o_no_descontado_Compras(int recibidoEnCompras)
    {
        await using var a = new Ambiente(recibido: recibidoEnCompras);
        var captura = await a.Handler.Handle(a.Command(10, 20), default);
        var factura = await a.Db.FacturasProveedor.SingleAsync(f => f.Id == captura.Id);
        factura.Autorizar(null, a.Ahora);
        await a.Db.SaveChangesAsync();
        var rec = a.Recepcion(3);
        await a.Recepciones.Handle(rec, default);
        a.Eventos.Integraciones.Clear();

        await Handler(a).Handle(Cancelar(a, rec, 3, recibidoVigente: 5), default);

        a.Eventos.Integraciones.OfType<PasivoAutorizadoParaPagoIntegrationEvent>().Single().SaldoPendiente.Should().Be(100);
    }

    [Fact]
    public async Task Recepcion_correcta_con_el_mismo_CFDI_se_liga_a_la_factura_autorizada_que_quedo_sin_recepcion()
    {
        await using var a = new Ambiente(); // OC 10 x 20, Compras ve 10 recibidos
        var mala = a.Recepcion(10);
        await a.Recepciones.Handle(mala, default);
        var cfdi = await a.CfdiAsync(200, 0, 0, 200);
        var captura = await a.Handler.Handle(a.Command(10, 20, 200) with
        { CfdiRecibidoId = cfdi.Id, UuidCfdi = cfdi.UuidCfdi.Valor, RecepcionId = mala.Payload.RecepcionId }, default);
        var factura = await a.Db.FacturasProveedor.SingleAsync(f => f.Id == captura.Id);
        factura.Autorizar(null, a.Ahora);
        await a.Db.SaveChangesAsync();

        await Handler(a).Handle(Cancelar(a, mala, 10, recibidoVigente: 0), default);
        factura.RecepcionId.Should().BeNull();
        var lector = new Millet.CuentasPorPagar.Infrastructure.PublicAdapters.CxpDocumentosReadAdapter(a.Db, a);
        (await lector.ObtenerCfdiAsync(cfdi.Id, default))!.OcDeFacturaSinRecepcionId.Should().Be(mala.Payload.OrdenCompraId);

        var correcta = a.Recepcion(10);
        correcta = correcta with { Payload = correcta.Payload with { CfdiRecibidoId = cfdi.Id } };
        a.Eventos.Integraciones.Clear();
        await a.Recepciones.Handle(correcta, default);

        factura.RecepcionId.Should().Be(correcta.Payload.RecepcionId);
        (await lector.ObtenerCfdiAsync(cfdi.Id, default))!.OcDeFacturaSinRecepcionId.Should().BeNull();
        a.Eventos.Integraciones.OfType<PasivoAutorizadoParaPagoIntegrationEvent>().Single().SaldoPendiente.Should().Be(200);
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
