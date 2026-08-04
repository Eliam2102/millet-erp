using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests unitarios del agregado <see cref="OrdenCompra"/>. F1-PR1 solo
/// cubre el constructor: cabecera mínima, invariantes estructurales
/// (GUIDs no vacíos, año razonable, moneda/tipo-de-cambio coherentes,
/// motivo si sin-RQ, observaciones acotadas, oc-origen no
/// auto-referencia). Los métodos de transición (EnviarAAutorizacion,
/// Autorizar, Cancelar, Rechazar) entran en tests de F3.
/// </summary>
public class OrdenCompraTests
{
    [Fact]
    public void Crear_ConParametrosValidos_DevuelveOrdenCompraEnBorrador()
    {
        var oc = NewOc();

        Assert.Equal(EstadoOrdenCompra.Borrador, oc.Estado);
        Assert.Equal(SubEstadoRecepcion.SinRecepcion, oc.SubEstadoRecepcion);
        Assert.Equal(SubEstadoFacturacion.SinFactura, oc.SubEstadoFacturacion);
        Assert.Equal(SubEstadoPago.SinPago, oc.SubEstadoPago);
        Assert.Equal("MXN", oc.Moneda);
        Assert.Null(oc.TipoCambio);
        Assert.False(oc.SinRequisicionPrevia);
        Assert.False(oc.EsImportacion);
        Assert.False(oc.CotizacionExcepcionada);
        Assert.Null(oc.FechaContabilizacion);
    }

    [Fact]
    public void Crear_ConMonedaExtranjeraYTipoCambioValido_OK()
    {
        var oc = NewOc(moneda: "USD", tipoCambio: 17.5m);

        Assert.Equal("USD", oc.Moneda);
        Assert.Equal(17.5m, oc.TipoCambio);
    }

    [Fact]
    public void Crear_ConMonedaExtranjeraSinTipoCambio_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => NewOc(moneda: "USD", tipoCambio: null));
        Assert.Equal("OC_TIPO_CAMBIO_REQUERIDO", ex.Code);
    }

    [Fact]
    public void Crear_ConMonedaExtranjeraYTipoCambioCero_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => NewOc(moneda: "USD", tipoCambio: 0m));
        Assert.Equal("OC_TIPO_CAMBIO_REQUERIDO", ex.Code);
    }

    [Fact]
    public void Crear_ConMxnYTipoCambioNoNulo_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => NewOc(moneda: "MXN", tipoCambio: 17.5m));
        Assert.Equal("OC_TIPO_CAMBIO_NO_APLICA", ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("MX")]
    [InlineData("MXNN")]
    public void Crear_ConMonedaDeFormatoInvalido_Lanza(string moneda)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => NewOc(moneda: moneda));
        Assert.Equal("OC_MONEDA_INVALIDA", ex.Code);
    }

    [Fact]
    public void Crear_NormalizaMonedaAMayusculas()
    {
        var oc = NewOc(moneda: "usd", tipoCambio: 17.5m);
        Assert.Equal("USD", oc.Moneda);
    }

    [Theory]
    [InlineData(1999)]
    [InlineData(2101)]
    [InlineData(0)]
    public void Crear_ConAnioFueraDeRango_Lanza(short anio)
    {
        var ex = Assert.Throws<BusinessRuleException>(() => NewOc(folioAnio: anio));
        Assert.Equal("FOLIO_OC_ANIO_INVALIDO", ex.Code);
    }

    [Fact]
    public void Crear_ConEmpresaIdVacio_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => NewOc(empresaId: Guid.Empty));
        Assert.Equal("GUID_VACIO", ex.Code);
    }

    [Fact]
    public void Crear_ConProveedorIdVacio_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() => NewOc(proveedorId: Guid.Empty));
        Assert.Equal("GUID_VACIO", ex.Code);
    }

    [Fact]
    public void Crear_SinRqPreviaYSinMotivo_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            NewOc(sinRequisicionPrevia: true, motivoSinRequisicion: null));
        Assert.Equal("OC_MOTIVO_SIN_RQ_REQUERIDO", ex.Code);
    }

    [Fact]
    public void Crear_SinRqPreviaYConMotivoVacio_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            NewOc(sinRequisicionPrevia: true, motivoSinRequisicion: "   "));
        Assert.Equal("OC_MOTIVO_SIN_RQ_REQUERIDO", ex.Code);
    }

    [Fact]
    public void Crear_SinRqPreviaConMotivoValido_OK()
    {
        var oc = NewOc(sinRequisicionPrevia: true, motivoSinRequisicion: "Compra emergencia mantenimiento línea 3");

        Assert.True(oc.SinRequisicionPrevia);
        Assert.Equal("Compra emergencia mantenimiento línea 3", oc.MotivoSinRequisicion);
    }

    [Fact]
    public void Crear_ConObservacionesDemasiadoLargas_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            NewOc(observaciones: new string('x', 1001)));
        Assert.Equal("OC_OBSERVACIONES_DEMASIADO_LARGAS", ex.Code);
    }

    [Fact]
    public void Crear_ConMotivoSinRqDemasiadoLargo_Lanza()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            NewOc(sinRequisicionPrevia: true, motivoSinRequisicion: new string('x', 501)));
        Assert.Equal("OC_MOTIVO_SIN_RQ_DEMASIADO_LARGO", ex.Code);
    }

    [Fact]
    public void Crear_ConOcOrigenIdIgualAlId_Lanza()
    {
        var id = Guid.CreateVersion7();
        var ex = Assert.Throws<BusinessRuleException>(() => NewOc(id: id, ocOrigenId: id));
        Assert.Equal("OC_ORIGEN_AUTOREFERENCIA", ex.Code);
    }

    [Fact]
    public void Crear_ConOcOrigenIdDistinto_OK()
    {
        var origen = Guid.CreateVersion7();
        var oc = NewOc(ocOrigenId: origen);

        Assert.Equal(origen, oc.OcOrigenId);
    }

    // --- Helper builder ---

    private static OrdenCompra NewOc(
        Guid? id = null,
        Guid? empresaId = null,
        string folioValor = "OC-MID2026-000001",
        short folioAnio = 2026,
        Guid? proveedorId = null,
        Guid? sucursalDestinoId = null,
        Guid? condicionesPagoId = null,
        Guid? usoPrincipalId = null,
        Guid? compradorTitularId = null,
        Guid? encargadoComprasId = null,
        DateOnly? fechaDocumento = null,
        string moneda = "MXN",
        decimal? tipoCambio = null,
        bool sinRequisicionPrevia = false,
        bool esImportacion = false,
        bool cotizacionExcepcionada = false,
        string? observaciones = null,
        string? motivoSinRequisicion = null,
        DateOnly? fechaEntregaEsperada = null,
        Guid? ocOrigenId = null) =>
        new(
            id: id ?? Guid.CreateVersion7(),
            empresaId: empresaId ?? Guid.CreateVersion7(),
            folio: Folio.Parse(folioValor),
            folioAnio: folioAnio,
            proveedorId: proveedorId ?? Guid.CreateVersion7(),
            sucursalDestinoId: sucursalDestinoId ?? Guid.CreateVersion7(),
            condicionesPagoId: condicionesPagoId ?? Guid.CreateVersion7(),
            usoPrincipalId: usoPrincipalId ?? Guid.CreateVersion7(),
            compradorTitularId: compradorTitularId ?? Guid.CreateVersion7(),
            encargadoComprasId: encargadoComprasId ?? Guid.CreateVersion7(),
            fechaDocumento: fechaDocumento ?? DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            moneda: moneda,
            tipoCambio: tipoCambio,
            sinRequisicionPrevia: sinRequisicionPrevia,
            esImportacion: esImportacion,
            cotizacionExcepcionada: cotizacionExcepcionada,
            observaciones: observaciones,
            motivoSinRequisicion: motivoSinRequisicion,
            fechaEntregaEsperada: fechaEntregaEsperada,
            ocOrigenId: ocOrigenId);
}
