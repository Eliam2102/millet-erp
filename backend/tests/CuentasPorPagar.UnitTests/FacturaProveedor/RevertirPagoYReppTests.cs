using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

/// <summary>
/// F9-PR1: tests de las transiciones nuevas en FacturaProveedor —
/// RevertirPago, MarcarReppRecibido.
/// </summary>
public sealed class RevertirPagoYReppTests
{
    private static Domain.FacturaProveedor.FacturaProveedor CapturarYAutorizar(decimal total = 1160m)
    {
        var f = Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(
            empresaId: Guid.NewGuid(),
            cfdiRecibidoId: null, uuidCfdi: null,
            proveedorId: Guid.NewGuid(),
            sucursalId: Guid.NewGuid(),
            folioProveedor: "F-001", serieProveedor: "A",
            fechaDocumento: DateTimeOffset.UtcNow,
            fechaContabilizacion: DateTimeOffset.UtcNow,
            fechaVencimiento: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            moneda: "MXN", tipoCambio: null,
            subtotal: 1000m, descuentos: 0m,
            impuestosTrasladados: 160m, retenciones: 0m, total: total,
            motivoCaptura: "F9 test",
            ahora: DateTimeOffset.UtcNow);
        f.Autorizar(usuarioId: null, DateTimeOffset.UtcNow);
        return f;
    }

    // ----- RevertirPago -----

    [Fact]
    public void RevertirPago_factura_capturada_rechaza()
    {
        var f = Domain.FacturaProveedor.FacturaProveedor.CapturarSinOc(
            empresaId: Guid.NewGuid(), cfdiRecibidoId: null, uuidCfdi: null,
            proveedorId: Guid.NewGuid(), sucursalId: Guid.NewGuid(),
            folioProveedor: "F-001", serieProveedor: "A",
            fechaDocumento: DateTimeOffset.UtcNow,
            fechaContabilizacion: DateTimeOffset.UtcNow,
            fechaVencimiento: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)),
            moneda: "MXN", tipoCambio: null,
            subtotal: 100m, descuentos: 0m,
            impuestosTrasladados: 16m, retenciones: 0m, total: 116m,
            motivoCaptura: "x", ahora: DateTimeOffset.UtcNow);
        // estado = Capturada

        var act = () => f.RevertirPago(50m, DateTimeOffset.UtcNow, "test");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_NO_REVERTIBLE");
    }

    [Fact]
    public void RevertirPago_total_desde_Pagada_vuelve_a_Autorizada()
    {
        var f = CapturarYAutorizar(total: 1000m);
        f.RegistrarPago(1000m, DateTimeOffset.UtcNow, "pago test");
        f.Estado.Should().Be(EstadoPasivo.Pagada);

        f.RevertirPago(1000m, DateTimeOffset.UtcNow, "reverso total");

        f.Estado.Should().Be(EstadoPasivo.Autorizada);
        f.ImportePagado.Should().Be(0m);
        f.SaldoPendiente.Should().Be(1000m);
    }

    [Fact]
    public void RevertirPago_parcial_desde_Pagada_vuelve_a_Autorizada()
    {
        var f = CapturarYAutorizar(total: 1000m);
        f.RegistrarPago(1000m, DateTimeOffset.UtcNow, "pago test");

        f.RevertirPago(300m, DateTimeOffset.UtcNow, "reverso parcial");

        f.Estado.Should().Be(EstadoPasivo.Autorizada);
        f.ImportePagado.Should().Be(700m);
        f.SaldoPendiente.Should().Be(300m);
    }

    [Fact]
    public void RevertirPago_parcial_desde_Autorizada_no_cambia_estado()
    {
        var f = CapturarYAutorizar(total: 1000m);
        f.RegistrarPago(500m, DateTimeOffset.UtcNow, "abono parcial");
        f.Estado.Should().Be(EstadoPasivo.Autorizada);

        f.RevertirPago(200m, DateTimeOffset.UtcNow, "reverso parcial");

        f.Estado.Should().Be(EstadoPasivo.Autorizada);
        f.ImportePagado.Should().Be(300m);
    }

    [Fact]
    public void RevertirPago_excede_pagado_rechaza()
    {
        var f = CapturarYAutorizar(total: 1000m);
        f.RegistrarPago(500m, DateTimeOffset.UtcNow, "abono");

        var act = () => f.RevertirPago(600m, DateTimeOffset.UtcNow, "x");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "REVERSA_EXCEDE_PAGADO");
    }

    [Fact]
    public void RevertirPago_monto_no_positivo_rechaza()
    {
        var f = CapturarYAutorizar();
        f.RegistrarPago(500m, DateTimeOffset.UtcNow, "abono");

        var act = () => f.RevertirPago(0m, DateTimeOffset.UtcNow, "x");
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "REVERSA_MONTO_INVALIDO");
    }

    // ----- MarcarReppRecibido -----

    [Fact]
    public void MarcarReppRecibido_idempotente()
    {
        var f = CapturarYAutorizar();
        f.ReppRecibido.Should().BeFalse();

        f.MarcarReppRecibido();
        f.ReppRecibido.Should().BeTrue();

        // Idempotente: una segunda llamada no falla.
        f.MarcarReppRecibido();
        f.ReppRecibido.Should().BeTrue();
    }
}
