using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests de <see cref="OrdenCompra.ActualizarCabecera"/> (F2-PR2).
/// </summary>
public class OrdenCompraEditarCabeceraTests
{
    private static OrdenCompra NewOcBorrador() => new(
        id: Guid.CreateVersion7(),
        empresaId: Guid.CreateVersion7(),
        folio: Folio.Parse("OC-MID2026-000001"),
        folioAnio: 2026,
        proveedorId: Guid.CreateVersion7(),
        sucursalDestinoId: Guid.CreateVersion7(),
        condicionesPagoId: Guid.CreateVersion7(),
        usoPrincipalId: Guid.CreateVersion7(),
        compradorTitularId: Guid.CreateVersion7(),
        encargadoComprasId: Guid.CreateVersion7(),
        fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime));

    [Fact]
    public void ActualizarCabecera_Observaciones_OK()
    {
        var oc = NewOcBorrador();
        oc.ActualizarCabecera(
            proveedorId: null,
            condicionesPagoId: null, usoPrincipalId: null,
            encargadoComprasId: null, moneda: null, tipoCambio: null,
            esImportacion: null, cotizacionExcepcionada: null,
            observaciones: "Nueva obs", fechaEntregaEsperada: null,
            descuentoGlobalTipo: null, descuentoGlobalValor: null,
            gastosAdicionales: null, redondeo: null);

        Assert.Equal("Nueva obs", oc.Observaciones);
    }

    [Fact]
    public void ActualizarCabecera_LimpiarObservaciones_SetNull()
    {
        var oc = NewOcBorrador();
        oc.ActualizarCabecera(
            proveedorId: null,
            condicionesPagoId: null, usoPrincipalId: null,
            encargadoComprasId: null, moneda: null, tipoCambio: null,
            esImportacion: null, cotizacionExcepcionada: null,
            observaciones: "Hello", fechaEntregaEsperada: null,
            descuentoGlobalTipo: null, descuentoGlobalValor: null,
            gastosAdicionales: null, redondeo: null);
        oc.ActualizarCabecera(
            proveedorId: null,
            condicionesPagoId: null, usoPrincipalId: null,
            encargadoComprasId: null, moneda: null, tipoCambio: null,
            esImportacion: null, cotizacionExcepcionada: null,
            observaciones: null, fechaEntregaEsperada: null,
            descuentoGlobalTipo: null, descuentoGlobalValor: null,
            gastosAdicionales: null, redondeo: null,
            limpiarObservaciones: true);

        Assert.Null(oc.Observaciones);
    }

    [Fact]
    public void ActualizarCabecera_GastosNegativos_Lanza()
    {
        var oc = NewOcBorrador();
        var ex = Assert.Throws<BusinessRuleException>(() => oc.ActualizarCabecera(
            proveedorId: null,
            condicionesPagoId: null, usoPrincipalId: null,
            encargadoComprasId: null, moneda: null, tipoCambio: null,
            esImportacion: null, cotizacionExcepcionada: null,
            observaciones: null, fechaEntregaEsperada: null,
            descuentoGlobalTipo: null, descuentoGlobalValor: null,
            gastosAdicionales: -10m, redondeo: null));
        Assert.Equal("OC_GASTOS_NEGATIVOS", ex.Code);
    }

    [Fact]
    public void ActualizarCabecera_DescuentoGlobal_Aplica()
    {
        var oc = NewOcBorrador();
        oc.ActualizarCabecera(
            proveedorId: null,
            condicionesPagoId: null, usoPrincipalId: null,
            encargadoComprasId: null, moneda: null, tipoCambio: null,
            esImportacion: null, cotizacionExcepcionada: null,
            observaciones: null, fechaEntregaEsperada: null,
            descuentoGlobalTipo: DescuentoTipo.Porcentaje,
            descuentoGlobalValor: 10m,
            gastosAdicionales: null, redondeo: null);

        Assert.NotNull(oc.DescuentoGlobal);
        Assert.Equal(DescuentoTipo.Porcentaje, oc.DescuentoGlobal!.Value.Tipo);
        Assert.Equal(10m, oc.DescuentoGlobal!.Value.Valor);
    }

    [Fact]
    public void ActualizarCabecera_LimpiarDescuentoGlobal_SetAmbosANull()
    {
        var oc = NewOcBorrador();
        oc.ActualizarCabecera(
            proveedorId: null,
            condicionesPagoId: null, usoPrincipalId: null,
            encargadoComprasId: null, moneda: null, tipoCambio: null,
            esImportacion: null, cotizacionExcepcionada: null,
            observaciones: null, fechaEntregaEsperada: null,
            descuentoGlobalTipo: DescuentoTipo.Monto,
            descuentoGlobalValor: 50m,
            gastosAdicionales: null, redondeo: null);
        oc.ActualizarCabecera(
            proveedorId: null,
            condicionesPagoId: null, usoPrincipalId: null,
            encargadoComprasId: null, moneda: null, tipoCambio: null,
            esImportacion: null, cotizacionExcepcionada: null,
            observaciones: null, fechaEntregaEsperada: null,
            descuentoGlobalTipo: null, descuentoGlobalValor: null,
            gastosAdicionales: null, redondeo: null,
            limpiarDescuentoGlobal: true);

        Assert.Null(oc.DescuentoGlobal);
        Assert.Null(oc.DescuentoGlobalTipo);
        Assert.Null(oc.DescuentoGlobalValor);
    }

    [Fact]
    public void ActualizarCabecera_CambiarAMonedaExtranjeraConTC_OK()
    {
        var oc = NewOcBorrador();
        oc.ActualizarCabecera(
            proveedorId: null,
            condicionesPagoId: null, usoPrincipalId: null,
            encargadoComprasId: null, moneda: "USD", tipoCambio: 17.5m,
            esImportacion: null, cotizacionExcepcionada: null,
            observaciones: null, fechaEntregaEsperada: null,
            descuentoGlobalTipo: null, descuentoGlobalValor: null,
            gastosAdicionales: null, redondeo: null);

        Assert.Equal("USD", oc.Moneda);
        Assert.Equal(17.5m, oc.TipoCambio);
    }

    [Fact]
    public void ActualizarCabecera_CambiarAMonedaExtranjeraSinTC_Lanza()
    {
        var oc = NewOcBorrador();
        var ex = Assert.Throws<BusinessRuleException>(() => oc.ActualizarCabecera(
            proveedorId: null,
            condicionesPagoId: null, usoPrincipalId: null,
            encargadoComprasId: null, moneda: "USD", tipoCambio: null,
            esImportacion: null, cotizacionExcepcionada: null,
            observaciones: null, fechaEntregaEsperada: null,
            descuentoGlobalTipo: null, descuentoGlobalValor: null,
            gastosAdicionales: null, redondeo: null));
        Assert.Equal("OC_TIPO_CAMBIO_REQUERIDO", ex.Code);
    }
}
