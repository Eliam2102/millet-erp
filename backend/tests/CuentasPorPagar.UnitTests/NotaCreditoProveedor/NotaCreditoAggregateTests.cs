using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.NotaCreditoProveedor;

public sealed class NotaCreditoAggregateTests
{
    private static Domain.NotaCreditoProveedor.NotaCreditoProveedor Capturar(
        Guid? facturaOrigenId = null,
        decimal total = 116m) =>
        Domain.NotaCreditoProveedor.NotaCreditoProveedor.Capturar(
            empresaId: Guid.NewGuid(),
            cfdiRecibidoId: Guid.NewGuid(),
            uuidCfdi: "5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B",
            proveedorId: Guid.NewGuid(),
            folioProveedor: "NC-001", serieProveedor: "A",
            fechaCfdi: DateTimeOffset.UtcNow,
            moneda: "MXN", tipoCambio: null,
            subtotal: 100m, impuestosTrasladados: 16m, retenciones: 0m, total: total,
            tipo: TipoNotaCredito.Descuento,
            tipoRelacionCfdi: TipoRelacionCfdi.NotaCredito,
            uuidRelacionCfdi: "11111111-2222-3333-4444-555555555555",
            facturaOrigenId: facturaOrigenId,
            capturadoPor: Guid.NewGuid(),
            ahora: DateTimeOffset.UtcNow);

    [Fact]
    public void Capturar_con_factura_origen_nace_Abierta_con_FechaMatch()
    {
        var nc = Capturar(facturaOrigenId: Guid.NewGuid());
        nc.Estado.Should().Be(EstadoNotaCredito.Abierta);
        nc.FacturaOrigenId.Should().NotBeNull();
        nc.FechaMatch.Should().NotBeNull();
        nc.SaldoPorAplicar.Should().Be(116m);
    }

    [Fact]
    public void Capturar_sin_factura_origen_nace_EnEspera()
    {
        var nc = Capturar(facturaOrigenId: null);
        nc.Estado.Should().Be(EstadoNotaCredito.EnEspera);
        nc.FacturaOrigenId.Should().BeNull();
        nc.FechaMatch.Should().BeNull();
    }

    [Fact]
    public void Capturar_normaliza_UUIDs_a_mayusculas()
    {
        var nc = Domain.NotaCreditoProveedor.NotaCreditoProveedor.Capturar(
            empresaId: Guid.NewGuid(),
            cfdiRecibidoId: null,
            uuidCfdi: "5fb0f1c2-3e2a-4f0f-9e2e-2c5c2b5c1a2b",
            proveedorId: Guid.NewGuid(),
            folioProveedor: null, serieProveedor: null,
            fechaCfdi: DateTimeOffset.UtcNow,
            moneda: "mxn", tipoCambio: null,
            subtotal: 100m, impuestosTrasladados: 16m, retenciones: 0m, total: 116m,
            tipo: TipoNotaCredito.Descuento,
            tipoRelacionCfdi: TipoRelacionCfdi.NotaCredito,
            uuidRelacionCfdi: "11111111-2222-3333-4444-555555555555",
            facturaOrigenId: null,
            capturadoPor: null,
            ahora: DateTimeOffset.UtcNow);

        nc.UuidCfdi.Should().Be("5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B");
        nc.UuidRelacionCfdi.Should().Be("11111111-2222-3333-4444-555555555555");
        nc.Moneda.Should().Be("MXN");
    }

    [Fact]
    public void Capturar_rechaza_uuid_relacion_vacio()
    {
        var act = () => Domain.NotaCreditoProveedor.NotaCreditoProveedor.Capturar(
            empresaId: Guid.NewGuid(),
            cfdiRecibidoId: null,
            uuidCfdi: "5FB0F1C2-3E2A-4F0F-9E2E-2C5C2B5C1A2B",
            proveedorId: Guid.NewGuid(),
            folioProveedor: null, serieProveedor: null,
            fechaCfdi: DateTimeOffset.UtcNow,
            moneda: "MXN", tipoCambio: null,
            subtotal: 100m, impuestosTrasladados: 16m, retenciones: 0m, total: 116m,
            tipo: TipoNotaCredito.Descuento,
            tipoRelacionCfdi: TipoRelacionCfdi.NotaCredito,
            uuidRelacionCfdi: "",
            facturaOrigenId: null,
            capturadoPor: null,
            ahora: DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NC_UUID_RELACION_VACIO");
    }

    [Fact]
    public void VincularFacturaOrigen_desde_EnEspera_pasa_a_Abierta()
    {
        var nc = Capturar(facturaOrigenId: null);
        var facturaId = Guid.NewGuid();
        var ahora = DateTimeOffset.UtcNow;

        nc.VincularFacturaOrigen(facturaId, ahora);

        nc.Estado.Should().Be(EstadoNotaCredito.Abierta);
        nc.FacturaOrigenId.Should().Be(facturaId);
        nc.FechaMatch.Should().Be(ahora);
    }

    [Fact]
    public void VincularFacturaOrigen_desde_Abierta_lanza()
    {
        var nc = Capturar(facturaOrigenId: Guid.NewGuid());
        var act = () => nc.VincularFacturaOrigen(Guid.NewGuid(), DateTimeOffset.UtcNow);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "NC_NO_EN_ESPERA");
    }

    [Fact]
    public void Cancelar_desde_EnEspera_pasa_a_Cancelada_con_motivo()
    {
        var nc = Capturar(facturaOrigenId: null);
        nc.Cancelar("Proveedor canceló la NC en SAT", DateTimeOffset.UtcNow);

        nc.Estado.Should().Be(EstadoNotaCredito.Cancelada);
        nc.MotivoCancelacion.Should().Be("Proveedor canceló la NC en SAT");
        nc.FechaCancelacion.Should().NotBeNull();
    }
}
