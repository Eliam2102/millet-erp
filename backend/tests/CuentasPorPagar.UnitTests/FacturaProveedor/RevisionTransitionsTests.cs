using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

public sealed class RevisionTransitionsTests
{
    private static Domain.FacturaProveedor.FacturaProveedor Capturar() =>
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
            impuestosTrasladados: 160m, retenciones: 0m, total: 1160m,
            ordenCompraId: Guid.NewGuid(),
            encargadoComprasSnapshot: null,
            tolerancia: Tolerancia.MontoAbsoluto(0.99m),
            diferenciaContraOc: 0m,
            redondeoAplicado: 0m,
            ahora: DateTimeOffset.UtcNow);

    [Fact]
    public void EnviarARevision_desde_Capturada_pasa_a_EnRevision_y_setea_metadata()
    {
        var f = Capturar();
        var motivoId = Guid.NewGuid();
        var depId = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;

        f.EnviarARevision(motivoId, depId, usuarioId: null, ahora);

        f.Estado.Should().Be(EstadoPasivo.EnRevision);
        f.EnRevision.Should().BeTrue();
        f.MotivoRevisionId.Should().Be(motivoId);
        f.DependenciaRevisoraId.Should().Be(depId);
        f.FechaEntradaRevision.Should().Be(ahora);
        f.Bitacora.Should().HaveCount(2);
    }

    [Fact]
    public void EnviarARevision_desde_Pagada_o_Cancelada_lanza()
    {
        var f = Capturar();
        f.Cancelar(MotivoCancelacion.ErrorCaptura, null, null, DateTimeOffset.UtcNow);

        var act = () => f.EnviarARevision(Guid.NewGuid(), Guid.NewGuid(), null, DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_NO_ENVIABLE_A_REVISION");
    }

    [Fact]
    public void LiberarRevision_desde_EnRevision_pasa_a_Capturada_limpia_metadata()
    {
        var f = Capturar();
        f.EnviarARevision(Guid.NewGuid(), Guid.NewGuid(), null, DateTimeOffset.UtcNow);

        f.LiberarRevision("Documentación corregida por el proveedor", usuarioId: null, ahora: DateTimeOffset.UtcNow);

        f.Estado.Should().Be(EstadoPasivo.Capturada);
        f.EnRevision.Should().BeFalse();
        f.MotivoRevisionId.Should().BeNull();
        f.DependenciaRevisoraId.Should().BeNull();
        f.FechaEntradaRevision.Should().BeNull();
        f.Bitacora.Should().HaveCount(3); // captura + envío + liberación
    }

    [Fact]
    public void LiberarRevision_sin_accion_lanza()
    {
        var f = Capturar();
        f.EnviarARevision(Guid.NewGuid(), Guid.NewGuid(), null, DateTimeOffset.UtcNow);

        var act = () => f.LiberarRevision("", null, DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_ACCION_REVISION_VACIA");
    }

    [Fact]
    public void LiberarRevision_desde_otro_estado_lanza()
    {
        var f = Capturar();
        var act = () => f.LiberarRevision("X", null, DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "FACTURA_NO_EN_REVISION");
    }

    [Fact]
    public void Cancelar_desde_EnRevision_limpia_flag_de_revision()
    {
        var f = Capturar();
        f.EnviarARevision(Guid.NewGuid(), Guid.NewGuid(), null, DateTimeOffset.UtcNow);

        f.Cancelar(MotivoCancelacion.ErrorCaptura, null, null, DateTimeOffset.UtcNow);

        f.Estado.Should().Be(EstadoPasivo.Cancelada);
        f.EnRevision.Should().BeFalse();
    }
}
