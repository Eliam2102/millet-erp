using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Cartera;

public class FacturaCarteraAggregateTests
{
    private static readonly DateTimeOffset Timbrado = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static FacturaCartera Crear(decimal total = 100_000m, Guid? clienteId = null) =>
        FacturaCartera.Crear(
            empresaId: Guid.NewGuid(),
            facturaVentaId: Guid.NewGuid(),
            clienteId: clienteId,
            receptorRfc: "vgl860910iu4",
            receptorNombre: "Vidrios del Golfo",
            uuid: Guid.NewGuid().ToString(),
            folio: "FV-123",
            total: total,
            moneda: "MXN",
            metodoPago: "PPD",
            fechaTimbrado: Timbrado,
            fechaVencimiento: Timbrado.AddDays(45));

    [Fact]
    public void Crear_normaliza_rfc_y_arranca_Abierta()
    {
        var f = Crear();

        f.Estado.Should().Be(EstadoFacturaCartera.Abierta);
        f.ReceptorRfc.Should().Be("VGL860910IU4");
        f.MontoPagado.Should().Be(0m);
        f.MontoNc.Should().Be(0m);
        f.SaldoPendiente.Should().Be(100_000m);
    }

    [Fact]
    public void Crear_rechaza_vencimiento_anterior_al_timbrado()
    {
        var act = () => FacturaCartera.Crear(
            Guid.NewGuid(), Guid.NewGuid(), null, "XAXX010101000", "Cliente", "uuid", "F-1",
            1000m, "MXN", "PUE", Timbrado, Timbrado.AddDays(-1));
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FC_VENCIMIENTO_INVALIDO");
    }

    [Fact]
    public void AplicarPago_parcial_deja_Parcial_y_total_deja_Pagada()
    {
        var f = Crear(total: 100m);

        f.AplicarPago(40m);
        f.Estado.Should().Be(EstadoFacturaCartera.Parcial);
        f.SaldoPendiente.Should().Be(60m);

        f.AplicarPago(60m);
        f.Estado.Should().Be(EstadoFacturaCartera.Pagada);
        f.SaldoPendiente.Should().Be(0m);
    }

    [Fact]
    public void AplicarPago_rechaza_sobrepago()
    {
        var f = Crear(total: 100m);
        f.AplicarPago(80m);

        var act = () => f.AplicarPago(30m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FC_SOBREPAGO");
    }

    [Fact]
    public void AplicarNotaCredito_reduce_saldo_y_combina_con_pagos()
    {
        var f = Crear(total: 100m);

        f.AplicarNotaCredito(25m);
        f.Estado.Should().Be(EstadoFacturaCartera.Parcial);

        f.AplicarPago(75m);
        f.Estado.Should().Be(EstadoFacturaCartera.Pagada);
        f.MontoNc.Should().Be(25m);
        f.MontoPagado.Should().Be(75m);
    }

    [Fact]
    public void RevertirPago_regresa_el_estado()
    {
        var f = Crear(total: 100m);
        f.AplicarPago(100m);
        f.Estado.Should().Be(EstadoFacturaCartera.Pagada);

        f.RevertirPago(100m);
        f.Estado.Should().Be(EstadoFacturaCartera.Abierta);
        f.MontoPagado.Should().Be(0m);
    }

    [Fact]
    public void RevertirPago_rechaza_mas_de_lo_pagado()
    {
        var f = Crear(total: 100m);
        f.AplicarPago(50m);

        var act = () => f.RevertirPago(60m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FC_REVERSA_INVALIDA");
    }

    [Fact]
    public void Cancelar_saca_la_factura_de_cobrable_y_bloquea_movimientos()
    {
        var f = Crear();
        f.Cancelar();

        f.Estado.Should().Be(EstadoFacturaCartera.Cancelada);
        var act = () => f.AplicarPago(10m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FC_CANCELADA");
    }

    [Fact]
    public void VincularCliente_solo_asigna_cliente_valido()
    {
        var f = Crear(clienteId: null);
        var clienteId = Guid.NewGuid();

        f.VincularCliente(clienteId);
        f.ClienteId.Should().Be(clienteId);

        var act = () => f.VincularCliente(Guid.Empty);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FC_CLIENTE_VACIO");
    }
}
