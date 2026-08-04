using System.Text.Json;
using Millet.CuentasPorPagar.Application.EventListeners;
using CxpHandlers = Millet.CuentasPorPagar.Application.EventListeners.Tesoreria;
using Millet.Tesoreria.Application.Integration;

namespace Millet.Tesoreria.UnitTests.Integration;

/// <summary>
/// Test de CONTRATO CONGELADO (TES-PR4, obligatorio por cuidados-infra
/// §2.2): los 4 eventos que Tesorería publica deben ser byte-compatibles
/// con los records espejo que CxP deserializa
/// (<c>ContratosEspejo.cs:97-141</c>). Round-trip real: se serializa el
/// evento con las MISMAS opciones del outbox (PascalCase, sin indentar) y
/// se deserializa con las MISMAS opciones del listener de CxP
/// (case-insensitive). Si cualquiera de los dos lados cambia nombres o
/// tipos, esto rompe en CI antes de divergir en runtime.
/// </summary>
public sealed class TesoreriaContratosCongeladosTests
{
    // Espejo de OutboxSaveChangesInterceptor.JsonOptions (PascalCase default).
    private static readonly JsonSerializerOptions OutboxJson = new() { WriteIndented = false };

    // Espejo de TesoreriaEventListenerWorker.JsonOpts en CxP.
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
    public void Aplicado_v1_es_byte_compatible_con_el_espejo_de_CxP()
    {
        var evento = new PagoFacturaProveedorAplicadoIntegrationEvent(
            EmpresaId: EmpresaId,
            OcurridoEn: OcurridoEn,
            FacturaProveedorId: Guid.NewGuid(),
            PagoId: Guid.NewGuid(),
            Monto: 12_345.67m,
            Moneda: "MXN",
            FechaPago: new DateOnly(2026, 7, 15),
            MetodoPago: "PPD",
            ReferenciaBancaria: "SPEI-00123");

        var espejo = RoundTrip<PagoFacturaProveedorPayload>(evento);

        espejo.Should().BeEquivalentTo(new PagoFacturaProveedorPayload(
            evento.EmpresaId, evento.OcurridoEn, evento.FacturaProveedorId,
            evento.PagoId, evento.Monto, evento.Moneda, evento.FechaPago,
            evento.MetodoPago, evento.ReferenciaBancaria));

        evento.EventType.Should().Be(CxpHandlers.PagoFacturaProveedorAplicadoHandler.EventType);
    }

    [Fact]
    public void Aplicado_v1_con_opcionales_null_es_compatible()
    {
        var evento = new PagoFacturaProveedorAplicadoIntegrationEvent(
            EmpresaId, OcurridoEn, Guid.NewGuid(), Guid.NewGuid(),
            1m, "USD", new DateOnly(2026, 7, 15), MetodoPago: null, ReferenciaBancaria: null);

        var espejo = RoundTrip<PagoFacturaProveedorPayload>(evento);

        espejo.MetodoPago.Should().BeNull();
        espejo.ReferenciaBancaria.Should().BeNull();
        espejo.Monto.Should().Be(1m);
    }

    [Fact]
    public void Revertido_v1_es_byte_compatible_con_el_espejo_de_CxP()
    {
        var evento = new PagoFacturaProveedorRevertidoIntegrationEvent(
            EmpresaId: EmpresaId,
            OcurridoEn: OcurridoEn,
            FacturaProveedorId: Guid.NewGuid(),
            PagoOriginalId: Guid.NewGuid(),
            MontoRevertido: 4_000m,
            Moneda: "MXN",
            FechaReversa: new DateOnly(2026, 7, 16),
            Motivo: "Transferencia rechazada por el banco");

        var espejo = RoundTrip<PagoFacturaProveedorRevertidoPayload>(evento);

        espejo.Should().BeEquivalentTo(new PagoFacturaProveedorRevertidoPayload(
            evento.EmpresaId, evento.OcurridoEn, evento.FacturaProveedorId,
            evento.PagoOriginalId, evento.MontoRevertido, evento.Moneda,
            evento.FechaReversa, evento.Motivo));

        evento.EventType.Should().Be(CxpHandlers.PagoFacturaProveedorRevertidoHandler.EventType);
    }

    [Fact]
    public void ReppRecibido_v1_es_byte_compatible_con_el_espejo_de_CxP()
    {
        var evento = new ReppProveedorRecibidoIntegrationEvent(
            EmpresaId: EmpresaId,
            OcurridoEn: OcurridoEn,
            FacturaProveedorId: Guid.NewGuid(),
            UuidComplementoPago: "3FA85F64-5717-4562-B3FC-2C963F66AFA6",
            FechaComplemento: new DateTimeOffset(2026, 7, 20, 0, 0, 0, TimeSpan.Zero));

        var espejo = RoundTrip<ReppProveedorRecibidoPayload>(evento);

        espejo.Should().BeEquivalentTo(new ReppProveedorRecibidoPayload(
            evento.EmpresaId, evento.OcurridoEn, evento.FacturaProveedorId,
            evento.UuidComplementoPago, evento.FechaComplemento));

        evento.EventType.Should().Be(CxpHandlers.ReppProveedorRecibidoHandler.EventType);
    }

    [Fact]
    public void CancelacionSolicitada_v1_es_byte_compatible_con_el_espejo_de_CxP()
    {
        var evento = new CancelacionPasivoSolicitadaIntegrationEvent(
            EmpresaId: EmpresaId,
            OcurridoEn: OcurridoEn,
            FacturaProveedorId: Guid.NewGuid(),
            UsuarioSolicitanteId: Guid.NewGuid(),
            Motivo: "Factura duplicada — el proveedor emitirá NC");

        var espejo = RoundTrip<CancelacionPasivoSolicitadaPayload>(evento);

        espejo.Should().BeEquivalentTo(new CancelacionPasivoSolicitadaPayload(
            evento.EmpresaId, evento.OcurridoEn, evento.FacturaProveedorId,
            evento.UsuarioSolicitanteId, evento.Motivo));

        evento.EventType.Should().Be(CxpHandlers.CancelacionPasivoSolicitadaHandler.EventType);
    }
}
