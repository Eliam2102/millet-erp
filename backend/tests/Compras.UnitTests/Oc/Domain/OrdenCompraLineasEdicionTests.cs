using Millet.Compras.Domain.Oc;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Oc.Domain;

/// <summary>
/// Tests de <see cref="OrdenCompra.ActualizarLinea"/>,
/// <see cref="OrdenCompra.EliminarLinea"/> y
/// <see cref="OrdenCompra.ActualizarTextoAdicionalLinea"/> (F2-PR2).
/// </summary>
public class OrdenCompraLineasEdicionTests
{
    /// <summary>
    /// OC con una línea MANUAL (LineaRequisicionId == null). Desde Fase E
    /// PR3.1 el CC-Máquina es requerido en la línea manual, así que el helper
    /// lo siembra por default; los tests del caso LEGADO (líneas anteriores a
    /// PR3.1, con CC en null) pasan <c>centroCostoId: null</c> explícito.
    /// </summary>
    private static OrdenCompra NewOcConLinea(
        out LineaOrdenCompra linea,
        Guid? centroCostoId = null,
        bool sembrarCc = true)
    {
        var oc = new OrdenCompra(
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
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: true,
            motivoSinRequisicion: "Test edicion linea", usoCfdiClave: "G03", metodoPago: "PPD", formaPagoClave: "99");
        linea = oc.AgregarLineaManual(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 10m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            centroCostoId: sembrarCc
                ? centroCostoId ?? Guid.CreateVersion7()
                : null);
        return oc;
    }

    /// <summary>OC con una línea HEREDADA de una RQ (LineaRequisicionId != null).</summary>
    private static OrdenCompra NewOcConLineaHeredada(out LineaOrdenCompra linea, Guid? centroCostoId = null)
    {
        var oc = new OrdenCompra(
            id: Guid.CreateVersion7(),
            empresaId: Guid.CreateVersion7(),
            folio: Folio.Parse("OC-MID2026-000009"),
            folioAnio: 2026,
            proveedorId: Guid.CreateVersion7(),
            sucursalDestinoId: Guid.CreateVersion7(),
            condicionesPagoId: Guid.CreateVersion7(),
            usoPrincipalId: Guid.CreateVersion7(),
            compradorTitularId: Guid.CreateVersion7(),
            encargadoComprasId: Guid.CreateVersion7(),
            fechaDocumento: DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime),
            sinRequisicionPrevia: false, usoCfdiClave: "G03", metodoPago: "PPD", formaPagoClave: "99");
        linea = oc.AgregarLineaDesdeRequisicion(
            lineaId: Guid.CreateVersion7(),
            articuloId: Guid.CreateVersion7(),
            cantidad: 10m,
            unidadMedida: "PZA",
            precioUnitario: 100m,
            departamentoSolicitanteId: Guid.CreateVersion7(),
            requisicionId: Guid.CreateVersion7(),
            lineaRequisicionId: Guid.CreateVersion7(),
            centroCostoId: centroCostoId);
        return oc;
    }

    [Fact]
    public void ActualizarLinea_CcEnLineaManual_LoCambia()
    {
        var oc = NewOcConLinea(out var linea); // manual: LineaRequisicionId == null
        var cc = Guid.CreateVersion7();
        oc.ActualizarLinea(linea.Id, centroCostoId: cc);
        Assert.Equal(cc, linea.CentroCostoId);
    }

    [Fact]
    public void ActualizarLinea_CcEnLineaHeredada_Lanza()
    {
        // Fase E PR3: el CC-Máquina heredado (LineaRequisicionId != null) es
        // inmutable — solo la línea manual lo edita (ADR-0050).
        var oc = NewOcConLineaHeredada(out var linea, centroCostoId: Guid.CreateVersion7());
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.ActualizarLinea(linea.Id, centroCostoId: Guid.CreateVersion7()));
        Assert.Equal("LINEA_OC_CC_HEREDADO_INMUTABLE", ex.Code);
    }

    // ─── Fase E PR3.1: el CC-Máquina es obligatorio en la línea MANUAL ───

    [Fact]
    public void ActualizarLinea_ManualLegadaSinCc_NoPuedeQuedarSinCc()
    {
        // Línea manual anterior a PR3.1 (CC en null). Editarla sin completar
        // el CC-Máquina se rechaza: la obligatoriedad se cobra por demanda al
        // tocar el documento, sin sembrado masivo (ADR-0050).
        var oc = NewOcConLinea(out var linea, sembrarCc: false);
        Assert.Null(linea.CentroCostoId);

        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.ActualizarLinea(linea.Id, cantidad: 20m));

        Assert.Equal("LINEA_OC_MANUAL_CENTRO_COSTO_REQUERIDO", ex.Code);
    }

    [Fact]
    public void ActualizarLinea_ManualLegadaSinCc_SeCompletaEnLaMismaEdicion()
    {
        // La salida del caso anterior: mandar el CC en la misma edición.
        var oc = NewOcConLinea(out var linea, sembrarCc: false);
        var cc = Guid.CreateVersion7();

        oc.ActualizarLinea(linea.Id, cantidad: 20m, centroCostoId: cc);

        Assert.Equal(cc, linea.CentroCostoId);
        Assert.Equal(20m, linea.Cantidad);
    }

    [Fact]
    public void ActualizarLinea_ManualConCc_PatchParcialSigueSinMandarCc()
    {
        // Regresión de la semántica PATCH parcial: la guarda es de POST-ESTADO,
        // no de input. Quien solo cambia el precio manda centroCostoId=null
        // ("no tocar") y el CC existente se conserva.
        var ccOriginal = Guid.CreateVersion7();
        var oc = NewOcConLinea(out var linea, centroCostoId: ccOriginal);

        oc.ActualizarLinea(linea.Id, precioUnitario: 250m);

        Assert.Equal(ccOriginal, linea.CentroCostoId);
        Assert.Equal(250m, linea.PrecioUnitario);
    }

    [Fact]
    public void ActualizarLinea_HeredadaSinCc_NoLaAlcanzaLaGuardaDeManual()
    {
        // La guarda de PR3.1 es solo para líneas manuales. Una heredada legada
        // sin CC se sigue pudiendo editar — su CC lo arregla la RQ, y el campo
        // es read-only aquí (LINEA_OC_CC_HEREDADO_INMUTABLE).
        var oc = NewOcConLineaHeredada(out var linea, centroCostoId: null);
        Assert.Null(linea.CentroCostoId);

        oc.ActualizarLinea(linea.Id, precioUnitario: 300m);

        Assert.Equal(300m, linea.PrecioUnitario);
    }

    [Fact]
    public void ActualizarLinea_CambiaCantidad_RecalculaImpuestos()
    {
        var oc = NewOcConLinea(out var linea);
        // Subtotal inicial: 1000, IVA 160
        Assert.Equal(160m, linea.IvaImporte);

        oc.ActualizarLinea(linea.Id, cantidad: 20m);
        // Subtotal nuevo: 2000, IVA 320
        Assert.Equal(2000m, linea.SubtotalLinea);
        Assert.Equal(320m, linea.IvaImporte);
    }

    [Fact]
    public void ActualizarLinea_CambiaDescuento_RecalculaImpuestos()
    {
        var oc = NewOcConLinea(out var linea);
        oc.ActualizarLinea(
            linea.Id,
            descuento: new DescuentoLinea(DescuentoTipo.Porcentaje, 10m));
        // bruto 1000, descuento 100, subtotal 900, IVA 144
        Assert.Equal(900m, linea.SubtotalLinea);
        Assert.Equal(144m, linea.IvaImporte);
    }

    [Fact]
    public void ActualizarLinea_LineaInexistente_Lanza()
    {
        var oc = NewOcConLinea(out _);
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.ActualizarLinea(Guid.CreateVersion7(), cantidad: 5m));
        Assert.Equal("OC_LINEA_NO_ENCONTRADA", ex.Code);
    }

    [Fact]
    public void EliminarLinea_OK()
    {
        var oc = NewOcConLinea(out var linea);
        oc.EliminarLinea(linea.Id);
        Assert.Empty(oc.Lineas);
    }

    [Fact]
    public void EliminarLinea_Inexistente_Lanza()
    {
        var oc = NewOcConLinea(out _);
        var ex = Assert.Throws<BusinessRuleException>(() => oc.EliminarLinea(Guid.CreateVersion7()));
        Assert.Equal("OC_LINEA_NO_ENCONTRADA", ex.Code);
    }

    [Fact]
    public void ActualizarTextoAdicional_OK()
    {
        var oc = NewOcConLinea(out var linea);
        oc.ActualizarTextoAdicionalLinea(linea.Id, "Nota de operación");
        Assert.Equal("Nota de operación", linea.TextoAdicional);
    }

    [Fact]
    public void ActualizarTextoAdicional_NullClears()
    {
        var oc = NewOcConLinea(out var linea);
        oc.ActualizarTextoAdicionalLinea(linea.Id, "Algo");
        oc.ActualizarTextoAdicionalLinea(linea.Id, null);
        Assert.Null(linea.TextoAdicional);
    }

    [Fact]
    public void ActualizarTextoAdicional_DemasiadoLargo_Lanza()
    {
        var oc = NewOcConLinea(out var linea);
        var ex = Assert.Throws<BusinessRuleException>(() =>
            oc.ActualizarTextoAdicionalLinea(linea.Id, new string('x', 501)));
        Assert.Equal("LINEA_OC_TEXTO_ADICIONAL_DEMASIADO_LARGO", ex.Code);
    }

    [Fact]
    public void ActualizarLinea_ActivoFijo_marca_y_null_conserva()
    {
        var oc = NewOcConLinea(out var linea);
        var clase = Guid.CreateVersion7();
        oc.ActualizarLinea(linea.Id, esActivoFijo: true, claseActivoId: clase);
        Assert.True(linea.EsActivoFijo);
        Assert.Equal(clase, linea.ClaseActivoId);

        oc.ActualizarLinea(linea.Id, precioUnitario: 120m);
        Assert.True(linea.EsActivoFijo);
        Assert.Equal(clase, linea.ClaseActivoId);

        oc.ActualizarLinea(linea.Id, esActivoFijo: false);
        Assert.False(linea.EsActivoFijo);
        Assert.Null(linea.ClaseActivoId);
    }

    [Fact]
    public void ActualizarLinea_ActivoFijo_sin_clase_o_con_recepcion_lanza()
    {
        var oc = NewOcConLinea(out var linea);
        var ex = Assert.Throws<BusinessRuleException>(() => oc.ActualizarLinea(linea.Id, esActivoFijo: true));
        Assert.Equal("COMPRA_ACTIVO_INVALIDO", ex.Code);

        typeof(LineaOrdenCompra).GetProperty(nameof(LineaOrdenCompra.CantidadRecibida))!.SetValue(linea, 1m);
        ex = Assert.Throws<BusinessRuleException>(() => oc.ActualizarLinea(linea.Id, esActivoFijo: true, claseActivoId: Guid.CreateVersion7()));
        Assert.Equal("LINEA_OC_CON_RECEPCION_NO_ESTRUCTURAL", ex.Code);
    }

    [Fact]
    public void ActivoFijo_en_servicio_lanza_al_actualizar_y_al_agregar()
    {
        var oc = NewOcConLinea(out var linea);
        oc.ActualizarLinea(linea.Id, esActivoFijo: true, claseActivoId: Guid.CreateVersion7());
        var ex = Assert.Throws<BusinessRuleException>(() => oc.ActualizarLinea(linea.Id, esServicio: true));
        Assert.Equal("OC_ACTIVO_SERVICIO", ex.Code);

        ex = Assert.Throws<BusinessRuleException>(() => oc.AgregarLineaManual(Guid.CreateVersion7(), Guid.CreateVersion7(), 1m, "PZA", 10m,
            Guid.CreateVersion7(), esServicio: true, centroCostoId: Guid.CreateVersion7(), esActivoFijo: true, claseActivoId: Guid.CreateVersion7()));
        Assert.Equal("OC_ACTIVO_SERVICIO", ex.Code);
    }

    // --- F1-MP-05 (A4.2): lote del proveedor ---

    [Fact]
    public void LoteProveedor_se_recorta_y_vacio_queda_null()
    {
        var oc = NewOcConLinea(out var linea);
        oc.ActualizarLinea(linea.Id, loteProveedor: " L-7781 ");
        Assert.Equal("L-7781", linea.LoteProveedor);

        oc.ActualizarLinea(linea.Id, loteProveedor: "   ");
        Assert.Null(linea.LoteProveedor);
    }

    [Fact]
    public void LoteProveedor_de_50_caracteres_pasa_y_de_51_lanza()
    {
        var oc = NewOcConLinea(out var linea);
        oc.ActualizarLinea(linea.Id, loteProveedor: new string('L', 50));
        Assert.Equal(50, linea.LoteProveedor!.Length);

        var ex = Assert.Throws<BusinessRuleException>(() => oc.ActualizarLinea(linea.Id, loteProveedor: new string('L', 51)));
        Assert.Equal("LINEA_OC_LOTE_LARGO", ex.Code);
        Assert.Equal(50, linea.LoteProveedor!.Length);
    }

    [Fact]
    public void LoteProveedor_limpiar_lo_deja_null_y_sin_tocar_lo_conserva()
    {
        var oc = NewOcConLinea(out var linea);
        oc.ActualizarLinea(linea.Id, loteProveedor: "L-1");
        oc.ActualizarLinea(linea.Id, cantidad: 12m);
        Assert.Equal("L-1", linea.LoteProveedor);

        oc.ActualizarLinea(linea.Id, limpiarLoteProveedor: true);
        Assert.Null(linea.LoteProveedor);
    }

    [Fact]
    public void LoteProveedor_al_agregar_linea_manual_se_normaliza_y_valida()
    {
        var oc = NewOcConLinea(out _);
        var nueva = oc.AgregarLineaManual(Guid.CreateVersion7(), Guid.CreateVersion7(), 1m, "PZA", 10m,
            Guid.CreateVersion7(), centroCostoId: Guid.CreateVersion7(), loteProveedor: "  A-9 ");
        Assert.Equal("A-9", nueva.LoteProveedor);

        var ex = Assert.Throws<BusinessRuleException>(() => oc.AgregarLineaManual(Guid.CreateVersion7(), Guid.CreateVersion7(), 1m, "PZA", 10m,
            Guid.CreateVersion7(), centroCostoId: Guid.CreateVersion7(), loteProveedor: new string('L', 51)));
        Assert.Equal("LINEA_OC_LOTE_LARGO", ex.Code);
    }

    [Theory]
    [InlineData(EstadoOrdenCompra.Autorizada)]
    [InlineData(EstadoOrdenCompra.Cancelada)]
    public void LoteProveedor_no_se_edita_fuera_de_Borrador_o_Rechazada(EstadoOrdenCompra estado)
    {
        var oc = NewOcConLinea(out var linea);
        oc.ActualizarLinea(linea.Id, loteProveedor: "L-1");
        typeof(OrdenCompra).GetProperty(nameof(OrdenCompra.Estado))!.SetValue(oc, estado);

        var ex = Assert.Throws<BusinessRuleException>(() => oc.ActualizarLinea(linea.Id, loteProveedor: "L-2"));
        Assert.Equal("OC_LINEAS_SOLO_EN_BORRADOR_O_RECHAZADA", ex.Code);
        ex = Assert.Throws<BusinessRuleException>(() => oc.ActualizarLinea(linea.Id, limpiarLoteProveedor: true));
        Assert.Equal("OC_LINEAS_SOLO_EN_BORRADOR_O_RECHAZADA", ex.Code);
        Assert.Equal("L-1", linea.LoteProveedor);
    }

    [Fact]
    public void LoteProveedor_se_edita_en_Rechazada()
    {
        var oc = NewOcConLinea(out var linea);
        typeof(OrdenCompra).GetProperty(nameof(OrdenCompra.Estado))!.SetValue(oc, EstadoOrdenCompra.Rechazada);
        oc.ActualizarLinea(linea.Id, loteProveedor: "R-1");
        Assert.Equal("R-1", linea.LoteProveedor);
    }
}
