using System.Text.Json;
using Millet.Facturacion.Application.EventListeners;
using Millet.Tesoreria.Application.Integration;

namespace Millet.Facturacion.UnitTests.Integration;

/// <summary>
/// Test de CONTRATO del PR gemelo de TES-PR7 (patrón
/// <c>TesoreriaContratosCongeladosTests</c>): el evento
/// <c>tesoreria.pago-cliente.confirmado.v1</c> que Tesorería publica debe
/// ser byte-compatible con el espejo <see cref="PagoClienteConfirmadoPayload"/>
/// que Facturación deserializa para emitir el REPP. Round-trip real: se
/// serializa con las MISMAS opciones del outbox (PascalCase, sin indentar)
/// y se deserializa con las del listener (case-insensitive). Cambios
/// incompatibles bumpean a v2 en ambos lados.
/// </summary>
public sealed class TesoreriaPagoConfirmadoContratoTests
{
    private static readonly JsonSerializerOptions OutboxJson = new() { WriteIndented = false };
    private static readonly JsonSerializerOptions ListenerJson = new() { PropertyNameCaseInsensitive = true };

    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly DateTimeOffset OcurridoEn = new(2026, 7, 15, 18, 30, 0, TimeSpan.Zero);

    private static TDestino RoundTrip<TDestino>(object evento)
    {
        var json = JsonSerializer.Serialize(evento, evento.GetType(), OutboxJson);
        return JsonSerializer.Deserialize<TDestino>(json, ListenerJson)
            ?? throw new InvalidOperationException("Payload null tras round-trip.");
    }

    [Fact]
    public void PagoClienteConfirmado_v1_es_byte_compatible_con_el_espejo()
    {
        var facturaA = Guid.NewGuid();
        var facturaB = Guid.NewGuid();
        var evento = new PagoClienteConfirmadoIntegrationEvent(
            EmpresaId: EmpresaId,
            OcurridoEn: OcurridoEn,
            PropuestaId: Guid.NewGuid(),
            ClienteId: Guid.NewGuid(),
            MovimientoBancarioId: Guid.NewGuid(),
            CuentaBancariaId: Guid.NewGuid(),
            Monto: 7_500m,
            Moneda: "MXN",
            FechaValor: new DateOnly(2026, 7, 15),
            Referencia: "SPEI-00123",
            Facturas:
            [
                new PagoClienteFacturaAplicada(facturaA, 5_000m),
                new PagoClienteFacturaAplicada(facturaB, 2_500m),
            ]);

        var espejo = RoundTrip<PagoClienteConfirmadoPayload>(evento);

        espejo.Should().BeEquivalentTo(new PagoClienteConfirmadoPayload(
            evento.EmpresaId, evento.OcurridoEn, evento.PropuestaId, evento.ClienteId,
            evento.MovimientoBancarioId, evento.CuentaBancariaId, evento.Monto,
            evento.Moneda, evento.FechaValor, evento.Referencia,
            [
                new PagoClienteFacturaAplicadaPayload(facturaA, 5_000m),
                new PagoClienteFacturaAplicadaPayload(facturaB, 2_500m),
            ]));

        evento.EventType.Should().Be(PagoClienteConfirmadoPayload.EventType);
        evento.EventType.Should().Be(EmitirReppDesdePagoConfirmadoHandler.EventType);
    }

    [Fact]
    public void PagoClienteConfirmado_v1_con_opcionales_null_es_compatible()
    {
        var evento = new PagoClienteConfirmadoIntegrationEvent(
            EmpresaId, OcurridoEn, PropuestaId: null, Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), 1m, "USD", new DateOnly(2026, 7, 15), Referencia: null, Facturas: []);

        var espejo = RoundTrip<PagoClienteConfirmadoPayload>(evento);

        espejo.PropuestaId.Should().BeNull();
        espejo.Referencia.Should().BeNull();
        espejo.Facturas.Should().BeEmpty();
    }
}
