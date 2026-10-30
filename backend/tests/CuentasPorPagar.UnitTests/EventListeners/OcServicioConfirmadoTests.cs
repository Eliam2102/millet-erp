using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Millet.CuentasPorPagar.Application.EventListeners;
using Millet.CuentasPorPagar.Application.Integration;
using Ambiente = Millet.CuentasPorPagar.Tests.P3.P3FacturaConOcTests.Ambiente;

namespace Millet.CuentasPorPagar.UnitTests.EventListeners;

/// <summary>86e3tacbn: las líneas de servicio solo cuentan como recibidas con la OC confirmada por el área solicitante.</summary>
public sealed class OcServicioConfirmadoTests
{
    private static async Task<Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor> FacturaAutorizadaAsync(Ambiente a)
    {
        var captura = await a.Handler.Handle(a.Command(10, 20), default); // OC 10 x 20 = 200
        var factura = await a.Db.FacturasProveedor.SingleAsync(f => f.Id == captura.Id);
        factura.Autorizar(null, a.Ahora);
        await a.Db.SaveChangesAsync();
        return factura;
    }

    private static OcServicioConfirmadoHandler Handler(Ambiente a) =>
        new(a.Db, a, new(a.Eventos, a.Db, a.Elegibilidad), NullLogger<OcServicioConfirmadoHandler>.Instance);

    private static OcServicioConfirmadoCommand Evento(Ambiente a, Guid ocId, bool confirmado, Guid? eventoId = null) =>
        new(eventoId ?? Guid.NewGuid(), new(a.Current!.Value, a.Ahora, ocId, "OC-P3-FICTICIA", confirmado, Guid.NewGuid(), null));

    [Fact]
    public async Task Servicio_sin_confirmar_no_es_elegible_aunque_Compras_reporte_recibido()
    {
        await using var a = new Ambiente(recibido: 10);
        var factura = await FacturaAutorizadaAsync(a);
        a.ConfigurarServicio(confirmado: false);

        var r = await a.Elegibilidad.CalcularAsync(factura, default);

        r.ElegibleTotal.Should().Be(0);
        r.ElegiblePendiente.Should().Be(0);
    }

    [Fact]
    public async Task Oc_mixta_con_servicio_sin_confirmar_solo_el_material_es_elegible()
    {
        await using var a = new Ambiente(cantidadOc: 20); // total de la OC con IVA: 464
        a.ConfigurarMixta(confirmado: false);
        var cmd = a.Command(10, 20, total: 464, iva: 64);
        var material = cmd.Lineas[0];
        var captura = await a.Handler.Handle(cmd with { Subtotal = 400, Lineas = [material, material with { LineaOcId = a.LineaServicioId }] }, default);
        captura.Estado.Should().NotBe(Millet.CuentasPorPagar.Domain.FacturaProveedor.EstadoPasivo.Cancelada, captura.MotivoCancelacion?.ToString());
        var factura = await a.Db.FacturasProveedor.SingleAsync(f => f.Id == captura.Id);

        var r = await a.Elegibilidad.CalcularAsync(factura, default);
        r.ElegibleTotal.Should().Be(232); // mitad del neto: solo el material

        a.ConfigurarMixta(confirmado: true);
        (await a.Elegibilidad.CalcularAsync(factura, default)).ElegibleTotal.Should().Be(464);
    }

    [Fact]
    public async Task Servicio_confirmado_es_elegible_completo()
    {
        await using var a = new Ambiente(recibido: 0);
        var factura = await FacturaAutorizadaAsync(a);
        a.ConfigurarServicio(confirmado: true);

        var r = await a.Elegibilidad.CalcularAsync(factura, default);

        r.ElegibleTotal.Should().Be(200);
        r.ElegiblePendiente.Should().Be(200);
    }

    [Fact]
    public async Task Evento_republica_el_pasivo_con_la_elegibilidad_nueva_y_es_idempotente()
    {
        await using var a = new Ambiente(recibido: 0);
        var factura = await FacturaAutorizadaAsync(a);
        a.ConfigurarServicio(confirmado: true);
        a.Eventos.Integraciones.Clear();
        var cmd = Evento(a, factura.OrdenCompraId!.Value, confirmado: true);

        await Handler(a).Handle(cmd, default);
        a.Eventos.Integraciones.OfType<PasivoAutorizadoParaPagoIntegrationEvent>().Single().SaldoPendiente.Should().Be(200);

        a.Eventos.Integraciones.Clear();
        await Handler(a).Handle(cmd, default); // re-entrega: dedupe
        a.Eventos.Integraciones.Should().BeEmpty();
        (await a.Db.EventosProcesados.CountAsync(e => e.EventoId == cmd.EventId && e.EventoTipo == OcServicioConfirmadoHandler.EventType)).Should().Be(1);
    }

    [Fact]
    public async Task Al_deshacer_la_confirmacion_el_pasivo_vuelve_a_cero()
    {
        await using var a = new Ambiente(recibido: 0);
        var factura = await FacturaAutorizadaAsync(a);
        a.ConfigurarServicio(confirmado: false);
        a.Eventos.Integraciones.Clear();

        await Handler(a).Handle(Evento(a, factura.OrdenCompraId!.Value, confirmado: false), default);

        a.Eventos.Integraciones.OfType<PasivoAutorizadoParaPagoIntegrationEvent>().Single().SaldoPendiente.Should().Be(0);
    }
}
