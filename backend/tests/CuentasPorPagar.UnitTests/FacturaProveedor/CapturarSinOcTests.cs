using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

/// <summary>
/// F7-PR1: factory <c>FacturaProveedor.CapturarSinOc</c> para variantes
/// sin OC (Caja Chica, Viáticos, TC empresarial, Otros).
/// </summary>
public sealed class CapturarSinOcTests
{
    private static Domain.FacturaProveedor.FacturaProveedor Capturar(
        decimal total = 1160m,
        Guid? cfdiRecibidoId = null,
        string? uuidCfdi = null) =>
        Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(
            empresaId: Guid.NewGuid(),
            cfdiRecibidoId: cfdiRecibidoId,
            uuidCfdi: uuidCfdi,
            proveedorId: Guid.NewGuid(),
            sucursalId: Guid.NewGuid(),
            folioProveedor: "F-001", serieProveedor: "A",
            fechaDocumento: DateTimeOffset.UtcNow,
            fechaContabilizacion: DateTimeOffset.UtcNow,
            fechaVencimiento: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            moneda: "MXN", tipoCambio: null,
            subtotal: 1000m, descuentos: 0m,
            impuestosTrasladados: 160m, retenciones: 0m, total: total,
            motivoCaptura: "Caja chica — comp test",
            ahora: DateTimeOffset.UtcNow);

    [Fact]
    public void CapturarSinOc_crea_factura_sin_OC_en_Capturada()
    {
        var f = Capturar();
        f.OrdenCompraId.Should().BeNull();
        f.EncargadoComprasSnapshot.Should().BeNull();
        f.ToleranciaTipo.Should().BeNull();
        f.ToleranciaValor.Should().BeNull();
        f.DiferenciaContraOc.Should().Be(0m);
        f.Estado.Should().Be(EstadoPasivo.Capturada);
        f.SaldoPendiente.Should().Be(1160m);
        f.Bitacora.Should().HaveCount(1);
    }

    [Fact]
    public void CapturarSinOc_acepta_uuid_y_cfdi_recibido_opcional()
    {
        var cfdiId = Guid.NewGuid();
        var uuid = "5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B";
        var f = Capturar(cfdiRecibidoId: cfdiId, uuidCfdi: uuid);

        f.CfdiRecibidoId.Should().Be(cfdiId);
        f.UuidCfdi.Should().Be(uuid);
    }

    [Fact]
    public void CapturarSinOc_acepta_factura_sin_cfdi()
    {
        var f = Capturar();
        f.CfdiRecibidoId.Should().BeNull();
        f.UuidCfdi.Should().BeNull();
    }

    [Fact]
    public void CapturarSinOc_rechaza_total_no_positivo()
    {
        var act = () => Capturar(total: 0m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_TOTAL_INVALIDO");
    }

    [Fact]
    public void CapturarSinOc_acepta_aplicar_NC_y_anticipo_igual_que_CapturarConOc()
    {
        var f = Capturar(total: 1000m);
        f.AplicarNotaCredito(300m);
        f.AplicarAnticipo(200m);
        f.SaldoPendiente.Should().Be(500m);
    }
}
