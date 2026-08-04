using System.Text.Json;
using Millet.CuentasPorCobrar.Application.Integration;
using Millet.CuentasPorPagar.Application.Integration;
using Millet.Facturacion.Application.Integration;
using Millet.Tesoreria.Application.EventListeners;

namespace Millet.Tesoreria.UnitTests.Integration;

/// <summary>
/// Test de CONTRATO de los eventos que Tesorería CONSUME (TES-PR7,
/// cuidados-infra §2.2): los records espejo de
/// <c>ContratosEspejoCxc.cs</c> / <c>ContratosEspejoFacturacion.cs</c>
/// deben deserializar lo que CxC y Facturación publican realmente.
/// Round-trip real: se serializa el evento del publisher con las MISMAS
/// opciones del outbox (PascalCase, sin indentar) y se deserializa con
/// las MISMAS opciones de los listeners de Tesorería (case-insensitive).
/// Los espejos de Facturación son SUBCONJUNTOS deliberados — solo se
/// asevera lo consumido.
/// </summary>
public sealed class TesoreriaContratosConsumidosTests
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
    public void PropuestaCreada_v1_es_compatible_con_el_espejo()
    {
        var facturaA = Guid.NewGuid();
        var facturaB = Guid.NewGuid();
        var evento = new PropuestaAplicacionPagoCreadaEvent(
            EmpresaId: EmpresaId,
            OcurridoEn: OcurridoEn,
            PropuestaId: Guid.NewGuid(),
            ClienteId: Guid.NewGuid(),
            DepositoRef: "DEP-2026-071",
            MontoDeposito: 7_500m,
            Moneda: "MXN",
            AjusteNoFiscal: -10m,
            NumeroFacturas: 2,
            Facturas:
            [
                new PropuestaAplicacionFacturaDetalle(facturaA, "VEN-101", 5_000m),
                new PropuestaAplicacionFacturaDetalle(facturaB, "VEN-102", 2_510m),
            ]);

        var espejo = RoundTrip<PropuestaAplicacionCreadaPayload>(evento);

        espejo.Should().BeEquivalentTo(new PropuestaAplicacionCreadaPayload(
            evento.EmpresaId, evento.OcurridoEn, evento.PropuestaId, evento.ClienteId,
            evento.DepositoRef, evento.MontoDeposito, evento.Moneda, evento.AjusteNoFiscal,
            evento.NumeroFacturas,
            [
                new PropuestaFacturaPayload(facturaA, "VEN-101", 5_000m),
                new PropuestaFacturaPayload(facturaB, "VEN-102", 2_510m),
            ]));

        evento.EventType.Should().Be(PropuestaAplicacionCreadaPayload.EventType);
    }

    [Fact]
    public void PropuestaCreada_v1_sin_facturas_pre_extension_deserializa_null()
    {
        // Simula un evento publicado ANTES de la extensión aditiva.
        var json = JsonSerializer.Serialize(new
        {
            EmpresaId,
            OcurridoEn,
            PropuestaId = Guid.NewGuid(),
            ClienteId = Guid.NewGuid(),
            DepositoRef = "DEP-VIEJO",
            MontoDeposito = 100m,
            Moneda = "MXN",
            AjusteNoFiscal = 0m,
            NumeroFacturas = 1,
        }, OutboxJson);

        var espejo = JsonSerializer.Deserialize<PropuestaAplicacionCreadaPayload>(json, ListenerJson)!;

        espejo.Facturas.Should().BeNull();
        espejo.MontoDeposito.Should().Be(100m);
    }

    [Fact]
    public void PasivoAutorizado_v1_con_MetodoPago_es_compatible_con_el_espejo()
    {
        // TES-PR8 [T-G11, decisión (a)]: extensión ADITIVA del evento de CxP.
        var evento = new PasivoAutorizadoParaPagoIntegrationEvent(
            EmpresaId: EmpresaId,
            OcurridoEn: OcurridoEn,
            FacturaProveedorId: Guid.NewGuid(),
            ProveedorId: Guid.NewGuid(),
            OrdenCompraId: Guid.NewGuid(),
            MontoTotal: 10_000m,
            SaldoPendiente: 10_000m,
            Moneda: "MXN",
            TipoCambio: null,
            FechaVencimiento: new DateOnly(2026, 8, 1),
            UuidCfdi: "3fa85f64-5717-4562-b3fc-2c963f66afa6",
            FolioProveedor: "F-001",
            MetodoPago: "PPD");

        var espejo = RoundTrip<PasivoAutorizadoParaPagoPayload>(evento);

        espejo.Should().BeEquivalentTo(new PasivoAutorizadoParaPagoPayload(
            evento.EmpresaId, evento.OcurridoEn, evento.FacturaProveedorId,
            evento.ProveedorId, evento.OrdenCompraId, evento.MontoTotal,
            evento.SaldoPendiente, evento.Moneda, evento.TipoCambio,
            evento.FechaVencimiento, evento.UuidCfdi, evento.FolioProveedor,
            MetodoPago: "PPD"));

        evento.EventType.Should().Be(PasivoAutorizadoParaPagoPayload.EventType);
    }

    [Fact]
    public void PasivoAutorizado_v1_pre_extension_deserializa_MetodoPago_null()
    {
        // Evento publicado ANTES de la extensión aditiva (sin el campo).
        var json = JsonSerializer.Serialize(new
        {
            EmpresaId,
            OcurridoEn,
            FacturaProveedorId = Guid.NewGuid(),
            ProveedorId = Guid.NewGuid(),
            MontoTotal = 100m,
            SaldoPendiente = 100m,
            Moneda = "MXN",
            FechaVencimiento = new DateOnly(2026, 8, 1),
        }, OutboxJson);

        var espejo = JsonSerializer.Deserialize<PasivoAutorizadoParaPagoPayload>(json, ListenerJson)!;

        espejo.MetodoPago.Should().BeNull();
        espejo.SaldoPendiente.Should().Be(100m);
    }

    [Fact]
    public void CajaSesionCerrada_v1_es_compatible_con_el_espejo_subset()
    {
        var evento = new CajaSesionCerradaIntegrationEvent(
            EmpresaId: EmpresaId,
            OcurridoEn: OcurridoEn,
            CajaSesionId: Guid.NewGuid(),
            CajaId: Guid.NewGuid(),
            SucursalId: Guid.NewGuid(),
            ResponsableUsuarioId: Guid.NewGuid(),
            DiaOperacion: new DateOnly(2026, 7, 14),
            FondoApertura: 2_000m,
            EfectivoTeorico: 14_500m,
            EfectivoDeclarado: 14_480m,
            Diferencia: -20m,
            CierreExtemporaneo: false,
            Cortes: [new CajaSesionCorteTotal("Efectivo", 12_480m, 12_480m)]);

        var espejo = RoundTrip<CajaSesionCerradaPayload>(evento);

        espejo.Should().BeEquivalentTo(new CajaSesionCerradaPayload(
            evento.EmpresaId, evento.OcurridoEn, evento.CajaSesionId, evento.CajaId,
            evento.SucursalId, evento.DiaOperacion, evento.EfectivoDeclarado));

        evento.EventType.Should().Be(CajaSesionCerradaPayload.EventType);
    }

    [Fact]
    public void ReciboPagoTimbrado_v1_es_compatible_con_el_espejo_subset()
    {
        var factura = Guid.NewGuid();
        var evento = new ReciboPagoTimbradoIntegrationEvent(
            EmpresaId: EmpresaId,
            OcurridoEn: OcurridoEn,
            ReciboPagoId: Guid.NewGuid(),
            Uuid: "3FA85F64-5717-4562-B3FC-2C963F66AFA6",
            ImporteTotalPago: 5_000m,
            GananciaPerdidaCambiaria: 0m,
            FacturasPagadas: [new ReppFacturaPagadaDetalle(factura, 5_000m, 1, "MXN", 7_000m)]);

        var espejo = RoundTrip<ReciboPagoTimbradoPayload>(evento);

        espejo.Should().BeEquivalentTo(new ReciboPagoTimbradoPayload(
            evento.EmpresaId, evento.OcurridoEn, evento.ReciboPagoId, evento.Uuid,
            evento.ImporteTotalPago,
            [new ReppFacturaPagadaPayload(factura, 5_000m)]));

        evento.EventType.Should().Be(ReciboPagoTimbradoPayload.EventType);
    }
}
