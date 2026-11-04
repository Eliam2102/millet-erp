using Millet.CuentasPorPagar.Domain.FacturaProveedor;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

/// <summary>86e3ke0z5: el motivo «CFDI cancelado ante el SAT» se asigna y se libera como cualquier motivo ordinario.</summary>
public sealed class CfdiCanceladoSatRevisionTests
{
    [Fact]
    public void Enviar_a_revision_con_motivo_cfdi_cancelado_sat_y_liberar_regresa_a_Capturada()
    {
        var now = DateTimeOffset.UtcNow;
        var f = Domain.FacturaProveedor.FacturaProveedor.CapturarConOc(
            empresaId: Guid.NewGuid(), cfdiRecibidoId: null, uuidCfdi: null, proveedorId: Guid.NewGuid(), sucursalId: Guid.NewGuid(),
            folioProveedor: "F-9A", serieProveedor: "A", fechaDocumento: now, fechaContabilizacion: now,
            fechaVencimiento: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(30)), moneda: "MXN", tipoCambio: null,
            subtotal: 1000m, descuentos: 0m, impuestosTrasladados: 160m, retenciones: 0m, total: 1160m,
            ordenCompraId: Guid.NewGuid(), encargadoComprasSnapshot: null, tolerancia: Tolerancia.MontoAbsoluto(0.99m),
            diferenciaContraOc: 0m, redondeoAplicado: 0m, ahora: now);

        f.EnviarARevision(Domain.FacturaProveedor.FacturaProveedor.MotivoCfdiCanceladoSatId, Guid.NewGuid(), null, now);
        f.Estado.Should().Be(EstadoPasivo.EnRevision);
        f.MotivoRevisionId.Should().Be(Domain.FacturaProveedor.FacturaProveedor.MotivoCfdiCanceladoSatId);

        f.LiberarRevision("Cancelación aclarada con el proveedor", null, now);
        f.Estado.Should().Be(EstadoPasivo.Capturada);
    }
}
