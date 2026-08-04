using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests de la entidad hija <see cref="LineaOrdenCompra"/> (diseño §4.2).
/// Cubre invariantes del constructor, subtotal post-descuento, IVA v0 y
/// coherencia RQ.
/// </summary>
public class LineaOrdenCompraTests
{
    private static LineaOrdenCompra NewLinea(
        Guid? id = null,
        Guid? ordenCompraId = null,
        int posicion = 1,
        Guid? articuloId = null,
        decimal cantidad = 10m,
        string unidadMedida = "PZA",
        decimal precioUnitario = 100m,
        Guid? departamentoSolicitanteId = null,
        DescuentoLinea? descuento = null,
        Guid? requisicionId = null,
        Guid? lineaRequisicionId = null,
        string? descripcionExtendida = null,
        string? textoAdicional = null) =>
        new(
            id: id ?? Guid.CreateVersion7(),
            ordenCompraId: ordenCompraId ?? Guid.CreateVersion7(),
            posicion: posicion,
            articuloId: articuloId ?? Guid.CreateVersion7(),
            cantidad: cantidad,
            unidadMedida: unidadMedida,
            precioUnitario: precioUnitario,
            departamentoSolicitanteId: departamentoSolicitanteId ?? Guid.CreateVersion7(),
            descuento: descuento,
            requisicionId: requisicionId,
            lineaRequisicionId: lineaRequisicionId,
            descripcionExtendida: descripcionExtendida,
            textoAdicional: textoAdicional);

    [Fact]
    public void Crear_HappyPath_OK()
    {
        var linea = NewLinea();
        Assert.Equal(1, linea.Posicion);
        Assert.Equal(10m, linea.Cantidad);
        Assert.Equal(100m, linea.PrecioUnitario);
        Assert.Equal(DescuentoLinea.Cero, linea.Descuento);
        Assert.Equal(IndicadorImpuestos.Iva16Default, linea.IndicadorImpuestos);
        Assert.Equal(0m, linea.CantidadRecibida);
        Assert.Equal(0m, linea.CantidadFacturada);
    }

    [Fact]
    public void Crear_OrdenCompraIdVacio_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => NewLinea(ordenCompraId: Guid.Empty));
        Assert.Equal("LINEA_OC_ORDEN_COMPRA_ID_VACIO", ex.Code);
    }

    [Fact]
    public void Crear_ArticuloVacio_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => NewLinea(articuloId: Guid.Empty));
        Assert.Equal("LINEA_OC_ARTICULO_REQUERIDO", ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Crear_CantidadNoPositiva_Lanza(decimal cantidad)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => NewLinea(cantidad: cantidad));
        Assert.Equal("LINEA_OC_CANTIDAD_INVALIDA", ex.Code);
    }

    [Fact]
    public void Crear_PrecioNegativo_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => NewLinea(precioUnitario: -1m));
        Assert.Equal("LINEA_OC_PRECIO_NEGATIVO", ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Crear_PosicionNoPositiva_Lanza(int posicion)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => NewLinea(posicion: posicion));
        Assert.Equal("LINEA_OC_POSICION_INVALIDA", ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    [InlineData("UNIDAD_MEDIDA_DE_MAS_DE_VEINTE_CARACTERES")]
    public void Crear_UnidadMedidaInvalida_Lanza(string um)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => NewLinea(unidadMedida: um));
        Assert.Equal("LINEA_OC_UNIDAD_MEDIDA_INVALIDA", ex.Code);
    }

    [Fact]
    public void Crear_RqIdSinLineaRqId_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            NewLinea(requisicionId: Guid.CreateVersion7(), lineaRequisicionId: null));
        Assert.Equal("LINEA_OC_RQ_INCOHERENTE", ex.Code);
    }

    [Fact]
    public void Crear_LineaRqIdSinRqId_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            NewLinea(requisicionId: null, lineaRequisicionId: Guid.CreateVersion7()));
        Assert.Equal("LINEA_OC_RQ_INCOHERENTE", ex.Code);
    }

    [Fact]
    public void Crear_AmbosRqIdsPresentes_OK()
    {
        var linea = NewLinea(
            requisicionId: Guid.CreateVersion7(),
            lineaRequisicionId: Guid.CreateVersion7());
        Assert.NotNull(linea.RequisicionId);
        Assert.NotNull(linea.LineaRequisicionId);
    }

    [Fact]
    public void SubtotalLinea_SinDescuento_EsBruto()
    {
        var linea = NewLinea(cantidad: 10m, precioUnitario: 25m);
        Assert.Equal(250m, linea.SubtotalLinea);
    }

    [Fact]
    public void SubtotalLinea_ConDescuentoPorcentaje_RestaCorrectamente()
    {
        var linea = NewLinea(
            cantidad: 10m, precioUnitario: 100m,
            descuento: new DescuentoLinea(DescuentoTipo.Porcentaje, 10m));
        // bruto = 1000, descuento = 100, subtotal = 900
        Assert.Equal(900m, linea.SubtotalLinea);
    }

    [Fact]
    public void SubtotalLinea_ConDescuentoMonto_RestaCorrectamente()
    {
        var linea = NewLinea(
            cantidad: 10m, precioUnitario: 100m,
            descuento: new DescuentoLinea(DescuentoTipo.Monto, 250m));
        // bruto = 1000, descuento = 250, subtotal = 750
        Assert.Equal(750m, linea.SubtotalLinea);
    }

    [Fact]
    public void IvaImporte_Construccion_AplicaIva16PorcSobreSubtotal()
    {
        var linea = NewLinea(cantidad: 10m, precioUnitario: 100m);
        // subtotal = 1000, IVA = 1000 * 0.16 = 160
        Assert.Equal(160m, linea.IvaImporte);
    }

    [Fact]
    public void IvaImporte_ConDescuento_CalculaSobrePostDescuento()
    {
        var linea = NewLinea(
            cantidad: 10m, precioUnitario: 100m,
            descuento: new DescuentoLinea(DescuentoTipo.Porcentaje, 10m));
        // subtotal post-descuento = 900, IVA = 900 * 0.16 = 144
        Assert.Equal(144m, linea.IvaImporte);
    }

    [Fact]
    public void RetencionIsr_MotorV0_EsNull()
    {
        var linea = NewLinea();
        Assert.Null(linea.RetencionIsr);
    }
}
