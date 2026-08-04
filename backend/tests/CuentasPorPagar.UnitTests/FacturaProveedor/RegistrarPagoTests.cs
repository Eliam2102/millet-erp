using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

/// <summary>
/// F7-PR4: <see cref="Domain.FacturaProveedor.FacturaProveedor.RegistrarPago"/>
/// transiciona Autorizada → Pagada cuando el saldo llega a 0. Usado por
/// el flujo A de TC empresarial (la TC ya pagó al proveedor).
/// </summary>
public sealed class RegistrarPagoTests
{
    private static Domain.FacturaProveedor.FacturaProveedor CapturarSinOc(decimal total = 1160m) =>
        Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(
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
            motivoCaptura: "TC test",
            ahora: DateTimeOffset.UtcNow);

    [Fact]
    public void RegistrarPago_Capturada_rechaza()
    {
        var f = CapturarSinOc();
        // estado = Capturada (no Autorizada todavía)
        var act = () => f.RegistrarPago(100m, DateTimeOffset.UtcNow, "test");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_NO_PAGABLE");
    }

    [Fact]
    public void RegistrarPago_total_transiciona_a_Pagada()
    {
        var f = CapturarSinOc(total: 1160m);
        f.Autorizar(usuarioId: null, DateTimeOffset.UtcNow);

        f.RegistrarPago(1160m, DateTimeOffset.UtcNow, "Pago TC");

        f.Estado.Should().Be(EstadoPasivo.Pagada);
        f.ImportePagado.Should().Be(1160m);
        f.SaldoPendiente.Should().Be(0m);
    }

    [Fact]
    public void RegistrarPago_parcial_mantiene_Autorizada()
    {
        var f = CapturarSinOc(total: 1160m);
        f.Autorizar(usuarioId: null, DateTimeOffset.UtcNow);

        f.RegistrarPago(500m, DateTimeOffset.UtcNow, "Abono parcial");

        f.Estado.Should().Be(EstadoPasivo.Autorizada);
        f.ImportePagado.Should().Be(500m);
        f.SaldoPendiente.Should().Be(660m);
    }

    [Fact]
    public void RegistrarPago_exceso_saldo_rechaza()
    {
        var f = CapturarSinOc(total: 100m);
        f.Autorizar(usuarioId: null, DateTimeOffset.UtcNow);

        var act = () => f.RegistrarPago(101m, DateTimeOffset.UtcNow, "exceso");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAGO_EXCEDE_SALDO");
    }

    [Fact]
    public void RegistrarPago_monto_no_positivo_rechaza()
    {
        var f = CapturarSinOc();
        f.Autorizar(usuarioId: null, DateTimeOffset.UtcNow);

        var act = () => f.RegistrarPago(0m, DateTimeOffset.UtcNow, "x");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PAGO_MONTO_INVALIDO");
    }
}
