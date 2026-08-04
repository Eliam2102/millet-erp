using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

/// <summary>
/// Tests del dominio para AplicarNotaCredito y AplicarAnticipo sobre
/// FacturaProveedor + AplicarMonto sobre NotaCreditoProveedor (F6-PR2).
/// </summary>
public sealed class AplicarNcYAnticipoTests
{
    private static Domain.FacturaProveedor.FacturaProveedor CapturarFactura(decimal total = 1160m) =>
        Domain.FacturaProveedor.FacturaProveedor.CapturarConOc(
            empresaId: Guid.NewGuid(),
            cfdiRecibidoId: null,
            uuidCfdi: null,
            proveedorId: Guid.NewGuid(),
            sucursalId: Guid.NewGuid(),
            folioProveedor: "F-001", serieProveedor: "A",
            fechaDocumento: DateTimeOffset.UtcNow,
            fechaContabilizacion: DateTimeOffset.UtcNow,
            fechaVencimiento: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            moneda: "MXN", tipoCambio: null,
            subtotal: 1000m, descuentos: 0m,
            impuestosTrasladados: 160m, retenciones: 0m, total: total,
            ordenCompraId: Guid.NewGuid(),
            encargadoComprasSnapshot: null,
            tolerancia: Tolerancia.MontoAbsoluto(0.99m),
            diferenciaContraOc: 0m,
            redondeoAplicado: 0m,
            ahora: DateTimeOffset.UtcNow);

    private static Domain.NotaCreditoProveedor.NotaCreditoProveedor CapturarNc(Guid? facturaOrigenId, decimal total = 500m) =>
        Domain.NotaCreditoProveedor.NotaCreditoProveedor.Capturar(
            empresaId: Guid.NewGuid(),
            cfdiRecibidoId: Guid.NewGuid(),
            uuidCfdi: "5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B",
            proveedorId: Guid.NewGuid(),
            folioProveedor: "NC-001", serieProveedor: "A",
            fechaCfdi: DateTimeOffset.UtcNow,
            moneda: "MXN", tipoCambio: null,
            subtotal: total / 1.16m, impuestosTrasladados: total - (total / 1.16m), retenciones: 0m, total: total,
            tipo: TipoNotaCredito.Descuento,
            tipoRelacionCfdi: TipoRelacionCfdi.NotaCredito,
            uuidRelacionCfdi: "11111111-2222-3333-4444-555555555555",
            facturaOrigenId: facturaOrigenId,
            capturadoPor: Guid.NewGuid(),
            ahora: DateTimeOffset.UtcNow);

    // -------- FacturaProveedor.AplicarNotaCredito --------

    [Fact]
    public void AplicarNotaCredito_decrementa_saldo_pendiente()
    {
        var f = CapturarFactura(total: 1160m);
        f.AplicarNotaCredito(300m);

        f.NcAplicadasTotal.Should().Be(300m);
        f.SaldoPendiente.Should().Be(860m);
    }

    [Fact]
    public void AplicarNotaCredito_acumula_aplicaciones_sucesivas()
    {
        var f = CapturarFactura(total: 1160m);
        f.AplicarNotaCredito(300m);
        f.AplicarNotaCredito(200m);
        f.NcAplicadasTotal.Should().Be(500m);
        f.SaldoPendiente.Should().Be(660m);
    }

    [Fact]
    public void AplicarNotaCredito_no_puede_dejar_saldo_negativo()
    {
        var f = CapturarFactura(total: 100m);
        var act = () => f.AplicarNotaCredito(101m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "APLICACION_EXCEDE_SALDO");
    }

    [Fact]
    public void AplicarNotaCredito_rechaza_monto_no_positivo()
    {
        var f = CapturarFactura();
        var act = () => f.AplicarNotaCredito(0m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "APLICACION_MONTO_INVALIDO");
    }

    [Fact]
    public void AplicarNotaCredito_a_factura_cancelada_no_permitido()
    {
        var f = CapturarFactura();
        f.Cancelar(MotivoCancelacion.ErrorCaptura, texto: null, usuarioId: null, ahora: DateTimeOffset.UtcNow);
        var act = () => f.AplicarNotaCredito(100m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_NO_ACEPTA_NC");
    }

    // -------- FacturaProveedor.AplicarAnticipo --------

    [Fact]
    public void AplicarAnticipo_decrementa_saldo_pendiente()
    {
        var f = CapturarFactura(total: 1160m);
        f.AplicarAnticipo(400m);
        f.AnticipoAplicadoTotal.Should().Be(400m);
        f.SaldoPendiente.Should().Be(760m);
    }

    [Fact]
    public void AplicarAnticipo_no_puede_dejar_saldo_negativo()
    {
        var f = CapturarFactura(total: 100m);
        var act = () => f.AplicarAnticipo(101m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "APLICACION_EXCEDE_SALDO");
    }

    [Fact]
    public void AplicarAnticipo_y_AplicarNc_se_combinan_correctamente()
    {
        var f = CapturarFactura(total: 1000m);
        f.AplicarAnticipo(300m);
        f.AplicarNotaCredito(200m);
        f.SaldoPendiente.Should().Be(500m);
    }

    // -------- NotaCreditoProveedor.AplicarMonto --------

    [Fact]
    public void NotaCreditoProveedor_AplicarMonto_parcial_no_cambia_estado()
    {
        var nc = CapturarNc(facturaOrigenId: Guid.NewGuid(), total: 500m);
        nc.AplicarMonto(200m);
        nc.MontoAplicado.Should().Be(200m);
        nc.SaldoPorAplicar.Should().Be(300m);
        nc.Estado.Should().Be(EstadoNotaCredito.Abierta);
    }

    [Fact]
    public void NotaCreditoProveedor_AplicarMonto_total_pasa_a_Aplicada()
    {
        var nc = CapturarNc(facturaOrigenId: Guid.NewGuid(), total: 500m);
        nc.AplicarMonto(500m);
        nc.SaldoPorAplicar.Should().Be(0m);
        nc.Estado.Should().Be(EstadoNotaCredito.Aplicada);
    }

    [Fact]
    public void NotaCreditoProveedor_AplicarMonto_exceso_saldo_lanza()
    {
        var nc = CapturarNc(facturaOrigenId: Guid.NewGuid(), total: 500m);
        var act = () => nc.AplicarMonto(501m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NC_SALDO_INSUFICIENTE");
    }

    [Fact]
    public void NotaCreditoProveedor_AplicarMonto_en_EnEspera_no_permitido()
    {
        var nc = CapturarNc(facturaOrigenId: null, total: 500m);
        var act = () => nc.AplicarMonto(100m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NC_NO_APLICABLE");
    }
}
