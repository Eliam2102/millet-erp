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
            motivoSinRequisicion: "Test edicion linea");
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
            sinRequisicionPrevia: false);
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
}
