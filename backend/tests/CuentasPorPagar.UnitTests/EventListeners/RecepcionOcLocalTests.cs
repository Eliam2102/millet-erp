using Millet.CuentasPorPagar.Domain.Almacen;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.EventListeners;

public sealed class RecepcionOcLocalTests
{
    private static RecepcionOcLocal Crear(Guid? recepcionId = null, Guid? ordenCompraId = null, bool facturaPendiente = false) =>
        new(
            empresaId: Guid.NewGuid(),
            recepcionId: recepcionId ?? Guid.NewGuid(),
            folioRecepcion: "REC-2026-000001",
            ordenCompraId: ordenCompraId ?? Guid.NewGuid(),
            fechaMovimiento: DateOnly.FromDateTime(DateTime.UtcNow),
            facturaPendiente: facturaPendiente,
            cfdiRecibidoId: null,
            observaciones: null,
            lineasJson: "[]",
            ocurridoEn: DateTimeOffset.UtcNow,
            proyectadoEn: DateTimeOffset.UtcNow);

    [Fact]
    public void Constructor_acepta_variante_A_factura_no_pendiente()
    {
        var r = Crear(facturaPendiente: false);
        r.FacturaPendiente.Should().BeFalse();
    }

    [Fact]
    public void Constructor_acepta_variante_B_factura_pendiente()
    {
        var r = Crear(facturaPendiente: true);
        r.FacturaPendiente.Should().BeTrue();
    }

    [Fact]
    public void Constructor_rechaza_recepcion_id_vacio()
    {
        var act = () => Crear(recepcionId: Guid.Empty);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "RECEPCION_ID_VACIO");
    }

    [Fact]
    public void Constructor_rechaza_oc_vacia()
    {
        var act = () => Crear(ordenCompraId: Guid.Empty);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "RECEPCION_OC_VACIA");
    }

    [Fact]
    public void Constructor_default_lineas_json_array_vacio()
    {
        var r = Crear();
        r.LineasJson.Should().Be("[]");
    }
}
