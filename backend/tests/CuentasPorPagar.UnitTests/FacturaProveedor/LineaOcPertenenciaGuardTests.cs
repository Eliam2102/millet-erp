using FluentAssertions;
using Millet.CuentasPorPagar.Application.FacturaProveedor.CapturarFacturaConOc;
using Millet.CuentasPorPagar.Domain.Ports.Compras;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.FacturaProveedor;

/// <summary>
/// Guarda <c>LINEA_OC_NO_PERTENECE</c>: una línea de factura solo puede
/// declarar una <c>LineaOcId</c> que exista entre las líneas de la OC
/// conciliada. Cierra el hueco donde un GUID ajeno corrompería el acumulado
/// facturado por línea de OC (three-way match).
/// </summary>
public sealed class LineaOcPertenenciaGuardTests
{
    private const string FolioOc = "OC-MID2026-000001";

    private static LineaOcDto LineaOc(Guid id) =>
        new(
            Id: id,
            ArticuloId: Guid.NewGuid(),
            Cantidad: 10m,
            PrecioUnitario: 5m,
            CantidadFacturada: 0m,
            CantidadRecibida: 0m);

    private static CapturarFacturaConOcLinea LineaFactura(Guid? lineaOcId) =>
        new(
            ArticuloId: null,
            ClaveProdServ: null,
            Descripcion: "Concepto",
            Cantidad: 1m,
            ClaveUnidad: "PZA",
            Unidad: null,
            PrecioUnitario: 5m,
            Importe: 5m,
            Descuento: null,
            LineaOcId: lineaOcId,
            ConceptoContableId: null);

    [Fact]
    public void LineaOcId_de_otra_OC_o_inexistente_lanza_LINEA_OC_NO_PERTENECE()
    {
        var lineaDeLaOc = Guid.NewGuid();
        var lineaAjena = Guid.NewGuid();

        var act = () => LineaOcPertenenciaGuard.Validar(
            lineasFactura: new[] { LineaFactura(lineaAjena) },
            lineasOc: new[] { LineaOc(lineaDeLaOc) },
            folioOc: FolioOc);

        act.Should().Throw<BusinessRuleException>()
            .Where(e => e.Code == "LINEA_OC_NO_PERTENECE");
    }

    [Fact]
    public void LineaOcId_valida_de_la_OC_no_lanza()
    {
        var lineaDeLaOc = Guid.NewGuid();

        var act = () => LineaOcPertenenciaGuard.Validar(
            lineasFactura: new[] { LineaFactura(lineaDeLaOc) },
            lineasOc: new[] { LineaOc(lineaDeLaOc) },
            folioOc: FolioOc);

        act.Should().NotThrow();
    }

    [Fact]
    public void LineaOcId_null_no_lanza()
    {
        var act = () => LineaOcPertenenciaGuard.Validar(
            lineasFactura: new[] { LineaFactura(null) },
            lineasOc: new[] { LineaOc(Guid.NewGuid()) },
            folioOc: FolioOc);

        act.Should().NotThrow();
    }

    [Fact]
    public void Mezcla_de_lineas_validas_y_null_no_lanza()
    {
        var lineaA = Guid.NewGuid();

        var act = () => LineaOcPertenenciaGuard.Validar(
            lineasFactura: new[] { LineaFactura(lineaA), LineaFactura(null) },
            lineasOc: new[] { LineaOc(lineaA), LineaOc(Guid.NewGuid()) },
            folioOc: FolioOc);

        act.Should().NotThrow();
    }
}
